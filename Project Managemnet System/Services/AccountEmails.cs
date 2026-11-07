using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Services;

/// <summary>Password-reset and welcome emails. Links point at App:BaseUrl.</summary>
public class AccountEmails(UserManager<ApplicationUser> users, EmailQueue queue, IOptions<AppOptions> app)
{
    string Base => app.Value.BaseUrl.TrimEnd('/');

    async Task<string> ResetLinkAsync(ApplicationUser user, bool welcome)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        return $"{Base}/Account/ResetPassword?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}"
               + (welcome ? "&welcome=true" : "");
    }

    public async Task SendPasswordResetAsync(ApplicationUser user)
    {
        var link = await ResetLinkAsync(user, welcome: false);
        queue.Enqueue(EmailTemplates.Build(user.Email!, user.FullName, "Reset your ProjectDesk password", "Reset your password",
            [
                $"Hi {user.FullName}, we got a request to reset the password for your ProjectDesk account.",
                "Use the button below to choose a new password. The link works for 3 days.",
            ],
            "Choose a new password", link,
            footer: "If you didn't ask for this, you can ignore this email; your password won't change."));
    }

    /// <summary>
    /// Sent when an admin creates an account. Without a starting password the email has a
    /// "set your password" link; with one, it points to the sign-in page.
    /// </summary>
    public async Task SendWelcomeAsync(ApplicationUser user, string createdBy, bool passwordWasSet)
    {
        if (passwordWasSet)
        {
            queue.Enqueue(EmailTemplates.Build(user.Email!, user.FullName, "Your ProjectDesk account is ready", "Welcome to ProjectDesk",
                [
                    $"Hi {user.FullName}, {createdBy} created a ProjectDesk account for you.",
                    $"Sign in with {user.Email} and the starting password {createdBy} gives you, then change it from your profile.",
                ],
                "Sign in", $"{Base}/Account/Login"));
        }
        else
        {
            var link = await ResetLinkAsync(user, welcome: true);
            queue.Enqueue(EmailTemplates.Build(user.Email!, user.FullName, "Set up your ProjectDesk account", "Welcome to ProjectDesk",
                [
                    $"Hi {user.FullName}, {createdBy} created a ProjectDesk account for you.",
                    "Choose a password to start using it. The link works for 3 days.",
                ],
                "Set my password", link));
        }
    }
}
