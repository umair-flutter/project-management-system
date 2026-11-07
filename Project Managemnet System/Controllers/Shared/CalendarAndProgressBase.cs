using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

namespace Project_Managemnet_System.Controllers.Shared;

/// <summary>
/// Calendar of to-dos across every project the user can see. The page loads events from
/// <see cref="Events"/> for whatever range is on screen (day, week, month, or year).
/// </summary>
public abstract class CalendarControllerBase(ProjectAccess access) : PortalController
{
    public async Task<IActionResult> Index(int? projectId, bool mine = false) =>
        View("Index", new CalendarVm
        {
            Projects = await access.VisibleProjects(User).Where(p => !p.IsArchived).OrderBy(p => p.Name).ToListAsync(),
            ProjectId = projectId,
            MineOnly = mine,
        });

    [HttpGet]
    public async Task<IActionResult> Events(DateTimeOffset start, DateTimeOffset end, int? projectId, bool mine = false)
    {
        var from = start.Date;
        var to = end.Date;
        var query = access.VisibleTodos(User)
            .Where(t => !t.Project!.IsArchived && t.DueDate != null)
            // Overlaps the visible range: begins before its end and is due on/after its start.
            .Where(t => (t.StartDate ?? t.DueDate) < to && t.DueDate >= from);
        if (projectId is not null) query = query.Where(t => t.ProjectId == projectId);
        if (mine)
        {
            var uid = UserId;
            query = query.Where(t => t.AssigneeId == uid);
        }

        var todos = await query
            .Select(t => new
            {
                t.Id, t.Title, t.Type, t.Priority, t.Status, t.StartDate, t.DueDate,
                Project = t.Project!.Name,
                Assignee = t.Assignee != null ? t.Assignee.FullName : null,
            })
            .Take(2000)
            .ToListAsync();

        var events = todos.Select(t =>
        {
            var begin = t.StartDate is { } s && s <= t.DueDate!.Value ? s : t.DueDate!.Value;
            var color = t.Status != TodoStatus.Incomplete ? "#94a3b8"
                : t.Priority switch { TodoPriority.High => "#dc2626", TodoPriority.Medium => "#d97706", _ => "#059669" };
            return new
            {
                id = t.Id,
                title = (t.Type == TodoType.Bug ? "Bug: " : "") + t.Title,
                start = begin.ToString("yyyy-MM-dd"),
                end = t.DueDate!.Value.AddDays(1).ToString("yyyy-MM-dd"), // end is exclusive for all-day events
                allDay = true,
                url = Url.Action("Details", "Todos", new { id = t.Id }),
                backgroundColor = color,
                borderColor = color,
                classNames = t.Status != TodoStatus.Incomplete ? new[] { "pm-event-done" } : [],
                extendedProps = new
                {
                    project = t.Project,
                    assignee = t.Assignee ?? "Unassigned",
                    priority = t.Priority.ToString(),
                    status = Infrastructure.Ui.StatusLabel(t.Status),
                    due = t.DueDate!.Value.ToString("dd MMM yyyy"),
                },
            };
        });
        return Json(events);
    }
}

/// <summary>What has been happening across the user's projects.</summary>
public abstract class ProgressControllerBase(ApplicationDbContext db, ProjectAccess access) : PortalController
{
    public async Task<IActionResult> Index(int? projectId, int page = 1)
    {
        var projects = await access.VisibleProjects(User).OrderBy(p => p.IsArchived).ThenBy(p => p.Name).ToListAsync();
        var ids = projects.Select(p => p.Id).ToList();
        if (projectId is not null && ids.Contains(projectId.Value)) ids = [projectId.Value];
        else projectId = null;

        var feed = await ActivityFeed.LoadAsync(db, db.ActivityLogs.Where(a => ids.Contains(a.ProjectId)), page);
        feed.Projects = projects;
        feed.ProjectId = projectId;
        return View("Index", feed);
    }
}
