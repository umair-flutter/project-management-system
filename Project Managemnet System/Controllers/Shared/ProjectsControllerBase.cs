using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Infrastructure;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

namespace Project_Managemnet_System.Controllers.Shared;

public abstract class ProjectsControllerBase(
    ApplicationDbContext db, ProjectAccess access, ProjectMembership membership, FileStorage files) : PortalController
{
    // ---------- List ----------

    public async Task<IActionResult> Index(string? q) =>
        View("Index", await LoadListAsync(archived: false, q));

    public async Task<IActionResult> Archived(string? q) =>
        View("Index", await LoadListAsync(archived: true, q));

    async Task<ProjectListVm> LoadListAsync(bool archived, string? q)
    {
        var query = access.VisibleProjects(User).Where(p => p.IsArchived == archived);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => p.Name.Contains(q));

        var projects = await query
            .Include(p => p.Owner)
            .Include(p => p.Members).ThenInclude(m => m.User)
            .OrderByDescending(p => p.CreatedAt)
            .AsSplitQuery()
            .ToListAsync();

        var ids = projects.Select(p => p.Id).ToList();
        var stats = await db.Todos.Where(t => ids.Contains(t.ProjectId))
            .GroupBy(t => new { t.ProjectId, t.Type })
            .Select(g => new { g.Key.ProjectId, g.Key.Type, Total = g.Count(), Done = g.Count(t => t.Status != TodoStatus.Incomplete) })
            .ToListAsync();

        var cards = projects.Select(p =>
        {
            var task = stats.FirstOrDefault(s => s.ProjectId == p.Id && s.Type == TodoType.Task);
            var bug = stats.FirstOrDefault(s => s.ProjectId == p.Id && s.Type == TodoType.Bug);
            return new ProjectCardVm
            {
                Project = p,
                TaskTotal = task?.Total ?? 0, TaskDone = task?.Done ?? 0,
                BugTotal = bug?.Total ?? 0, BugDone = bug?.Done ?? 0,
                People = ProjectAccess.Assignables(p),
            };
        }).ToList();

        return new ProjectListVm { Projects = cards, Query = q, Archived = archived, CanCreate = !archived && IsAdminPortal && User.CanCreateProjects() };
    }

    // ---------- Details with to-do lists ----------

    public async Task<IActionResult> Details(int id, string filter = TodoFilters.All, TodoPriority? priority = null, string? q = null, bool mine = false)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();

        var all = await db.Todos.Where(t => t.ProjectId == id)
            .Include(t => t.Assignee)
            .Include(t => t.Comments)
            .AsSplitQuery()
            .ToListAsync();

        var counts = TodoFilters.Tabs.ToDictionary(t => t.Key, t => all.Count(x => Matches(x, t.Key)));

        IEnumerable<TodoItem> list = all.Where(t => Matches(t, filter));
        if (priority is not null) list = list.Where(t => t.Priority == priority);
        if (mine) list = list.Where(t => t.AssigneeId == perm.UserId);
        if (!string.IsNullOrWhiteSpace(q)) list = list.Where(t => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase));

        var vm = new ProjectDetailsVm
        {
            Access = perm,
            Todos = list
                .OrderBy(t => t.Status)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ToList(),
            Filter = TodoFilters.Tabs.Any(t => t.Key == filter) ? filter : TodoFilters.All,
            Priority = priority,
            Query = q,
            MineOnly = mine,
            Counts = counts,
        };
        return View("Details", vm);
    }

    static bool Matches(TodoItem t, string filter) => filter switch
    {
        TodoFilters.Assigned => t.AssigneeId != null,
        TodoFilters.Unassigned => t.AssigneeId == null,
        TodoFilters.Completed => t.Status == TodoStatus.Completed,
        TodoFilters.Incomplete => t.AssigneeId != null && t.Status == TodoStatus.Incomplete,
        TodoFilters.Tasks => t.Type == TodoType.Task,
        TodoFilters.Bugs => t.Type == TodoType.Bug,
        TodoFilters.Finished => t.Status == TodoStatus.Finished,
        _ => true,
    };

    // ---------- Create / edit / delete (owner portal) ----------

    [HttpGet]
    public IActionResult Create()
    {
        if (!User.CanCreateProjects()) return Forbid();
        return View("Form", new ProjectFormVm());
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProjectFormVm vm)
    {
        if (!User.CanCreateProjects()) return Forbid();
        if (!ModelState.IsValid) return View("Form", vm);

        var project = new Project { Name = vm.Name.Trim(), Description = vm.Description, OwnerId = UserId };
        try
        {
            if (vm.Icon is { Length: > 0 }) project.IconPath = await files.SaveIconAsync(vm.Icon);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Icon), ex.Message);
            return View("Form", vm);
        }

        db.Projects.Add(project);
        membership.Log(project, UserId, "created the project");
        var invite = await membership.AddByEmailsAsync(project, ProjectMembership.ParseEmails(vm.MemberEmails), false, UserId);
        await db.SaveChangesAsync();

        Flash(InviteMessage($"Project \"{project.Name}\" was created.", invite));
        return RedirectToAction(nameof(Details), new { id = project.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (!perm.CanEditSettings) return Forbid();

        var p = perm.Project;
        return View("Form", new ProjectFormVm { Id = p.Id, Name = p.Name, Description = p.Description, ExistingIconPath = p.IconPath });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ProjectFormVm vm)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (!perm.CanEditSettings) return Forbid();

        var p = perm.Project;
        vm.ExistingIconPath = p.IconPath;
        if (!ModelState.IsValid) return View("Form", vm);

        try
        {
            if (vm.Icon is { Length: > 0 })
            {
                var newPath = await files.SaveIconAsync(vm.Icon);
                files.DeleteIcon(p.IconPath);
                p.IconPath = newPath;
            }
            else if (vm.RemoveIcon)
            {
                files.DeleteIcon(p.IconPath);
                p.IconPath = null;
            }
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Icon), ex.Message);
            return View("Form", vm);
        }

        p.Name = vm.Name.Trim();
        p.Description = vm.Description;
        membership.Log(p, UserId, "updated the project details");
        await db.SaveChangesAsync();
        Flash("Project updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (!perm.CanEditSettings) return Forbid();

        await files.DeleteAttachmentsAsync(db.Attachments.Where(a => a.ProjectId == id));
        files.DeleteIcon(perm.Project.IconPath);
        db.Projects.Remove(perm.Project);
        await db.SaveChangesAsync();
        Flash($"Project \"{perm.Project.Name}\" was deleted.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Archive(int id, bool archive)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (!perm.CanEditSettings) return Forbid();

        var p = perm.Project;
        if (p.IsArchived != archive)
        {
            p.IsArchived = archive;
            p.ArchivedAt = archive ? DateTime.UtcNow : null;
            membership.Log(p, UserId, archive ? "archived the project" : "restored the project from the archive");
            await db.SaveChangesAsync();
        }
        Flash(archive
            ? $"\"{p.Name}\" was archived. It's read-only now and listed under Archived projects."
            : $"\"{p.Name}\" was restored.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------- Project files ----------

    public async Task<IActionResult> Files(int id)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();

        var all = await db.Attachments.Where(a => a.ProjectId == id)
            .Include(a => a.UploadedBy)
            .Include(a => a.TodoItem)
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync();

        return View("Files", new ProjectFilesVm
        {
            Access = perm,
            Documents = all.Where(a => a.TodoItemId == null).ToList(),
            TodoFiles = all.Where(a => a.TodoItemId != null).ToList(),
        });
    }

    [HttpPost]
    public async Task<IActionResult> UploadFiles(int id, List<IFormFile>? documents)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (perm.Project.IsArchived) return ArchivedRedirect(id, nameof(Files));

        try
        {
            var saved = await files.SaveAttachmentsAsync(documents, id, UserId);
            if (saved.Count == 0)
            {
                Flash("Choose at least one file to upload.", "warning");
                return RedirectToAction(nameof(Files), new { id });
            }
            db.Attachments.AddRange(saved);
            membership.Log(perm.Project, UserId, saved.Count == 1
                ? $"uploaded the file \"{saved[0].FileName}\""
                : $"uploaded {saved.Count} files");
            await db.SaveChangesAsync();
            Flash(saved.Count == 1 ? "File uploaded." : $"{saved.Count} files uploaded.");
        }
        catch (InvalidOperationException ex)
        {
            Flash(ex.Message, "danger");
        }
        return RedirectToAction(nameof(Files), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteFile(int id, int fileId)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (perm.Project.IsArchived) return ArchivedRedirect(id, nameof(Files));

        var file = await db.Attachments.FirstOrDefaultAsync(a => a.Id == fileId && a.ProjectId == id);
        if (file is null) return NotFound();
        if (!perm.CanManage && file.UploadedById != UserId) return Forbid();

        await files.DeleteAttachmentsAsync(db.Attachments.Where(a => a.Id == fileId));
        membership.Log(perm.Project, UserId, $"deleted the file \"{file.FileName}\"");
        await db.SaveChangesAsync();
        Flash($"\"{file.FileName}\" was deleted.");
        return RedirectToAction(nameof(Files), new { id });
    }

    // ---------- Progress (activity feed for one project) ----------

    public async Task<IActionResult> Progress(int id, int page = 1)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();

        var feed = await ActivityFeed.LoadAsync(db, db.ActivityLogs.Where(a => a.ProjectId == id), page);
        feed.Access = perm;
        return View("Progress", feed);
    }

    // ---------- Board (kanban) ----------

    public async Task<IActionResult> Board(int id, TodoType? type = null, bool mine = false)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();

        var query = db.Todos.Where(t => t.ProjectId == id);
        if (type is not null) query = query.Where(t => t.Type == type);
        if (mine) query = query.Where(t => t.AssigneeId == perm.UserId);

        var todos = await query.Include(t => t.Assignee).Include(t => t.Comments).AsSplitQuery()
            .OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ToListAsync();
        return View("Board", new BoardVm { Access = perm, Todos = todos, Type = type, MineOnly = mine });
    }

    // ---------- Export ----------

    /// <summary>All of the project's to-dos as a CSV file (opens in Excel).</summary>
    public async Task<IActionResult> Export(int id)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();

        var todos = await db.Todos.Where(t => t.ProjectId == id)
            .Include(t => t.Assignee).Include(t => t.CreatedBy)
            .OrderBy(t => t.Id).ToListAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Id,Title,Type,Priority,Status,Assigned to,Start date,Due date,Created by,Created,Completed,Finished,Description");
        foreach (var t in todos)
        {
            string[] cells =
            [
                t.Id.ToString(), t.Title, t.Type.ToString(), t.Priority.ToString(), Ui.StatusLabel(t.Status),
                t.Assignee?.FullName ?? "", t.StartDate?.ToString("yyyy-MM-dd") ?? "", t.DueDate?.ToString("yyyy-MM-dd") ?? "",
                t.CreatedBy?.FullName ?? "", t.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                t.CompletedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "", t.FinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "",
                t.Description ?? "",
            ];
            sb.AppendLine(string.Join(",", cells.Select(Csv)));
        }

        var name = string.Concat(perm.Project.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"{(name.Length > 0 ? name : "project")} to-dos {DateTime.Now:yyyy-MM-dd}.csv");
    }

    /// <summary>Quotes a CSV cell and stops spreadsheet apps from running it as a formula.</summary>
    static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    IActionResult ArchivedRedirect(int id, string action)
    {
        Flash("This project is archived and read-only. Restore it to make changes.", "warning");
        return RedirectToAction(action, new { id });
    }

    // ---------- Team members ----------

    [HttpGet]
    public async Task<IActionResult> Members(int id)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();

        var open = await db.Todos.Where(t => t.ProjectId == id && t.AssigneeId != null && t.Status == TodoStatus.Incomplete)
            .GroupBy(t => t.AssigneeId!).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return View("Members", new ProjectMembersVm
        {
            Access = perm,
            Invitations = await db.ProjectInvitations.Where(i => i.ProjectId == id).OrderBy(i => i.Email).ToListAsync(),
            OpenTodosByUser = open,
        });
    }

    [HttpPost]
    public async Task<IActionResult> AddMembers(int id, string? emails, bool asProjectAdmin)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (!perm.CanManage) return Forbid();

        var list = ProjectMembership.ParseEmails(emails).ToList();
        if (list.Count == 0)
        {
            Flash("Enter at least one email address.", "warning");
            return RedirectToAction(nameof(Members), new { id });
        }
        // Only the owner or Super Admin may hand out project-admin rights.
        var result = await membership.AddByEmailsAsync(perm.Project, list, asProjectAdmin && perm.CanEditSettings, UserId);
        await db.SaveChangesAsync();
        Flash(InviteMessage("Team updated.", result));
        return RedirectToAction(nameof(Members), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveMember(int id, string userId)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (!perm.CanManage) return Forbid();

        var member = perm.Project.Members.FirstOrDefault(m => m.UserId == userId);
        if (member is null) return NotFound();
        if (member.IsProjectAdmin && !perm.CanEditSettings) return Forbid();

        // Their open to-dos go back to the unassigned pool.
        var open = await db.Todos.Where(t => t.ProjectId == id && t.AssigneeId == userId && t.Status == TodoStatus.Incomplete).ToListAsync();
        open.ForEach(t => t.AssigneeId = null);

        db.ProjectMembers.Remove(member);
        membership.Log(perm.Project, UserId, $"removed {member.User?.FullName} from the project");
        await db.SaveChangesAsync();
        Flash($"{member.User?.FullName} was removed" + (open.Count > 0 ? $"; {open.Count} open to-do(s) are now unassigned." : "."));
        return RedirectToAction(nameof(Members), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> ToggleProjectAdmin(int id, string userId)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (!perm.CanEditSettings) return Forbid();

        var member = perm.Project.Members.FirstOrDefault(m => m.UserId == userId);
        if (member is null) return NotFound();
        member.IsProjectAdmin = !member.IsProjectAdmin;
        membership.Log(perm.Project, UserId, member.IsProjectAdmin
            ? $"made {member.User?.FullName} a project admin"
            : $"removed project admin rights from {member.User?.FullName}");
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Members), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> CancelInvite(int id, int inviteId)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (!perm.CanManage) return Forbid();

        var invite = await db.ProjectInvitations.FirstOrDefaultAsync(i => i.Id == inviteId && i.ProjectId == id);
        if (invite is not null)
        {
            db.ProjectInvitations.Remove(invite);
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Members), new { id });
    }

    /// <summary>Sends the invitation email again with a fresh link (the old link stops working).</summary>
    [HttpPost]
    public async Task<IActionResult> ResendInvite(int id, int inviteId)
    {
        var perm = await access.GetAsync(id, User);
        if (perm is null) return NotFound();
        if (!perm.CanManage) return Forbid();

        var invite = await db.ProjectInvitations.FirstOrDefaultAsync(i => i.Id == inviteId && i.ProjectId == id);
        if (invite is null) return NotFound();
        invite.EmailedAt = null;   // the email job picks it up within a few seconds
        invite.TokenHash = null;
        invite.CreatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Flash($"A new invitation is on its way to {invite.Email}.");
        return RedirectToAction(nameof(Members), new { id });
    }

    static string InviteMessage(string lead, InviteResult r)
    {
        var parts = new List<string> { lead };
        if (r.Added.Count > 0) parts.Add($"Added {r.Added.Count} member(s).");
        if (r.Invited.Count > 0) parts.Add($"Emailed an invitation to {r.Invited.Count} new person(s); they join when they create their account from the link.");
        if (r.Skipped.Count > 0) parts.Add($"Skipped {r.Skipped.Count} already on the team or invited.");
        return string.Join(" ", parts);
    }
}
