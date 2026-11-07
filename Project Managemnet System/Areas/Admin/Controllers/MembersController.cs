using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Controllers.Shared;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Infrastructure;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

namespace Project_Managemnet_System.Areas.Admin.Controllers;

/// <summary>
/// User accounts. Super Admins manage everyone; Admins (project owners) can add and edit
/// Developer accounts so they can staff their projects.
/// </summary>
[Area("Admin"), Authorize(Roles = Roles.AdminPortal)]
public class MembersController(ApplicationDbContext db, UserManager<ApplicationUser> users, ProjectMembership membership, AccountEmails emails) : PortalController
{
    List<string> AllowedRoles => User.IsSuperAdmin() ? [.. Roles.All] : [Roles.Developer];

    public async Task<IActionResult> Index(string? q, string? role)
    {
        var roleByUser = await (from ur in db.UserRoles
                                join r in db.Roles on ur.RoleId equals r.Id
                                select new { ur.UserId, r.Name }).ToDictionaryAsync(x => x.UserId, x => x.Name ?? "");

        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(u => u.FullName.Contains(q) || u.Email!.Contains(q));
        var list = await query.OrderBy(u => u.FullName).ToListAsync();

        var projectCounts = await db.ProjectMembers.Where(m => !m.Project!.IsArchived)
            .GroupBy(m => m.UserId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var ownedCounts = await db.Projects.Where(p => !p.IsArchived)
            .GroupBy(p => p.OwnerId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var openCounts = await db.Todos.Where(t => t.AssigneeId != null && t.Status == TodoStatus.Incomplete)
            .GroupBy(t => t.AssigneeId!).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);

        var rows = list.Select(u => new MemberRowVm
        {
            User = u,
            Role = roleByUser.GetValueOrDefault(u.Id, ""),
            ProjectCount = projectCounts.GetValueOrDefault(u.Id) + ownedCounts.GetValueOrDefault(u.Id),
            OpenTodos = openCounts.GetValueOrDefault(u.Id),
        }).Where(r => string.IsNullOrEmpty(role) || r.Role == role).ToList();

        ViewBag.Query = q;
        ViewBag.Role = role;
        ViewBag.CanManageAll = User.IsSuperAdmin();
        return View(rows);
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new MemberFormVm { AllowedRoles = AllowedRoles });

    [HttpPost]
    public async Task<IActionResult> Create(MemberFormVm vm)
    {
        vm.AllowedRoles = AllowedRoles;
        if (!vm.AllowedRoles.Contains(vm.Role)) ModelState.AddModelError(nameof(vm.Role), "You can't give that role.");
        if (!ModelState.IsValid) return View("Form", vm);

        var user = new ApplicationUser
        {
            UserName = vm.Email.Trim(), Email = vm.Email.Trim(), EmailConfirmed = true,
            FullName = vm.FullName.Trim(), IsActive = vm.IsActive,
        };
        // Without a starting password the account gets a "set your password" email instead.
        var hasPassword = !string.IsNullOrWhiteSpace(vm.Password);
        var result = hasPassword ? await users.CreateAsync(user, vm.Password!) : await users.CreateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View("Form", vm);
        }
        await users.AddToRoleAsync(user, vm.Role);
        await membership.ApplyInvitationsAsync(user);

        if (user.IsActive)
        {
            var me = await users.GetUserAsync(User);
            await emails.SendWelcomeAsync(user, me?.FullName ?? "An admin", hasPassword);
        }
        Flash(!user.IsActive ? $"{user.FullName}'s account was created (inactive, so no email was sent)."
            : hasPassword ? $"{user.FullName}'s account was created and a welcome email is on its way. Share the starting password with them yourself; it isn't emailed."
            : $"{user.FullName}'s account was created. We emailed them a link to set their password.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SendPasswordReset(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? Roles.Developer;
        if (!CanEdit(role)) return Forbid();
        if (!user.IsActive)
        {
            Flash("This account is inactive. Activate it before sending a reset link.", "warning");
            return RedirectToAction(nameof(Edit), new { id });
        }

        await emails.SendPasswordResetAsync(user);
        Flash($"A password reset link was emailed to {user.Email}.");
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? Roles.Developer;
        if (!CanEdit(role)) return Forbid();

        return View("Form", new MemberFormVm
        {
            Id = user.Id, FullName = user.FullName, Email = user.Email ?? "", Role = role,
            IsActive = user.IsActive, AllowedRoles = AllowedRoles,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string id, MemberFormVm vm)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var currentRole = (await users.GetRolesAsync(user)).FirstOrDefault() ?? Roles.Developer;
        if (!CanEdit(currentRole)) return Forbid();

        vm.Id = id;
        vm.AllowedRoles = AllowedRoles;
        if (!vm.AllowedRoles.Contains(vm.Role)) ModelState.AddModelError(nameof(vm.Role), "You can't give that role.");
        var isSelf = user.Id == UserId;
        if (isSelf && (!vm.IsActive || vm.Role != currentRole))
            ModelState.AddModelError("", "You can't deactivate your own account or change your own role.");
        if (!ModelState.IsValid) return View("Form", vm);

        user.FullName = vm.FullName.Trim();
        user.IsActive = vm.IsActive;
        if (!string.Equals(user.Email, vm.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            user.Email = vm.Email.Trim();
            user.UserName = vm.Email.Trim();
        }
        var result = await users.UpdateAsync(user);
        if (result.Succeeded && !string.IsNullOrWhiteSpace(vm.Password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            result = await users.ResetPasswordAsync(user, token, vm.Password);
        }
        if (result.Succeeded && vm.Role != currentRole)
        {
            await users.RemoveFromRoleAsync(user, currentRole);
            await users.AddToRoleAsync(user, vm.Role);
        }
        if (result.Succeeded && !user.IsActive)
            await users.UpdateSecurityStampAsync(user); // signs them out on their next request

        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View("Form", vm);
        }

        Flash($"{user.FullName}'s account was updated.");
        return RedirectToAction(nameof(Index));
    }

    bool CanEdit(string targetRole) => User.IsSuperAdmin() || targetRole == Roles.Developer;
}
