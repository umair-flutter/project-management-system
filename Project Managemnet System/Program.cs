using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Infrastructure;
using Project_Managemnet_System.Models;
using Project_Managemnet_System.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;
    // Pages under /Admin send people to the Admin portal's own sign-in page.
    o.Events.OnRedirectToLogin = ctx =>
    {
        var target = ctx.Request.Path.StartsWithSegments("/Admin")
            ? "/Admin/Account/Login?ReturnUrl=" + Uri.EscapeDataString(ctx.Request.PathBase + ctx.Request.Path + ctx.Request.QueryString)
            : ctx.RedirectUri;
        ctx.Response.Redirect(target);
        return Task.CompletedTask;
    };
});

// Re-check accounts every minute so deactivated users are signed out quickly.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 100 * 1024 * 1024);

builder.Services.AddScoped<ProjectAccess>();
builder.Services.AddScoped<ProjectMembership>();
builder.Services.AddScoped<FileStorage>();
builder.Services.AddScoped<Notifier>();

// Email: pages queue messages; background workers send them and turn saved data into email.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddScoped<AccountEmails>();
builder.Services.AddHostedService<EmailQueueWorker>();
builder.Services.AddHostedService<MailOutboxWorker>();
// Password reset and "set your password" links stay valid for 3 days.
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(3));

builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.Configure<RazorViewEngineOptions>(o => o.ViewLocationExpanders.Add(new PortalViewLocationExpander()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Dashboard/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // runtime uploads (project icons) are not part of the static asset manifest
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}").WithStaticAssets();
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}").WithStaticAssets();

await DbSeeder.SeedAsync(app.Services);

app.Run();
