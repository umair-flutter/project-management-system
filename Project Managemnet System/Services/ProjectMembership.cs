using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Services;

public record InviteResult(List<string> Added, List<string> Invited, List<string> Skipped);

/// <summary>Adds people to projects by email and records project activity.</summary>
public class ProjectMembership(ApplicationDbContext db)
{
    public static IEnumerable<string> ParseEmails(string? raw) =>
        (raw ?? "")
            .Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.Contains('@'))
            .Distinct();

    /// <summary>
    /// Existing users become members right away. Unknown emails are stored as invitations
    /// and turn into memberships when an account with that email is created.
    /// Changes are staged; the caller saves.
    /// </summary>
    public async Task<InviteResult> AddByEmailsAsync(Project project, IEnumerable<string> emails, bool asProjectAdmin, string inviterId)
    {
        var result = new InviteResult([], [], []);
        foreach (var email in emails)
        {
            var normalized = email.ToUpperInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);
            if (user is not null)
            {
                var already = user.Id == project.OwnerId
                    || project.Members.Any(m => m.UserId == user.Id)
                    || await db.ProjectMembers.AnyAsync(m => m.ProjectId == project.Id && project.Id != 0 && m.UserId == user.Id);
                if (already) { result.Skipped.Add(email); continue; }

                project.Members.Add(new ProjectMember { UserId = user.Id, IsProjectAdmin = asProjectAdmin });
                Log(project, inviterId, $"added {user.FullName} to the project");
                db.Notifications.Add(new Notification
                {
                    Project = project, ProjectId = project.Id, UserId = user.Id, ActorId = inviterId,
                    Message = $"You were added to the project \"{project.Name}\"" + (asProjectAdmin ? " as a project admin" : ""),
                });
                result.Added.Add(email);
            }
            else
            {
                var pending = project.Invitations.Any(i => i.Email == email)
                    || (project.Id != 0 && await db.ProjectInvitations.AnyAsync(i => i.ProjectId == project.Id && i.Email == email));
                if (pending) { result.Skipped.Add(email); continue; }

                project.Invitations.Add(new ProjectInvitation { Email = email, IsProjectAdmin = asProjectAdmin, InvitedById = inviterId });
                result.Invited.Add(email);
            }
        }
        return result;
    }

    /// <summary>Turns pending invitations for this user's email into memberships, then saves.</summary>
    public async Task ApplyInvitationsAsync(ApplicationUser user)
    {
        var email = (user.Email ?? "").ToLowerInvariant();
        var invites = await db.ProjectInvitations.Include(i => i.Project).Where(i => i.Email == email).ToListAsync();
        foreach (var inv in invites)
        {
            var exists = await db.ProjectMembers.AnyAsync(m => m.ProjectId == inv.ProjectId && m.UserId == user.Id);
            if (!exists)
            {
                db.ProjectMembers.Add(new ProjectMember { ProjectId = inv.ProjectId, UserId = user.Id, IsProjectAdmin = inv.IsProjectAdmin });
                db.ActivityLogs.Add(new ActivityLog { ProjectId = inv.ProjectId, UserId = user.Id, Message = "joined the project from an invitation" });
                db.Notifications.Add(new Notification
                {
                    ProjectId = inv.ProjectId, UserId = user.Id, ActorId = inv.InvitedById,
                    Message = $"You were added to the project \"{inv.Project?.Name}\"" + (inv.IsProjectAdmin ? " as a project admin" : ""),
                });
            }
            db.ProjectInvitations.Remove(inv);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Stages an activity entry. Works for new projects too (uses the navigation).</summary>
    public void Log(Project project, string userId, string message, int? todoId = null) =>
        db.ActivityLogs.Add(new ActivityLog { Project = project, ProjectId = project.Id, UserId = userId, Message = message, TodoItemId = todoId });

    public void Log(int projectId, string userId, string message, int? todoId = null) =>
        db.ActivityLogs.Add(new ActivityLog { ProjectId = projectId, UserId = userId, Message = message, TodoItemId = todoId });
}
