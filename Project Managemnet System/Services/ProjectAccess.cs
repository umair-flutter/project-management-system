using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Infrastructure;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Services;

/// <summary>What the current user may do in one project.</summary>
public record ProjectPermission(Project Project, string UserId, bool CanManage, bool CanEditSettings)
{
    /// <summary>Archived projects are read-only for everyone.</summary>
    public bool IsReadOnly => Project.IsArchived;

    /// <summary>Assignees may move their own to-dos between Incomplete and Completed.</summary>
    public bool CanUpdateStatus(TodoItem t) => !IsReadOnly && (CanManage || t.AssigneeId == UserId);

    /// <summary>
    /// Statuses this user may move the to-do to. Managers (owner, Super Admin, project admins)
    /// may move it anywhere; the assignee may only toggle between Incomplete and Completed.
    /// </summary>
    public IReadOnlyList<TodoStatus> AllowedMoves(TodoItem t)
    {
        if (!CanUpdateStatus(t)) return [];
        if (CanManage) return Enum.GetValues<TodoStatus>().Where(s => s != t.Status).ToList();
        return t.Status switch
        {
            TodoStatus.Incomplete => [TodoStatus.Completed],
            TodoStatus.Completed => [TodoStatus.Incomplete],
            _ => [],
        };
    }
}

/// <summary>
/// Central place for the access rules:
/// - Super Admin sees every project.
/// - Admin (project owner) sees projects they own or belong to.
/// - Developer sees projects they are a member of.
/// - Owner, Super Admin, and project admins manage to-dos and members.
/// - Only the owner and Super Admin can edit, archive, or delete the project.
/// </summary>
public class ProjectAccess(ApplicationDbContext db)
{
    public IQueryable<Project> VisibleProjects(ClaimsPrincipal user)
    {
        if (user.IsSuperAdmin()) return db.Projects;
        var uid = user.GetUserId();
        return db.Projects.Where(p => p.OwnerId == uid || p.Members.Any(m => m.UserId == uid));
    }

    public IQueryable<TodoItem> VisibleTodos(ClaimsPrincipal user)
    {
        if (user.IsSuperAdmin()) return db.Todos;
        var uid = user.GetUserId();
        return db.Todos.Where(t => t.Project!.OwnerId == uid || t.Project.Members.Any(m => m.UserId == uid));
    }

    /// <summary>Returns null when the project does not exist or the user cannot see it.</summary>
    public async Task<ProjectPermission?> GetAsync(int projectId, ClaimsPrincipal user)
    {
        var uid = user.GetUserId();
        var project = await db.Projects
            .Include(p => p.Owner)
            .Include(p => p.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return null;

        var isSuper = user.IsSuperAdmin();
        var isOwner = project.OwnerId == uid;
        var member = project.Members.FirstOrDefault(m => m.UserId == uid);
        if (!isSuper && !isOwner && member is null) return null;

        var canEdit = isSuper || isOwner;
        return new ProjectPermission(project, uid, canEdit || member?.IsProjectAdmin == true, canEdit);
    }

    /// <summary>People a to-do in this project can be assigned to: the owner plus active members.</summary>
    public static List<ApplicationUser> Assignables(Project p)
    {
        var people = p.Members.Where(m => m.User is { IsActive: true }).Select(m => m.User!).ToList();
        if (p.Owner is { IsActive: true } owner && people.All(u => u.Id != owner.Id)) people.Insert(0, owner);
        return people.OrderBy(u => u.FullName).ToList();
    }
}
