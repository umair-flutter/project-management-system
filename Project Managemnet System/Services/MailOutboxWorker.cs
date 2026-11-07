using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Services;

/// <summary>
/// Runs every few seconds and turns saved data into email:
/// 1. Invitation emails with a sign-up link, for invitations not emailed yet.
/// 2. Due-date reminders: an in-app notification when an assigned to-do is due today or
///    tomorrow, and another when it becomes overdue (each once per due date).
/// 3. Notification digests: unread notifications older than Email:DigestDelaySeconds are
///    emailed to people who have email notifications turned on, one email per person.
/// Working from saved rows means nothing is emailed for a change that failed to save.
/// </summary>
public class MailOutboxWorker(IServiceScopeFactory scopes, EmailQueue queue, IOptions<EmailOptions> email,
    IOptions<AppOptions> app, ILogger<MailOutboxWorker> log) : BackgroundService
{
    static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);
    static readonly TimeSpan ReminderEvery = TimeSpan.FromMinutes(5);
    DateTime lastReminderRun = DateTime.MinValue;

    string Base => app.Value.BaseUrl.TrimEnd('/');
    string ProfileUrl => $"{Base}/Account/Profile";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // let startup (migrations, seeding) finish
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await SendInvitationsAsync(db, stoppingToken);
                if (DateTime.UtcNow - lastReminderRun >= ReminderEvery)
                {
                    await CreateRemindersAsync(db, stoppingToken);
                    lastReminderRun = DateTime.UtcNow;
                }
                await SendDigestsAsync(db, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "The email job failed; it will try again shortly.");
            }
            await Task.Delay(Tick, stoppingToken);
        }
    }

    // ---------- 1. Invitations ----------

    async Task SendInvitationsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var pending = await db.ProjectInvitations.Where(i => i.EmailedAt == null)
            .Include(i => i.Project).Include(i => i.InvitedBy).OrderBy(i => i.Id)
            .Take(50).ToListAsync(ct);
        if (pending.Count == 0) return;

        foreach (var inv in pending)
        {
            var token = NewToken();
            inv.TokenHash = Hash(token);
            inv.EmailedAt = DateTime.UtcNow;

            var url = $"{Base}/Account/AcceptInvite?token={Uri.EscapeDataString(token)}";
            var inviter = inv.InvitedBy?.FullName ?? "A project owner";
            queue.Enqueue(EmailTemplates.Build(inv.Email, inv.Email,
                $"{inviter} invited you to \"{inv.Project?.Name}\"",
                $"You're invited to {inv.Project?.Name}",
                [
                    $"{inviter} invited you to join the project \"{inv.Project?.Name}\" on ProjectDesk" + (inv.IsProjectAdmin ? " as a project admin." : "."),
                    "Create your account to see the project's tasks, bugs, and files. The link works for 14 days.",
                ],
                "Create my account", url,
                footer: "If you weren't expecting this, you can ignore this email."));
        }
        await db.SaveChangesAsync(ct);
    }

    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    // ---------- 2. Due-date reminders ----------

    async Task CreateRemindersAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var open = db.Todos.Where(t => t.Status == TodoStatus.Incomplete && t.AssigneeId != null && t.DueDate != null && !t.Project!.IsArchived);

        var dueSoon = await open.Where(t => t.DueDate >= today && t.DueDate <= tomorrow && t.DueReminderFor != t.DueDate).OrderBy(t => t.Id).Take(500).ToListAsync(ct);
        foreach (var t in dueSoon)
        {
            t.DueReminderFor = t.DueDate;
            db.Notifications.Add(new Notification
            {
                UserId = t.AssigneeId!, ProjectId = t.ProjectId, TodoItemId = t.Id,
                Message = $"\"{t.Title}\" is due {(t.DueDate == today ? "today" : "tomorrow")}",
            });
        }

        var overdue = await open.Where(t => t.DueDate < today && t.OverdueReminderFor != t.DueDate).OrderBy(t => t.Id).Take(500).ToListAsync(ct);
        foreach (var t in overdue)
        {
            t.OverdueReminderFor = t.DueDate;
            t.DueReminderFor ??= t.DueDate;
            db.Notifications.Add(new Notification
            {
                UserId = t.AssigneeId!, ProjectId = t.ProjectId, TodoItemId = t.Id,
                Message = $"\"{t.Title}\" is overdue (it was due {t.DueDate:dd MMM})",
            });
        }

        if (dueSoon.Count + overdue.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Created {Soon} due-soon and {Overdue} overdue reminders.", dueSoon.Count, overdue.Count);
        }
    }

    // ---------- 3. Notification digests ----------

    async Task SendDigestsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-Math.Max(0, email.Value.DigestDelaySeconds));
        var due = await db.Notifications.Where(n => n.EmailedAt == null && n.CreatedAt <= cutoff)
            .Include(n => n.User).Include(n => n.Project)
            .OrderBy(n => n.CreatedAt).Take(500).ToListAsync(ct);
        if (due.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var group in due.GroupBy(n => n.UserId))
        {
            foreach (var n in group) n.EmailedAt = now;

            var user = group.First().User;
            var unread = group.Where(n => !n.IsRead).ToList();
            if (user is null || !user.IsActive || !user.EmailNotifications || unread.Count == 0 || string.IsNullOrEmpty(user.Email))
                continue;

            var subject = unread.Count == 1 ? unread[0].Message : $"{unread.Count} new updates in ProjectDesk";
            var lines = unread.Take(10)
                .Select(n => new EmailTemplates.Line($"{n.Message} ({n.Project?.Name})", $"{Base}/Notifications/Open/{n.Id}"))
                .ToList();
            var paragraphs = new List<string> { $"Hi {user.FullName}, here's what you missed:" };
            if (unread.Count > 10) paragraphs.Add($"…and {unread.Count - 10} more.");

            queue.Enqueue(EmailTemplates.Build(user.Email, user.FullName, subject,
                unread.Count == 1 ? "You have a new update" : $"You have {unread.Count} new updates",
                paragraphs, "Open ProjectDesk", $"{Base}/Notifications", lines,
                footer: $"Don't want these emails? Turn them off on your profile: {ProfileUrl}"));
        }
        await db.SaveChangesAsync(ct);
    }
}
