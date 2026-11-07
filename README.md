# ProjectDesk – Project Management System

A project management system built with **ASP.NET Core MVC (.NET 10)** and **SQL Server**, for project owners to plan work, assign tasks and bugs to remote team members, and track progress.

## Features

- **Three roles:** Super Admin (all projects), Admin / project owner (their own projects), Developer (projects they are added to). Owners can make members **project admins**.
- **Two portals:** Admin portal at `/Admin`, member portal at `/`.
- **Dashboard:** counts of projects, tasks, bugs, and members; to-do status and project progress charts; top due to-dos; recent activity.
- **Projects:** name, icon, description, team by email (unknown emails get an invitation link), archive/restore (archived projects are read-only).
- **To-dos:** tasks and bugs with High/Medium/Low priority, start and due dates, assignee, file attachments. Lists for All, Assigned, Unassigned, Completed, Incomplete, Tasks, Bugs, Finished, with priority filter and search.
- **Status flow:** assignee marks *Completed*; owner or project admin marks *Finished* or reopens.
- **Board view** with drag-and-drop between statuses.
- **Comments** with attachments (edit and delete your own).
- **Project files**, **calendar** (day/week/month/year/list), **progress feed**, **My to-dos**, **global search**, **CSV export**.
- **Notifications:** in-app bell plus email digests and due-date reminders.
- **Email:** forgot/reset password, invitation sign-up links, welcome emails.
- **Profile** with photo and email preferences.

## Getting started

Requirements: .NET 10 SDK and SQL Server LocalDB (installed with Visual Studio).

```bash
cd "Project Managemnet System"
dotnet run --launch-profile http
```

Open http://localhost:5133. On first run the database is created and, in Development, demo data is added. Demo accounts and their passwords are in `appsettings.Development.json` under `Seed` (development use only; change or remove them before deploying).

- Admin portal: http://localhost:5133/Admin
- Member portal: http://localhost:5133/

## Email

By default (`Email:Mode = Pickup`) emails are saved as `.eml` files in `App_Data/mail-outbox` instead of being sent. To send real email, set in `appsettings.json`:

```json
"App":   { "BaseUrl": "https://your-site.example" },
"Email": { "Mode": "Smtp", "FromAddress": "you@yourdomain.com",
           "Smtp": { "Host": "smtp.example.com", "Port": 587, "Security": "StartTls", "Username": "you@yourdomain.com" } }
```

Keep the SMTP password out of source control:

```bash
dotnet user-secrets set "Email:Smtp:Password" "your-password"
```

On a server, use the environment variable `Email__Smtp__Password` instead.

## Project layout

- `Controllers/Shared/` – logic shared by both portals (each portal has thin controllers that inherit from these).
- `Areas/Admin/` – Admin portal controllers and the Members pages.
- `Views/Portal/` – views shared by both portals.
- `Services/` – access rules (`ProjectAccess`), files, notifications, email, background email job.
- `Data/` – EF Core context, migrations, seeding.
