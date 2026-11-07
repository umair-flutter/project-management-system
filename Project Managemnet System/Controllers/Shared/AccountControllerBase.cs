using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Infrastructure;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

namespace Project_Managemnet_System.Controllers.Shared;

/// <summary>Sign in / sign out shared by both portals. The Admin portal only admits admin roles.</summary>
public abstract class AccountControllerBase(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, FileStorage files) : PortalController
{
    /// <summary>Roles allowed to sign in here; empty means any active user.</summary>
    protected abstract string[] AllowedRoles { get; }
    protected abstract string PortalTitle { get; }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        ViewBag.PortalTitle = PortalTitle;
        return View("Login", new LoginVm { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        ViewBag.PortalTitle = PortalTitle;
        if (!ModelState.IsValid) return View("Login", vm);

        var user = await users.FindByEmailAsync(vm.Email);
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError("", "The email or password is incorrect.");
            return View("Login", vm);
        }

        if (AllowedRoles.Length > 0)
        {
            var roles = await users.GetRolesAsync(user);
            if (!roles.Any(r => AllowedRoles.Contains(r)))
            {
                ModelState.AddModelError("", "This account can't use the Admin portal. Sign in from the member portal instead.");
                return View("Login", vm);
            }
        }

        var result = await signIn.PasswordSignInAsync(user, vm.Password, vm.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "Too many failed attempts. Try again in a few minutes.");
            return View("Login", vm);
        }
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", "The email or password is incorrect.");
            return View("Login", vm);
        }

        return Url.IsLocalUrl(vm.ReturnUrl) ? LocalRedirect(vm.ReturnUrl) : RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [Authorize, HttpGet]
    public IActionResult ChangePassword() => View("ChangePassword", new ChangePasswordVm());

    [Authorize, HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid) return View("ChangePassword", vm);
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        var result = await users.ChangePasswordAsync(user, vm.CurrentPassword, vm.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View("ChangePassword", vm);
        }
        await signIn.RefreshSignInAsync(user);
        Flash("Your password was changed.");
        return RedirectToAction("Index", "Dashboard");
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        return View("Profile", await ToProfileVm(user));
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Profile(ProfileVm vm)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!ModelState.IsValid) return View("Profile", await ToProfileVm(user, vm));

        try
        {
            if (vm.Avatar is { Length: > 0 })
            {
                var path = await files.SaveAvatarAsync(vm.Avatar);
                files.DeleteAvatar(user.AvatarPath);
                user.AvatarPath = path;
            }
            else if (vm.RemoveAvatar)
            {
                files.DeleteAvatar(user.AvatarPath);
                user.AvatarPath = null;
            }
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(vm.Avatar), ex.Message);
            return View("Profile", await ToProfileVm(user, vm));
        }

        user.FullName = vm.FullName.Trim();
        user.EmailNotifications = vm.EmailNotifications;
        await users.UpdateAsync(user);
        Flash("Your profile was updated.");
        return RedirectToAction(nameof(Profile));
    }

    async Task<ProfileVm> ToProfileVm(ApplicationUser user, ProfileVm? posted = null) => new()
    {
        FullName = posted?.FullName ?? user.FullName,
        Email = user.Email ?? "",
        Role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "",
        AvatarPath = user.AvatarPath,
        EmailNotifications = posted?.EmailNotifications ?? user.EmailNotifications,
    };

    // ---------- Forgot / reset password ----------

    [AllowAnonymous, HttpGet]
    public IActionResult ForgotPassword() => View("ForgotPassword", new ForgotPasswordVm());

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordVm vm, [FromServices] AccountEmails emails)
    {
        if (!ModelState.IsValid) return View("ForgotPassword", vm);

        var user = await users.FindByEmailAsync(vm.Email);
        if (user is { IsActive: true }) await emails.SendPasswordResetAsync(user);

        // Same answer whether or not the account exists, so this page can't be used to find accounts.
        ViewBag.Sent = vm.Email;
        return View("ForgotPassword", vm);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult ResetPassword(string? email, string? token, bool welcome = false)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            Flash("That link is incomplete. Ask for a new one below.", "warning");
            return RedirectToAction(nameof(ForgotPassword));
        }
        return View("ResetPassword", new ResetPasswordVm { Email = email, Token = token, Welcome = welcome });
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordVm vm)
    {
        if (!ModelState.IsValid) return View("ResetPassword", vm);

        const string badLink = "This link has expired or was already used. Ask for a new one from the \"Forgot password\" page.";
        var user = await users.FindByEmailAsync(vm.Email);
        if (user is not { IsActive: true })
        {
            ModelState.AddModelError("", badLink);
            return View("ResetPassword", vm);
        }

        var result = await users.ResetPasswordAsync(user, vm.Token, vm.Password);
        if (!result.Succeeded)
        {
            // An invalid or expired token is the usual cause; password-rule errors are shown as-is.
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
                ModelState.AddModelError("", badLink);
            else
                foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View("ResetPassword", vm);
        }

        await users.ResetAccessFailedCountAsync(user!);
        await users.SetLockoutEndDateAsync(user!, null);
        Flash(vm.Welcome ? "Your password is set. Sign in to get started." : "Your password was changed. Sign in with the new one.");
        return RedirectToAction(nameof(Login));
    }

    // ---------- Accept a project invitation (self sign-up) ----------

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> AcceptInvite(string? token, [FromServices] ApplicationDbContext db)
    {
        var vm = await LoadInviteAsync(token, db);
        if (vm is null) return View("InviteInvalid");
        if (await users.FindByEmailAsync(vm.Email) is not null)
        {
            Flash("You already have an account. Sign in to see the project.", "info");
            return RedirectToAction(nameof(Login));
        }
        return View("AcceptInvite", vm);
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> AcceptInvite(AcceptInviteVm posted, [FromServices] ApplicationDbContext db, [FromServices] ProjectMembership membership)
    {
        var vm = await LoadInviteAsync(posted.Token, db);
        if (vm is null) return View("InviteInvalid");
        vm.FullName = posted.FullName;
        if (!ModelState.IsValid) return View("AcceptInvite", vm);

        if (await users.FindByEmailAsync(vm.Email) is not null)
        {
            Flash("You already have an account. Sign in to see the project.", "info");
            return RedirectToAction(nameof(Login));
        }

        var user = new ApplicationUser
        {
            UserName = vm.Email, Email = vm.Email, EmailConfirmed = true, // they proved it by opening the emailed link
            FullName = posted.FullName.Trim(),
        };
        var result = await users.CreateAsync(user, posted.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View("AcceptInvite", vm);
        }
        await users.AddToRoleAsync(user, Roles.Developer);
        await membership.ApplyInvitationsAsync(user);
        await signIn.SignInAsync(user, isPersistent: false);

        Flash($"Welcome, {user.FullName}! You've joined {string.Join(", ", vm.ProjectNames)}.");
        return RedirectToAction("Index", "Dashboard", new { area = "" });
    }

    async Task<AcceptInviteVm?> LoadInviteAsync(string? token, ApplicationDbContext db)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = MailOutboxWorker.Hash(token);
        var invite = await db.ProjectInvitations.Include(i => i.InvitedBy).FirstOrDefaultAsync(i => i.TokenHash == hash);
        if (invite is null || invite.IsExpired) return null;

        // Every pending invitation for this email is accepted together.
        var names = await db.ProjectInvitations.Where(i => i.Email == invite.Email).Select(i => i.Project!.Name).ToListAsync();
        return new AcceptInviteVm { Token = token, Email = invite.Email, ProjectNames = names, InvitedBy = invite.InvitedBy?.FullName };
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View("AccessDenied");
}
