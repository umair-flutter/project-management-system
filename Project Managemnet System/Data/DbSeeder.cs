using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Infrastructure;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Data;

/// <summary>
/// Applies migrations, creates the roles and the Super Admin account, and (when
/// Seed:DemoData is true) adds demo users and a sample project. All values come from
/// the "Seed" section of appsettings.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        // SQL Server uses migrations; the SQLite demo database is created straight from the model.
        if (db.Database.IsSqlite()) await db.Database.EnsureCreatedAsync(); else await db.Database.MigrateAsync();

        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        var seed = config.GetSection("Seed");
        var superEmail = seed["SuperAdmin:Email"];
        var superPassword = seed["SuperAdmin:Password"];
        if (string.IsNullOrWhiteSpace(superEmail) || string.IsNullOrWhiteSpace(superPassword))
        {
            log.LogWarning("Seed:SuperAdmin is not configured, so no Super Admin account was created.");
            return;
        }

        var superAdmin = await EnsureUserAsync(users, superEmail, superPassword, seed["SuperAdmin:FullName"] ?? "Super Admin", Roles.SuperAdmin, log);

        if (!seed.GetValue<bool>("DemoData") || await db.Projects.AnyAsync()) return;

        var demoPassword = seed["DemoPassword"];
        if (string.IsNullOrWhiteSpace(demoPassword)) return;

        var owner = await EnsureUserAsync(users, "owner@demo.local", demoPassword, "Olivia Owner", Roles.Admin, log);
        var dev1 = await EnsureUserAsync(users, "dev1@demo.local", demoPassword, "Daniel Developer", Roles.Developer, log);
        var dev2 = await EnsureUserAsync(users, "dev2@demo.local", demoPassword, "Priya Programmer", Roles.Developer, log);
        var designer = await EnsureUserAsync(users, "designer@demo.local", demoPassword, "Sam Designer", Roles.Developer, log);

        var today = DateTime.Today;
        var web = new Project
        {
            Name = "Company Website Redesign",
            Description = "New marketing site with a blog, careers page, and contact forms.",
            OwnerId = owner.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-20),
            Members =
            [
                new ProjectMember { UserId = dev1.Id, IsProjectAdmin = true },
                new ProjectMember { UserId = dev2.Id },
                new ProjectMember { UserId = designer.Id },
            ],
        };
        web.Todos.AddRange(
        [
            Todo("Design the new home page", TodoType.Task, TodoPriority.High, designer.Id, owner.Id, today.AddDays(-10), today.AddDays(2), TodoStatus.Incomplete),
            Todo("Build the blog listing page", TodoType.Task, TodoPriority.Medium, dev1.Id, owner.Id, today.AddDays(-7), today.AddDays(5), TodoStatus.Incomplete),
            Todo("Set up the contact form email", TodoType.Task, TodoPriority.Low, dev2.Id, owner.Id, today.AddDays(-12), today.AddDays(-3), TodoStatus.Completed),
            Todo("Navigation menu overlaps logo on mobile", TodoType.Bug, TodoPriority.High, dev2.Id, owner.Id, today.AddDays(-2), today.AddDays(1), TodoStatus.Incomplete),
            Todo("Careers page copy", TodoType.Task, TodoPriority.Low, null, owner.Id, null, today.AddDays(9), TodoStatus.Incomplete),
            Todo("Footer links return 404", TodoType.Bug, TodoPriority.Medium, dev1.Id, owner.Id, today.AddDays(-15), today.AddDays(-8), TodoStatus.Finished),
        ]);

        var app = new Project
        {
            Name = "Mobile Ordering App",
            Description = "Customer app for ordering ahead and paying in store.",
            OwnerId = owner.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-8),
            Members = [new ProjectMember { UserId = dev1.Id }, new ProjectMember { UserId = designer.Id }],
        };
        app.Todos.AddRange(
        [
            Todo("Checkout flow wireframes", TodoType.Task, TodoPriority.High, designer.Id, owner.Id, today.AddDays(-5), today.AddDays(3), TodoStatus.Completed),
            Todo("Order history API", TodoType.Task, TodoPriority.Medium, dev1.Id, owner.Id, today, today.AddDays(12), TodoStatus.Incomplete),
            Todo("App crashes when the cart is empty", TodoType.Bug, TodoPriority.High, null, owner.Id, null, today.AddDays(4), TodoStatus.Incomplete),
        ]);

        db.Projects.AddRange(web, app);
        foreach (var p in new[] { web, app })
            db.ActivityLogs.Add(new ActivityLog { Project = p, UserId = owner.Id, Message = "created the project", CreatedAt = p.CreatedAt });

        await db.SaveChangesAsync();
        log.LogInformation("Demo data added (Super Admin: {Email}).", superAdmin.Email);
    }

    static TodoItem Todo(string title, TodoType type, TodoPriority prio, string? assignee, string creator,
        DateTime? start, DateTime? due, TodoStatus status) => new()
    {
        Title = title,
        Type = type,
        Priority = prio,
        AssigneeId = assignee,
        CreatedById = creator,
        StartDate = start,
        DueDate = due,
        Status = status,
        CompletedAt = status != TodoStatus.Incomplete ? DateTime.UtcNow.AddDays(-1) : null,
        FinishedAt = status == TodoStatus.Finished ? DateTime.UtcNow : null,
    };

    static async Task<ApplicationUser> EnsureUserAsync(UserManager<ApplicationUser> users, string email, string password,
        string fullName, string role, ILogger log)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = fullName };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not create {email}: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            log.LogInformation("Created {Role} account {Email}.", role, email);
        }
        if (!await users.IsInRoleAsync(user, role))
            await users.AddToRoleAsync(user, role);
        return user;
    }
}
