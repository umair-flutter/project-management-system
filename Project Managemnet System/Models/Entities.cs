using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Project_Managemnet_System.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string FullName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    /// <summary>Web path of the profile photo, e.g. /uploads/avatars/abc.png</summary>
    [MaxLength(300)]
    public string? AvatarPath { get; set; }

    /// <summary>Email me about notifications I haven't seen in the app.</summary>
    public bool EmailNotifications { get; set; } = true;
}

/// <summary>An in-app notification for one user.</summary>
public class Notification
{
    public long Id { get; set; }

    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }

    /// <summary>Who caused it (shown as the avatar).</summary>
    public string? ActorId { get; set; }
    public ApplicationUser? Actor { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>Not a foreign key: the to-do may be deleted later; the link then falls back to the project.</summary>
    public int? TodoItemId { get; set; }

    [MaxLength(500)]
    public string Message { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the email job handled it (sent, or skipped because it was read or emails are off).</summary>
    public DateTime? EmailedAt { get; set; }
}

public class Project
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Web path of the uploaded icon, e.g. /uploads/icons/abc.png</summary>
    [MaxLength(300)]
    public string? IconPath { get; set; }

    [Required]
    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public List<ProjectMember> Members { get; set; } = [];
    public List<ProjectInvitation> Invitations { get; set; } = [];
    public List<TodoItem> Todos { get; set; } = [];
}

public class ProjectMember
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }

    /// <summary>Project admins can manage to-dos and members of this project.</summary>
    public bool IsProjectAdmin { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>An email invited to a project before an account exists for it.</summary>
public class ProjectInvitation
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    [Required, MaxLength(256)]
    public string Email { get; set; } = "";
    public bool IsProjectAdmin { get; set; }

    public string InvitedById { get; set; } = "";
    public ApplicationUser? InvitedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>SHA-256 of the sign-up token in the invitation email (the token itself is never stored).</summary>
    [MaxLength(64)]
    public string? TokenHash { get; set; }
    public DateTime? EmailedAt { get; set; }

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);
    public bool IsExpired => EmailedAt is { } sent && sent + Lifetime < DateTime.UtcNow;
}

public enum TodoType { Task = 0, Bug = 1 }
public enum TodoPriority { Low = 0, Medium = 1, High = 2 }

/// <summary>
/// Incomplete: open work. Completed: the assignee says it is done.
/// Finished: a project manager has verified and closed it.
/// </summary>
public enum TodoStatus { Incomplete = 0, Completed = 1, Finished = 2 }

public class TodoItem
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public TodoType Type { get; set; }
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;
    public TodoStatus Status { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }

    public string? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }

    public string CreatedById { get; set; } = "";
    public ApplicationUser? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>The due date the "due soon" / "overdue" reminders were sent for. Changing the due date re-arms them.</summary>
    public DateTime? DueReminderFor { get; set; }
    public DateTime? OverdueReminderFor { get; set; }

    public List<TodoComment> Comments { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
}

public class TodoComment
{
    public int Id { get; set; }
    public int TodoItemId { get; set; }
    public TodoItem? TodoItem { get; set; }

    public string AuthorId { get; set; } = "";
    public ApplicationUser? Author { get; set; }

    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }

    public List<Attachment> Attachments { get; set; } = [];
}

/// <summary>
/// A stored file. Always belongs to a project. If TodoItemId and TodoCommentId are both
/// null it is a project-level document.
/// </summary>
public class Attachment
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? TodoItemId { get; set; }
    public TodoItem? TodoItem { get; set; }

    public int? TodoCommentId { get; set; }
    public TodoComment? TodoComment { get; set; }

    [MaxLength(255)]
    public string FileName { get; set; } = "";
    [MaxLength(100)]
    public string StoredName { get; set; } = "";
    [MaxLength(150)]
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }

    public string UploadedById { get; set; } = "";
    public ApplicationUser? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Feed of what happened in a project (used by the Progress page).</summary>
public class ActivityLog
{
    public long Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }

    /// <summary>Not a foreign key: the to-do may have been deleted since.</summary>
    public int? TodoItemId { get; set; }

    [MaxLength(500)]
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
