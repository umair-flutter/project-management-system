using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Project_Managemnet_System.Controllers.Shared;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Infrastructure;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

// Admin portal (/Admin): Super Admins and Admins (project owners) only.
namespace Project_Managemnet_System.Areas.Admin.Controllers;

[Area("Admin")]
public class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, FileStorage files)
    : AccountControllerBase(signIn, users, files)
{
    protected override string[] AllowedRoles => [Roles.SuperAdmin, Roles.Admin];
    protected override string PortalTitle => "Admin portal";
}

[Area("Admin"), Authorize(Roles = Roles.AdminPortal)]
public class DashboardController(ApplicationDbContext db, ProjectAccess access) : DashboardControllerBase(db, access);

[Area("Admin"), Authorize(Roles = Roles.AdminPortal)]
public class ProjectsController(ApplicationDbContext db, ProjectAccess access, ProjectMembership membership, FileStorage files)
    : ProjectsControllerBase(db, access, membership, files);

[Area("Admin"), Authorize(Roles = Roles.AdminPortal)]
public class CalendarController(ProjectAccess access) : CalendarControllerBase(access);

[Area("Admin"), Authorize(Roles = Roles.AdminPortal)]
public class ProgressController(ApplicationDbContext db, ProjectAccess access) : ProgressControllerBase(db, access);

[Area("Admin"), Authorize(Roles = Roles.AdminPortal)]
public class TodosController(ApplicationDbContext db, ProjectAccess access, ProjectMembership membership, FileStorage files, Notifier notifier)
    : TodosControllerBase(db, access, membership, files, notifier);

[Area("Admin"), Authorize(Roles = Roles.AdminPortal)]
public class NotificationsController(ApplicationDbContext db) : NotificationsControllerBase(db);

[Area("Admin"), Authorize(Roles = Roles.AdminPortal)]
public class MyTodosController(ProjectAccess access) : MyTodosControllerBase(access);

[Area("Admin"), Authorize(Roles = Roles.AdminPortal)]
public class SearchController(ProjectAccess access) : SearchControllerBase(access);
