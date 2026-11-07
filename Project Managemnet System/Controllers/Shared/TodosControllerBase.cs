using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Infrastructure;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

namespace Project_Managemnet_System.Controllers.Shared;

public abstract class TodosControllerBase(
    ApplicationDbContext db, ProjectAccess access, ProjectMembership membership, FileStorage files, Notifier notifier) : PortalController
{
    async Task<(TodoItem? todo, ProjectPermission? perm)> LoadAsync(int id)
    {
        var projectId = await db.Todos.Where(t => t.Id == id).Select(t => (int?)t.ProjectId).FirstOrDefaultAsync();
        if (projectId is null) return (null, null);
        var perm = await access.GetAsync(projectId.Value, User);
        if (perm is null) return (null, null);
        var todo = await db.Todos.FirstAsync(t => t.Id == id);
        return (todo, perm);
    }

    string Me(ProjectPermission perm) => ProjectAccess.Assignables(perm.Project).FirstOrDefault(u => u.Id == UserId)?.FullName ?? "Someone";

    // ---------- Details ----------

    public async Task<IActionResult> Details(int id)
    {
        var (_, perm) = await LoadAsync(id);
        if (perm is null) return NotFound();

        var todo = await db.Todos.Where(t => t.Id == id)
            .Include(t => t.Assignee).Include(t => t.CreatedBy)
            .Include(t => t.Attachments.Where(a => a.TodoCommentId == null))
            .Include(t => t.Comments).ThenInclude(c => c.Author)
            .Include(t => t.Comments).ThenInclude(c => c.Attachments)
            .AsSplitQuery()
            .FirstAsync();
        todo.Comments = todo.Comments.OrderBy(c => c.CreatedAt).ToList();

        return View("Details", new TodoDetailsVm { Todo = todo, Access = perm, Assignables = ProjectAccess.Assignables(perm.Project) });
    }

    // ---------- Create / edit ----------

    [HttpGet]
    public async Task<IActionResult> Create(int projectId, TodoType type = TodoType.Task)
    {
        var perm = await access.GetAsync(projectId, User);
        if (perm is null) return NotFound();
        if (!perm.CanManage) return Forbid();
        if (perm.Project.IsArchived) return ArchivedRedirect(projectId);

        return View("Form", Fill(new TodoFormVm { ProjectId = projectId, Type = type }, perm));
    }

    [HttpPost]
    public async Task<IActionResult> Create(TodoFormVm vm)
    {
        var perm = await access.GetAsync(vm.ProjectId, User);
        if (perm is null) return NotFound();
        if (!perm.CanManage) return Forbid();
        if (perm.Project.IsArchived) return ArchivedRedirect(vm.ProjectId);

        Validate(vm, perm);
        if (!ModelState.IsValid) return View("Form", Fill(vm, perm));

        var todo = new TodoItem { ProjectId = vm.ProjectId, CreatedById = UserId };
        Apply(vm, todo);
        try
        {
            todo.Attachments.AddRange(await files.SaveAttachmentsAsync(vm.Files, vm.ProjectId, UserId));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Files), ex.Message);
            return View("Form", Fill(vm, perm));
        }

        db.Todos.Add(todo);
        await db.SaveChangesAsync();

        var who = AssigneeName(perm, todo.AssigneeId);
        membership.Log(vm.ProjectId, UserId, $"added the {todo.Type.ToString().ToLower()} \"{todo.Title}\"" + (who is null ? "" : $" and assigned it to {who}"), todo.Id);
        notifier.Notify(todo.AssigneeId, UserId, todo.ProjectId, $"{Me(perm)} assigned you the {todo.Type.ToString().ToLower()} \"{todo.Title}\"", todo.Id);
        await db.SaveChangesAsync();

        Flash($"{todo.Type} added.");
        return RedirectToAction("Details", "Projects", new { id = vm.ProjectId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var (todo, perm) = await LoadAsync(id);
        if (todo is null || perm is null) return NotFound();
        if (!perm.CanManage) return Forbid();
        if (perm.IsReadOnly) return ReadOnlyRedirect(id);

        var vm = new TodoFormVm
        {
            Id = todo.Id, ProjectId = todo.ProjectId, Title = todo.Title, Description = todo.Description,
            Type = todo.Type, Priority = todo.Priority, StartDate = todo.StartDate, DueDate = todo.DueDate, AssigneeId = todo.AssigneeId,
        };
        return View("Form", Fill(vm, perm));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, TodoFormVm vm)
    {
        var (todo, perm) = await LoadAsync(id);
        if (todo is null || perm is null) return NotFound();
        if (!perm.CanManage) return Forbid();
        if (perm.IsReadOnly) return ReadOnlyRedirect(id);

        vm.Id = id;
        vm.ProjectId = todo.ProjectId;
        Validate(vm, perm);
        if (!ModelState.IsValid) return View("Form", Fill(vm, perm));

        var previousAssignee = todo.AssigneeId;
        Apply(vm, todo);
        try
        {
            todo.Attachments.AddRange(await files.SaveAttachmentsAsync(vm.Files, todo.ProjectId, UserId));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Files), ex.Message);
            return View("Form", Fill(vm, perm));
        }

        if (previousAssignee != todo.AssigneeId)
        {
            var who = AssigneeName(perm, todo.AssigneeId);
            membership.Log(todo.ProjectId, UserId, who is null ? $"unassigned \"{todo.Title}\"" : $"assigned \"{todo.Title}\" to {who}", todo.Id);
            notifier.Notify(todo.AssigneeId, UserId, todo.ProjectId, $"{Me(perm)} assigned you \"{todo.Title}\"", todo.Id);
            notifier.Notify(previousAssignee, UserId, todo.ProjectId, $"{Me(perm)} took you off \"{todo.Title}\"", todo.Id);
        }
        else
        {
            membership.Log(todo.ProjectId, UserId, $"updated \"{todo.Title}\"", todo.Id);
        }
        await db.SaveChangesAsync();
        Flash("Changes saved.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var (todo, perm) = await LoadAsync(id);
        if (todo is null || perm is null) return NotFound();
        if (!perm.CanManage) return Forbid();
        if (perm.IsReadOnly) return ReadOnlyRedirect(id);

        await files.DeleteAttachmentsAsync(db.Attachments.Where(a => a.TodoItemId == id || a.TodoComment!.TodoItemId == id));
        db.Todos.Remove(todo);
        membership.Log(todo.ProjectId, UserId, $"deleted \"{todo.Title}\"");
        await db.SaveChangesAsync();
        Flash($"\"{todo.Title}\" was deleted.");
        return RedirectToAction("Details", "Projects", new { id = todo.ProjectId });
    }

    // ---------- Status ----------

    [HttpPost]
    public async Task<IActionResult> SetStatus(int id, TodoStatus status, string? returnUrl = null)
    {
        var (todo, perm) = await LoadAsync(id);
        if (todo is null || perm is null) return NotFound();
        if (perm.IsReadOnly) return ReadOnlyRedirect(id);
        if (todo.Status != status && !perm.AllowedMoves(todo).Contains(status)) return Forbid();

        await ApplyStatusAsync(todo, perm, status);
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Used by the board's drag-and-drop. Returns JSON instead of redirecting.</summary>
    [HttpPost]
    public async Task<IActionResult> Move(int id, TodoStatus status)
    {
        var (todo, perm) = await LoadAsync(id);
        if (todo is null || perm is null) return NotFound(new { error = "This to-do no longer exists." });
        if (todo.Status != status && !perm.AllowedMoves(todo).Contains(status))
            return StatusCode(403, new { error = perm.IsReadOnly ? "This project is archived." : "You can't move this to-do there." });

        await ApplyStatusAsync(todo, perm, status);
        return Json(new { ok = true, status = status.ToString(), allowed = perm.AllowedMoves(todo).Select(s => s.ToString()) });
    }

    async Task ApplyStatusAsync(TodoItem todo, ProjectPermission perm, TodoStatus status)
    {
        if (todo.Status == status) return;
        todo.Status = status;
        todo.CompletedAt = status == TodoStatus.Incomplete ? null : todo.CompletedAt ?? DateTime.UtcNow;
        todo.FinishedAt = status == TodoStatus.Finished ? DateTime.UtcNow : null;

        var verb = status switch { TodoStatus.Completed => "completed", TodoStatus.Finished => "finished", _ => "reopened" };
        membership.Log(todo.ProjectId, UserId, $"{verb} \"{todo.Title}\"", todo.Id);

        var me = Me(perm);
        var msg = $"{me} {verb} \"{todo.Title}\"";
        if (status == TodoStatus.Completed)
        {
            // Whoever needs to review it: the creator, the owner, and project admins.
            var reviewers = perm.Project.Members.Where(m => m.IsProjectAdmin).Select(m => m.UserId)
                .Append(perm.Project.OwnerId).Append(todo.CreatedById);
            notifier.Notify(reviewers, UserId, todo.ProjectId, msg, todo.Id);
        }
        else
        {
            notifier.Notify([todo.AssigneeId, todo.CreatedById], UserId, todo.ProjectId, msg, todo.Id);
        }
        await db.SaveChangesAsync();
    }

    // ---------- Comments ----------

    [HttpPost]
    public async Task<IActionResult> Comment(int id, string? body, List<IFormFile>? attachments)
    {
        var (todo, perm) = await LoadAsync(id);
        if (todo is null || perm is null) return NotFound();
        if (perm.IsReadOnly) return ReadOnlyRedirect(id);

        var hasFiles = attachments?.Any(f => f.Length > 0) == true;
        if (string.IsNullOrWhiteSpace(body) && !hasFiles)
        {
            Flash("Write a comment or attach a file.", "warning");
            return RedirectToAction(nameof(Details), new { id });
        }

        var comment = new TodoComment { TodoItemId = id, AuthorId = UserId, Body = body?.Trim() ?? "" };
        try
        {
            foreach (var a in await files.SaveAttachmentsAsync(attachments, todo.ProjectId, UserId))
            {
                a.TodoItemId = id;
                comment.Attachments.Add(a);
            }
        }
        catch (InvalidOperationException ex)
        {
            Flash(ex.Message, "danger");
            return RedirectToAction(nameof(Details), new { id });
        }

        db.TodoComments.Add(comment);
        membership.Log(todo.ProjectId, UserId, $"commented on \"{todo.Title}\"", todo.Id);

        // Everyone involved in the conversation hears about it.
        var earlier = await db.TodoComments.Where(c => c.TodoItemId == id).Select(c => c.AuthorId).Distinct().ToListAsync();
        notifier.Notify(earlier.Append(todo.AssigneeId).Append(todo.CreatedById), UserId, todo.ProjectId,
            $"{Me(perm)} commented on \"{todo.Title}\"", todo.Id);

        await db.SaveChangesAsync();
        return Redirect(Url.Action(nameof(Details), new { id }) + "#comment-" + comment.Id);
    }

    [HttpPost]
    public async Task<IActionResult> EditComment(int id, int commentId, string? body)
    {
        var (todo, perm) = await LoadAsync(id);
        if (todo is null || perm is null) return NotFound();
        if (perm.IsReadOnly) return ReadOnlyRedirect(id);

        var comment = await db.TodoComments.Include(c => c.Attachments).FirstOrDefaultAsync(c => c.Id == commentId && c.TodoItemId == id);
        if (comment is null) return NotFound();
        if (comment.AuthorId != UserId) return Forbid();
        if (string.IsNullOrWhiteSpace(body) && comment.Attachments.Count == 0)
        {
            Flash("A comment can't be empty. Delete it instead.", "warning");
            return RedirectToAction(nameof(Details), new { id });
        }

        comment.Body = body?.Trim() ?? "";
        comment.EditedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Redirect(Url.Action(nameof(Details), new { id }) + "#comment-" + comment.Id);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteComment(int id, int commentId)
    {
        var (todo, perm) = await LoadAsync(id);
        if (todo is null || perm is null) return NotFound();
        if (perm.IsReadOnly) return ReadOnlyRedirect(id);

        var comment = await db.TodoComments.FirstOrDefaultAsync(c => c.Id == commentId && c.TodoItemId == id);
        if (comment is null) return NotFound();
        if (comment.AuthorId != UserId && !perm.CanManage) return Forbid();

        await files.DeleteAttachmentsAsync(db.Attachments.Where(a => a.TodoCommentId == commentId));
        db.TodoComments.Remove(comment);
        await db.SaveChangesAsync();
        Flash("Comment deleted.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAttachment(int id, int fileId)
    {
        var (todo, perm) = await LoadAsync(id);
        if (todo is null || perm is null) return NotFound();
        if (perm.IsReadOnly) return ReadOnlyRedirect(id);

        var file = await db.Attachments.FirstOrDefaultAsync(a => a.Id == fileId && a.TodoItemId == id);
        if (file is null) return NotFound();
        if (file.UploadedById != UserId && !perm.CanManage) return Forbid();

        await files.DeleteAttachmentsAsync(db.Attachments.Where(a => a.Id == fileId));
        membership.Log(todo.ProjectId, UserId, $"removed the file \"{file.FileName}\" from \"{todo.Title}\"", todo.Id);
        await db.SaveChangesAsync();
        Flash($"\"{file.FileName}\" was removed.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------- Helpers ----------

    IActionResult ReadOnlyRedirect(int todoId)
    {
        Flash("This project is archived and read-only. Restore it to make changes.", "warning");
        return RedirectToAction(nameof(Details), new { id = todoId });
    }

    IActionResult ArchivedRedirect(int projectId)
    {
        Flash("This project is archived, so new to-dos can't be added.", "warning");
        return RedirectToAction("Details", "Projects", new { id = projectId });
    }

    void Validate(TodoFormVm vm, ProjectPermission perm)
    {
        if (!string.IsNullOrEmpty(vm.AssigneeId) && ProjectAccess.Assignables(perm.Project).All(u => u.Id != vm.AssigneeId))
            ModelState.AddModelError(nameof(vm.AssigneeId), "Pick someone on this project's team.");
        if (vm.StartDate is not null && vm.DueDate is not null && vm.DueDate < vm.StartDate)
            ModelState.AddModelError(nameof(vm.DueDate), "The due date can't be before the start date.");
    }

    static void Apply(TodoFormVm vm, TodoItem t)
    {
        t.Title = vm.Title.Trim();
        t.Description = vm.Description;
        t.Type = vm.Type;
        t.Priority = vm.Priority;
        t.StartDate = vm.StartDate?.Date;
        t.DueDate = vm.DueDate?.Date;
        t.AssigneeId = string.IsNullOrEmpty(vm.AssigneeId) ? null : vm.AssigneeId;
    }

    static TodoFormVm Fill(TodoFormVm vm, ProjectPermission perm)
    {
        vm.Project = perm.Project;
        vm.Assignables = ProjectAccess.Assignables(perm.Project);
        return vm;
    }

    static string? AssigneeName(ProjectPermission perm, string? userId) =>
        userId is null ? null : ProjectAccess.Assignables(perm.Project).FirstOrDefault(u => u.Id == userId)?.FullName;
}
