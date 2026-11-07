using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Project_Managemnet_System.Controllers.Shared;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

// Member portal (site root). Every signed-in, active user can use it; what they see is
// limited by ProjectAccess to the projects they belong to.
namespace Project_Managemnet_System.Controllers;

public class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, FileStorage files)
    : AccountControllerBase(signIn, users, files)
{
    protected override string[] AllowedRoles => [];
    protected override string PortalTitle => "Member portal";
}

[Authorize]
public class DashboardController(ApplicationDbContext db, ProjectAccess access) : DashboardControllerBase(db, access);

[Authorize]
public class ProjectsController(ApplicationDbContext db, ProjectAccess access, ProjectMembership membership, FileStorage files)
    : ProjectsControllerBase(db, access, membership, files);

[Authorize]
public class TodosController(ApplicationDbContext db, ProjectAccess access, ProjectMembership membership, FileStorage files, Notifier notifier)
    : TodosControllerBase(db, access, membership, files, notifier);

[Authorize]
public class CalendarController(ProjectAccess access) : CalendarControllerBase(access);

[Authorize]
public class ProgressController(ApplicationDbContext db, ProjectAccess access) : ProgressControllerBase(db, access);

[Authorize]
public class NotificationsController(ApplicationDbContext db) : NotificationsControllerBase(db);

[Authorize]
public class MyTodosController(ProjectAccess access) : MyTodosControllerBase(access);

[Authorize]
public class SearchController(ProjectAccess access) : SearchControllerBase(access);

/// <summary>Serves stored files to people who can see the owning project. Used by both portals.</summary>
[Authorize]
public class AttachmentsController(ApplicationDbContext db, ProjectAccess access, FileStorage files) : Controller
{
    public async Task<IActionResult> Download(int id)
    {
        var a = await db.Attachments.FindAsync(id);
        if (a is null || await access.GetAsync(a.ProjectId, User) is null) return NotFound();

        var path = files.AttachmentPath(a);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, a.ContentType, a.FileName);
    }
}
