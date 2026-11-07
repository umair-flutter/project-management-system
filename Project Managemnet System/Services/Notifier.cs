using Project_Managemnet_System.Data;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Services;

/// <summary>
/// Stages in-app notifications (the caller saves). The person who caused an event is
/// never notified about it, and each recipient gets one notification per event.
/// </summary>
public class Notifier(ApplicationDbContext db)
{
    public void Notify(IEnumerable<string?> recipients, string actorId, int projectId, string message, int? todoId = null)
    {
        foreach (var userId in recipients.Where(r => !string.IsNullOrEmpty(r) && r != actorId).Distinct())
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId!,
                ActorId = actorId,
                ProjectId = projectId,
                TodoItemId = todoId,
                Message = message,
            });
        }
    }

    public void Notify(string? recipient, string actorId, int projectId, string message, int? todoId = null) =>
        Notify([recipient], actorId, projectId, message, todoId);
}
