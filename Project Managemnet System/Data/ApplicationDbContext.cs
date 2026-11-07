using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectInvitation> ProjectInvitations => Set<ProjectInvitation>();
    public DbSet<TodoItem> Todos => Set<TodoItem>();
    public DbSet<TodoComment> TodoComments => Set<TodoComment>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>SQLite (hosted demo) can't sort or compare decimals, so store them as REAL there.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        if (Database.IsSqlite()) configurationBuilder.Properties<decimal>().HaveConversion<double>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Users are deactivated, never deleted, so user FKs never cascade.
        b.Entity<Project>(e =>
        {
            e.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.IsArchived);
        });

        b.Entity<ProjectMember>(e =>
        {
            e.HasKey(m => new { m.ProjectId, m.UserId });
            e.HasOne(m => m.Project).WithMany(p => p.Members).HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ProjectInvitation>(e =>
        {
            e.HasOne(i => i.Project).WithMany(p => p.Invitations).HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.InvitedBy).WithMany().HasForeignKey(i => i.InvitedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.Email);
            e.HasIndex(i => i.TokenHash);
        });

        b.Entity<TodoItem>(e =>
        {
            e.HasOne(t => t.Project).WithMany(p => p.Todos).HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.CreatedBy).WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => new { t.ProjectId, t.Status });
            e.HasIndex(t => t.DueDate);
        });

        b.Entity<TodoComment>(e =>
        {
            e.HasOne(c => c.TodoItem).WithMany(t => t.Comments).HasForeignKey(c => c.TodoItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        // SQL Server rejects multiple cascade paths, so attachments are removed in code
        // (FileStorage.DeleteAttachmentsAsync) before their parents are deleted.
        b.Entity<Attachment>(e =>
        {
            e.HasOne(a => a.Project).WithMany().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.TodoItem).WithMany(t => t.Attachments).HasForeignKey(a => a.TodoItemId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.TodoComment).WithMany(c => c.Attachments).HasForeignKey(a => a.TodoCommentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ActivityLog>(e =>
        {
            e.HasOne(a => a.Project).WithMany().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.ProjectId, a.CreatedAt });
        });

        b.Entity<Notification>(e =>
        {
            e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.Actor).WithMany().HasForeignKey(n => n.ActorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.Project).WithMany().HasForeignKey(n => n.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
        });
    }
}
