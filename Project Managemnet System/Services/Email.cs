using System.Net;
using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Project_Managemnet_System.Services;

/// <summary>"Email" section of appsettings.</summary>
public class EmailOptions
{
    /// <summary>"Smtp" sends real mail. "Pickup" writes .eml files to PickupDirectory (for development).</summary>
    public string Mode { get; set; } = "Pickup";
    public string PickupDirectory { get; set; } = "App_Data/mail-outbox";
    public string FromAddress { get; set; } = "no-reply@projectdesk.local";
    public string FromName { get; set; } = "ProjectDesk";
    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>How long a notification waits before it is emailed, so people active in the app aren't emailed about things they've already seen.</summary>
    public int DigestDelaySeconds { get; set; } = 120;

    public class SmtpSettings
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        /// <summary>"StartTls" (port 587), "SslOnConnect" (port 465), or "None".</summary>
        public string Security { get; set; } = "StartTls";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}

/// <summary>"App" section of appsettings.</summary>
public class AppOptions
{
    /// <summary>Public address of the site, used for links in emails sent by background jobs.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5133";
}

public record EmailMessage(string ToAddress, string ToName, string Subject, string Html, string Text);

/// <summary>Queue that pages put emails on; <see cref="EmailQueueWorker"/> sends them.</summary>
public class EmailQueue
{
    readonly Channel<EmailMessage> channel = Channel.CreateUnbounded<EmailMessage>();
    public void Enqueue(EmailMessage message) => channel.Writer.TryWrite(message);
    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken ct) => channel.Reader.ReadAllAsync(ct);
}

/// <summary>Delivers one message by SMTP or into the pickup folder.</summary>
public class EmailSender(IOptions<EmailOptions> options, IWebHostEnvironment env)
{
    public async Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        var o = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(new MailboxAddress(m.ToName, m.ToAddress));
        mime.Subject = m.Subject;
        mime.Body = new BodyBuilder { HtmlBody = m.Html, TextBody = m.Text }.ToMessageBody();

        if (string.Equals(o.Mode, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(o.Smtp.Host))
                throw new InvalidOperationException("Email:Mode is Smtp but Email:Smtp:Host is empty.");

            using var client = new SmtpClient();
            var security = o.Smtp.Security.ToLowerInvariant() switch
            {
                "sslonconnect" => SecureSocketOptions.SslOnConnect,
                "none" => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTls,
            };
            await client.ConnectAsync(o.Smtp.Host, o.Smtp.Port, security, ct);
            if (!string.IsNullOrEmpty(o.Smtp.Username))
                await client.AuthenticateAsync(o.Smtp.Username, o.Smtp.Password, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
        }
        else
        {
            var dir = Path.IsPathRooted(o.PickupDirectory) ? o.PickupDirectory : Path.Combine(env.ContentRootPath, o.PickupDirectory);
            Directory.CreateDirectory(dir);
            var safeTo = string.Concat(m.ToAddress.Split(Path.GetInvalidFileNameChars()));
            var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{safeTo}.eml");
            await mime.WriteToAsync(file, ct);
        }
    }
}

/// <summary>Sends queued emails in the background, retrying a failed message twice.</summary>
public class EmailQueueWorker(EmailQueue queue, EmailSender sender, ILogger<EmailQueueWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var m in queue.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await sender.SendAsync(m, stoppingToken);
                    log.LogInformation("Email \"{Subject}\" sent to {To}.", m.Subject, m.ToAddress);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Sending \"{Subject}\" to {To} failed (attempt {Attempt} of 3).", m.Subject, m.ToAddress, attempt);
                    if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(5 * attempt), stoppingToken);
                }
            }
        }
    }
}

/// <summary>Builds the HTML and plain-text versions of every email from one simple layout.</summary>
public static class EmailTemplates
{
    public record Line(string Text, string? Url = null);

    public static EmailMessage Build(string toAddress, string toName, string subject, string heading,
        IEnumerable<string> paragraphs, string? buttonText = null, string? buttonUrl = null,
        IEnumerable<Line>? list = null, string? footer = null)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var paras = paragraphs.ToList();
        var items = list?.ToList() ?? [];

        var html = new System.Text.StringBuilder();
        html.Append("""
            <!doctype html><html><body style="margin:0;background:#f4f6fb;font-family:Segoe UI,Roboto,Arial,sans-serif;color:#1f2937">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6fb;padding:24px 12px"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border:1px solid #e5e8f0;border-radius:12px">
            <tr><td style="padding:20px 28px;border-bottom:1px solid #e5e8f0;font-weight:700;font-size:17px;color:#4f46e5">ProjectDesk</td></tr>
            <tr><td style="padding:28px">
            """);
        html.Append($"<h1 style=\"font-size:20px;margin:0 0 16px\">{E(heading)}</h1>");
        foreach (var p in paras) html.Append($"<p style=\"margin:0 0 14px;line-height:1.55\">{E(p)}</p>");
        if (items.Count > 0)
        {
            html.Append("<ul style=\"padding-left:18px;margin:0 0 16px;line-height:1.6\">");
            foreach (var i in items)
                html.Append(i.Url is null ? $"<li>{E(i.Text)}</li>" : $"<li><a href=\"{E(i.Url)}\" style=\"color:#4f46e5\">{E(i.Text)}</a></li>");
            html.Append("</ul>");
        }
        if (buttonText is not null && buttonUrl is not null)
            html.Append($"<p style=\"margin:22px 0\"><a href=\"{E(buttonUrl)}\" style=\"background:#4f46e5;color:#ffffff;text-decoration:none;padding:11px 20px;border-radius:8px;font-weight:600;display:inline-block\">{E(buttonText)}</a></p>");
        if (buttonUrl is not null)
            html.Append($"<p style=\"font-size:12px;color:#6b7280;margin:0;word-break:break-all\">If the button doesn't work, copy this link into your browser: {E(buttonUrl)}</p>");
        html.Append("</td></tr>");
        html.Append($"<tr><td style=\"padding:16px 28px;border-top:1px solid #e5e8f0;font-size:12px;color:#6b7280\">{E(footer ?? "You're receiving this because you have a ProjectDesk account.")}</td></tr>");
        html.Append("</table></td></tr></table></body></html>");

        var text = new System.Text.StringBuilder();
        text.AppendLine(heading).AppendLine();
        foreach (var p in paras) text.AppendLine(p).AppendLine();
        foreach (var i in items) text.AppendLine("- " + i.Text + (i.Url is null ? "" : $" ({i.Url})"));
        if (items.Count > 0) text.AppendLine();
        if (buttonText is not null && buttonUrl is not null) text.AppendLine($"{buttonText}: {buttonUrl}").AppendLine();
        text.AppendLine(footer ?? "You're receiving this because you have a ProjectDesk account.");

        return new EmailMessage(toAddress, toName, subject, html.ToString(), text.ToString());
    }
}
