using Microsoft.EntityFrameworkCore;
using Project_Managemnet_System.Data;
using Project_Managemnet_System.Models;

namespace Project_Managemnet_System.Services;

/// <summary>
/// Attachments are kept outside wwwroot (App_Data/uploads) and only served through
/// AttachmentsController, which checks project access. Project icons are public images
/// under wwwroot/uploads/icons.
/// </summary>
public class FileStorage(IWebHostEnvironment env, ApplicationDbContext db)
{
    public const long MaxFileBytes = 20 * 1024 * 1024;
    static readonly string[] IconExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    string AttachmentDir => Path.Combine(env.ContentRootPath, "App_Data", "uploads");

    public string AttachmentPath(Attachment a) => Path.Combine(AttachmentDir, a.StoredName);

    /// <summary>Saves the files and returns unsaved Attachment rows; the caller sets the parent and saves.</summary>
    public async Task<List<Attachment>> SaveAttachmentsAsync(IEnumerable<IFormFile>? files, int projectId, string userId)
    {
        var result = new List<Attachment>();
        if (files is null) return result;
        Directory.CreateDirectory(AttachmentDir);

        foreach (var file in files.Where(f => f.Length > 0))
        {
            if (file.Length > MaxFileBytes)
                throw new InvalidOperationException($"\"{file.FileName}\" is larger than {MaxFileBytes / 1024 / 1024} MB.");

            var stored = Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant();
            await using (var fs = File.Create(Path.Combine(AttachmentDir, stored)))
                await file.CopyToAsync(fs);

            result.Add(new Attachment
            {
                ProjectId = projectId,
                FileName = Path.GetFileName(file.FileName),
                StoredName = stored,
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                Size = file.Length,
                UploadedById = userId,
            });
        }
        return result;
    }

    public Task<string> SaveIconAsync(IFormFile file) => SaveImageAsync(file, "icons", "The project icon");
    public Task<string> SaveAvatarAsync(IFormFile file) => SaveImageAsync(file, "avatars", "Your photo");

    /// <summary>Saves a public image under wwwroot/uploads/{folder} and returns its web path.</summary>
    async Task<string> SaveImageAsync(IFormFile file, string folder, string what)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!IconExtensions.Contains(ext))
            throw new InvalidOperationException($"{what} must be a PNG, JPG, GIF, or WebP image.");
        if (file.Length > 2 * 1024 * 1024)
            throw new InvalidOperationException($"{what} must be 2 MB or smaller.");

        var dir = Path.Combine(env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(dir);
        var name = Guid.NewGuid().ToString("N") + ext;
        await using (var fs = File.Create(Path.Combine(dir, name)))
            await file.CopyToAsync(fs);
        return $"/uploads/{folder}/{name}";
    }

    public void DeleteIcon(string? webPath) => DeleteImage(webPath, "icons");
    public void DeleteAvatar(string? webPath) => DeleteImage(webPath, "avatars");

    void DeleteImage(string? webPath, string folder)
    {
        if (string.IsNullOrEmpty(webPath) || !webPath.StartsWith($"/uploads/{folder}/")) return;
        var path = Path.Combine(env.WebRootPath, "uploads", folder, Path.GetFileName(webPath));
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Removes attachment rows (not yet saved) and their files on disk.</summary>
    public async Task DeleteAttachmentsAsync(IQueryable<Attachment> query)
    {
        var items = await query.ToListAsync();
        foreach (var a in items)
        {
            var path = AttachmentPath(a);
            if (File.Exists(path)) File.Delete(path);
        }
        db.Attachments.RemoveRange(items);
    }
}
