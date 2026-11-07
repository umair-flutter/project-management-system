using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Razor;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Infrastructure;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Developer = "Developer";

    /// <summary>Roles allowed into the /Admin portal.</summary>
    public const string AdminPortal = SuperAdmin + "," + Admin;

    public static readonly string[] All = [SuperAdmin, Admin, Developer];
}

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not signed in.");

    public static bool IsSuperAdmin(this ClaimsPrincipal user) => user.IsInRole(Roles.SuperAdmin);

    /// <summary>Super admins and admins (project owners) can create projects.</summary>
    public static bool CanCreateProjects(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.SuperAdmin) || user.IsInRole(Roles.Admin);
}

/// <summary>
/// Lets both portals (root and the Admin area) share one copy of the views in /Views/Portal.
/// Each portal still searches its own folders first, so either can override a view.
/// </summary>
public class PortalViewLocationExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context) { }

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations) =>
        viewLocations.Concat(["/Views/Portal/{1}/{0}.cshtml", "/Views/Portal/Shared/{0}.cshtml"]);
}

/// <summary>Small display helpers used by the views.</summary>
public static class Ui
{
    public static string PriorityBadge(TodoPriority p) => p switch
    {
        TodoPriority.High => "badge-soft-danger",
        TodoPriority.Medium => "badge-soft-warning",
        _ => "badge-soft-success",
    };

    public static string StatusBadge(TodoStatus s) => s switch
    {
        TodoStatus.Finished => "badge-soft-primary",
        TodoStatus.Completed => "badge-soft-success",
        _ => "badge-soft-secondary",
    };

    public static string StatusLabel(TodoStatus s) => s switch
    {
        TodoStatus.Finished => "Finished",
        TodoStatus.Completed => "Completed",
        _ => "Incomplete",
    };

    public static string TypeBadge(TodoType t) => t == TodoType.Bug ? "badge-soft-danger" : "badge-soft-info";
    public static string TypeIcon(TodoType t) => t == TodoType.Bug ? "bi-bug" : "bi-check2-square";

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
            : $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }

    /// <summary>Stable colour per user, so an avatar always looks the same.</summary>
    public static string AvatarColor(string? key)
    {
        string[] palette = ["#4f46e5", "#0891b2", "#059669", "#d97706", "#db2777", "#7c3aed", "#2563eb", "#dc2626"];
        var hash = 0;
        foreach (var c in key ?? "") hash = (hash * 31 + c) & 0x7fffffff;
        return palette[hash % palette.Length];
    }

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };

    public static bool IsOverdue(TodoItem t) =>
        t.DueDate is { } d && d.Date < DateTime.Today && t.Status == TodoStatus.Incomplete;
}
