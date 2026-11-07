using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

namespace Project_Managemnet_System.Controllers.Shared;

/// <summary>The signed-in user's notifications.</summary>
public abstract class NotificationsControllerBase(ApplicationDbContext db) : PortalController
{
    const int PageSize = 30;

    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Max(1, page);
        var uid = UserId;
        var items = await db.Notifications.Where(n => n.UserId == uid)
            .Include(n => n.Actor).Include(n => n.Project)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * PageSize).Take(PageSize + 1)
            .ToListAsync();

        var vm = new NotificationListVm { Page = page, HasMore = items.Count > PageSize, Items = items.Take(PageSize).ToList() };
        return View("Index", vm);
    }

    /// <summary>Marks the notification read and goes to what it is about.</summary>
    public async Task<IActionResult> Open(long id)
    {
        var uid = UserId;
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
        if (n is null) return RedirectToAction(nameof(Index));

        if (!n.IsRead)
        {
            n.IsRead = true;
            await db.SaveChangesAsync();
        }
        if (n.TodoItemId is { } tid && await db.Todos.AnyAsync(t => t.Id == tid))
            return RedirectToAction("Details", "Todos", new { id = tid });
        return RedirectToAction("Details", "Projects", new { id = n.ProjectId });
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllRead(string? returnUrl = null)
    {
        var uid = UserId;
        await db.Notifications.Where(n => n.UserId == uid && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }
}

/// <summary>Everything assigned to the signed-in user, across all their projects.</summary>
public abstract class MyTodosControllerBase(ProjectAccess access) : PortalController
{
    public static readonly (string Key, string Label)[] Tabs =
        [("open", "Open"), ("overdue", "Overdue"), ("completed", "Completed"), ("finished", "Finished"), ("all", "All")];

    public async Task<IActionResult> Index(string filter = "open", TodoPriority? priority = null)
    {
        var uid = UserId;
        var today = DateTime.Today;
        var mine = await access.VisibleTodos(User)
            .Where(t => t.AssigneeId == uid && !t.Project!.IsArchived)
            .Include(t => t.Project)
            .Include(t => t.Comments)
            .AsSplitQuery()
            .ToListAsync();

        bool Match(TodoItem t, string f) => f switch
        {
            "open" => t.Status == TodoStatus.Incomplete,
            "overdue" => t.Status == TodoStatus.Incomplete && t.DueDate < today,
            "completed" => t.Status == TodoStatus.Completed,
            "finished" => t.Status == TodoStatus.Finished,
            _ => true,
        };
        if (Tabs.All(x => x.Key != filter)) filter = "open";

        var list = mine.Where(t => Match(t, filter));
        if (priority is not null) list = list.Where(t => t.Priority == priority);

        return View("Index", new MyTodosVm
        {
            Filter = filter,
            Priority = priority,
            Counts = Tabs.ToDictionary(x => x.Key, x => mine.Count(t => Match(t, x.Key))),
            Todos = list.OrderBy(t => t.Status).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ThenByDescending(t => t.Priority).ToList(),
        });
    }
}

/// <summary>Search across the projects and to-dos the user can see.</summary>
public abstract class SearchControllerBase(ProjectAccess access) : PortalController
{
    public async Task<IActionResult> Index(string? q)
    {
        var vm = new SearchVm { Query = q?.Trim() };
        if (string.IsNullOrWhiteSpace(vm.Query) || vm.Query.Length < 2) return View("Index", vm);

        var term = vm.Query;
        vm.Projects = await access.VisibleProjects(User)
            .Where(p => p.Name.Contains(term) || (p.Description != null && p.Description.Contains(term)))
            .OrderBy(p => p.IsArchived).ThenBy(p => p.Name).Take(20).ToListAsync();
        vm.Todos = await access.VisibleTodos(User)
            .Where(t => t.Title.Contains(term) || (t.Description != null && t.Description.Contains(term)))
            .Include(t => t.Project).Include(t => t.Assignee)
            .OrderBy(t => t.Status).ThenByDescending(t => t.CreatedAt).Take(50).ToListAsync();
        return View("Index", vm);
    }
}
