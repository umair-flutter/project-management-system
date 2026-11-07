using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Services;

/// <summary>Loads one page of the activity feed used by the Progress pages.</summary>
public static class ActivityFeed
{
    public const int PageSize = 40;

    public static async Task<ActivityFeedVm> LoadAsync(ApplicationDbContext db, IQueryable<ActivityLog> query, int page, int pageSize = PageSize)
    {
        page = Math.Max(1, page);
        var items = await query
            .Include(a => a.User)
            .Include(a => a.Project)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .ToListAsync();

        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);

        var todoIds = items.Where(i => i.TodoItemId != null).Select(i => i.TodoItemId!.Value).Distinct().ToList();
        var live = await db.Todos.Where(t => todoIds.Contains(t.Id)).Select(t => t.Id).ToListAsync();

        return new ActivityFeedVm { Items = items, Page = page, HasMore = hasMore, LiveTodoIds = [.. live] };
    }
}
