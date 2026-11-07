using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Infrastructure;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

namespace Project_Managemnet_System.Controllers.Shared;

public abstract class DashboardControllerBase(ApplicationDbContext db, ProjectAccess access) : PortalController
{
    public async Task<IActionResult> Index()
    {
        var projects = access.VisibleProjects(User).Where(p => !p.IsArchived);
        var todos = access.VisibleTodos(User).Where(t => !t.Project!.IsArchived);

        var vm = new DashboardVm
        {
            ProjectCount = await projects.CountAsync(),
            TaskCount = await todos.CountAsync(t => t.Type == TodoType.Task),
            BugCount = await todos.CountAsync(t => t.Type == TodoType.Bug),
            Unassigned = await todos.CountAsync(t => t.AssigneeId == null && t.Status == TodoStatus.Incomplete),
            Incomplete = await todos.CountAsync(t => t.AssigneeId != null && t.Status == TodoStatus.Incomplete),
            Completed = await todos.CountAsync(t => t.Status == TodoStatus.Completed),
            Finished = await todos.CountAsync(t => t.Status == TodoStatus.Finished),
            Overdue = await todos.CountAsync(t => t.Status == TodoStatus.Incomplete && t.DueDate < DateTime.Today),
        };

        if (User.IsSuperAdmin())
        {
            vm.MemberCount = await db.Users.CountAsync(u => u.IsActive);
        }
        else
        {
            var ids = await projects.Select(p => p.Id).ToListAsync();
            var memberIds = await db.ProjectMembers.Where(m => ids.Contains(m.ProjectId)).Select(m => m.UserId).ToListAsync();
            var ownerIds = await projects.Select(p => p.OwnerId).ToListAsync();
            vm.MemberCount = memberIds.Concat(ownerIds).Distinct().Count();
        }

        vm.ProjectProgress = await projects
            .OrderByDescending(p => p.CreatedAt)
            .Take(8)
            .Select(p => new ProjectProgressRow(p.Id, p.Name, p.Todos.Count(), p.Todos.Count(t => t.Status != TodoStatus.Incomplete)))
            .ToListAsync();

        // Developers see their own upcoming work; admins see the whole team's.
        var due = todos.Where(t => t.Status == TodoStatus.Incomplete && t.DueDate != null);
        vm.TopDueIsMine = !User.CanCreateProjects();
        if (vm.TopDueIsMine)
        {
            var uid = UserId;
            due = due.Where(t => t.AssigneeId == uid);
        }
        vm.TopDue = await due.Include(t => t.Project).Include(t => t.Assignee)
            .OrderBy(t => t.DueDate).ThenByDescending(t => t.Priority).Take(8).ToListAsync();

        var activeIds = await projects.Select(p => p.Id).ToListAsync();
        vm.RecentActivity = await ActivityFeed.LoadAsync(db, db.ActivityLogs.Where(a => activeIds.Contains(a.ProjectId)), 1, pageSize: 8);

        return View("Index", vm);
    }

    [AllowAnonymous]
    public IActionResult Error() => View("Error");
}
