using System.ComponentModel.DataAnnotations;
using Project_Managemnet_System.Services;

namespace Project_Managemnet_System.Models;

public class LoginVm
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class ChangePasswordVm
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(8), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match."), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public class DashboardVm
{
    public int ProjectCount { get; set; }
    public int TaskCount { get; set; }
    public int BugCount { get; set; }
    public int MemberCount { get; set; }

    public int Unassigned { get; set; }
    public int Incomplete { get; set; }
    public int Completed { get; set; }
    public int Finished { get; set; }
    public int Overdue { get; set; }

    public List<ProjectProgressRow> ProjectProgress { get; set; } = [];
    public List<TodoItem> TopDue { get; set; } = [];
    public bool TopDueIsMine { get; set; }
    public ActivityFeedVm RecentActivity { get; set; } = new();
}

public record ProjectProgressRow(int Id, string Name, int Total, int Done);

public class ProjectCardVm
{
    public required Project Project { get; init; }
    public int TaskTotal { get; init; }
    public int TaskDone { get; init; }
    public int BugTotal { get; init; }
    public int BugDone { get; init; }
    public List<ApplicationUser> People { get; init; } = [];
    public int Percent => TaskTotal + BugTotal == 0 ? 0 : (int)Math.Round(100.0 * (TaskDone + BugDone) / (TaskTotal + BugTotal));
}

public class ProjectListVm
{
    public List<ProjectCardVm> Projects { get; set; } = [];
    public string? Query { get; set; }
    public bool CanCreate { get; set; }
    public bool Archived { get; set; }
}

public class ProjectFilesVm
{
    public required ProjectPermission Access { get; init; }
    /// <summary>Files uploaded to the project itself (documentation, flowcharts…).</summary>
    public List<Attachment> Documents { get; set; } = [];
    /// <summary>Files attached to to-dos and their comments.</summary>
    public List<Attachment> TodoFiles { get; set; } = [];
}

public class ActivityFeedVm
{
    public List<ActivityLog> Items { get; set; } = [];
    /// <summary>To-do ids in the feed that still exist (deleted ones are not linked).</summary>
    public HashSet<int> LiveTodoIds { get; set; } = [];
    public int Page { get; set; } = 1;
    public bool HasMore { get; set; }

    // Per-project page
    public ProjectPermission? Access { get; set; }

    // Global page
    public int? ProjectId { get; set; }
    public List<Project> Projects { get; set; } = [];
}

public class NotificationListVm
{
    public List<Notification> Items { get; set; } = [];
    public int Page { get; set; } = 1;
    public bool HasMore { get; set; }
}

public class MyTodosVm
{
    public List<TodoItem> Todos { get; set; } = [];
    public string Filter { get; set; } = "open";
    public TodoPriority? Priority { get; set; }
    public Dictionary<string, int> Counts { get; set; } = [];
}

public class SearchVm
{
    public string? Query { get; set; }
    public List<Project> Projects { get; set; } = [];
    public List<TodoItem> Todos { get; set; } = [];
}

public class ForgotPasswordVm
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";
}

public class ResetPasswordVm
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Token { get; set; } = "";

    /// <summary>True for the "set your password" link in a welcome email.</summary>
    public bool Welcome { get; set; }

    [Required, DataType(DataType.Password), MinLength(8), Display(Name = "New password")]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords do not match."), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public class AcceptInviteVm
{
    [Required]
    public string Token { get; set; } = "";

    // Display only
    public string Email { get; set; } = "";
    public List<string> ProjectNames { get; set; } = [];
    public string? InvitedBy { get; set; }

    [Required, StringLength(100), Display(Name = "Your full name")]
    public string FullName { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(8), Display(Name = "Password")]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords do not match."), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public class ProfileVm
{
    [Display(Name = "Email me about updates I haven't seen in the app")]
    public bool EmailNotifications { get; set; } = true;

    [Required, StringLength(100), Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string? AvatarPath { get; set; }

    [Display(Name = "Profile photo")]
    public IFormFile? Avatar { get; set; }
    public bool RemoveAvatar { get; set; }
}

public class BoardVm
{
    public required ProjectPermission Access { get; init; }
    public List<TodoItem> Todos { get; set; } = [];
    public TodoType? Type { get; set; }
    public bool MineOnly { get; set; }
}

public class CalendarVm
{
    public List<Project> Projects { get; set; } = [];
    public int? ProjectId { get; set; }
    public bool MineOnly { get; set; }
}

public class ProjectFormVm
{
    public int? Id { get; set; }

    [Required, StringLength(150), Display(Name = "Project name")]
    public string Name { get; set; } = "";

    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Project icon")]
    public IFormFile? Icon { get; set; }
    public string? ExistingIconPath { get; set; }
    public bool RemoveIcon { get; set; }

    [Display(Name = "Team member emails")]
    public string? MemberEmails { get; set; }
}

public static class TodoFilters
{
    public const string All = "all", Assigned = "assigned", Unassigned = "unassigned", Completed = "completed",
        Incomplete = "incomplete", Tasks = "tasks", Bugs = "bugs", Finished = "finished";

    public static readonly (string Key, string Label)[] Tabs =
    [
        (All, "All to-dos"), (Assigned, "Assigned"), (Unassigned, "Unassigned"), (Completed, "Completed"),
        (Incomplete, "Incomplete"), (Tasks, "Tasks"), (Bugs, "Bugs"), (Finished, "Finished"),
    ];
}

public class ProjectDetailsVm
{
    public required ProjectPermission Access { get; init; }
    public List<TodoItem> Todos { get; set; } = [];
    public string Filter { get; set; } = TodoFilters.All;
    public TodoPriority? Priority { get; set; }
    public string? Query { get; set; }
    public bool MineOnly { get; set; }
    public Dictionary<string, int> Counts { get; set; } = [];
    public Project Project => Access.Project;
}

public class ProjectMembersVm
{
    public required ProjectPermission Access { get; init; }
    public List<ProjectInvitation> Invitations { get; set; } = [];
    public Dictionary<string, int> OpenTodosByUser { get; set; } = [];

    [Display(Name = "Email addresses")]
    public string? Emails { get; set; }
    public bool AsProjectAdmin { get; set; }
}

public class TodoFormVm
{
    public int? Id { get; set; }
    public int ProjectId { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public TodoType Type { get; set; } = TodoType.Task;
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;

    [DataType(DataType.Date), Display(Name = "Start date")]
    public DateTime? StartDate { get; set; }

    [DataType(DataType.Date), Display(Name = "Due date")]
    public DateTime? DueDate { get; set; }

    [Display(Name = "Assign to")]
    public string? AssigneeId { get; set; }

    [Display(Name = "Attach files")]
    public List<IFormFile>? Files { get; set; }

    // Display only
    public Project? Project { get; set; }
    public List<ApplicationUser> Assignables { get; set; } = [];
}

public class TodoDetailsVm
{
    public required TodoItem Todo { get; init; }
    public required ProjectPermission Access { get; init; }
    public List<ApplicationUser> Assignables { get; set; } = [];
}

public class MemberFormVm
{
    public string? Id { get; set; }

    [Required, StringLength(100), Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [DataType(DataType.Password), Display(Name = "Password")]
    public string? Password { get; set; }

    [Required]
    public string Role { get; set; } = Infrastructure.Roles.Developer;

    [Display(Name = "Account active")]
    public bool IsActive { get; set; } = true;

    public List<string> AllowedRoles { get; set; } = [];
}

public class MemberRowVm
{
    public required ApplicationUser User { get; init; }
    public string Role { get; init; } = "";
    public int ProjectCount { get; init; }
    public int OpenTodos { get; init; }
}
