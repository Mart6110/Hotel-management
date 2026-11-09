using HotelManagement.Application.Common;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HotelManagement.Infrastructure.Email;

public class EmailOptions
{
    /// <summary>SMTP host. Leave empty to log emails instead of sending them.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 25;
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>None, Auto, SslOnConnect, StartTls or StartTlsWhenAvailable.</summary>
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto;

    public string FromAddress { get; set; } = "no-reply@hotel-management.local";
    public string FromName { get; set; } = "Hotel Management";
}

/// <summary>Sends email over SMTP (Mailpit in development, any SMTP relay in production).</summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };

        var host = o.Host ?? throw new InvalidOperationException("Email:Host is not configured.");
        using var client = new SmtpClient();
        await client.ConnectAsync(host, o.Port, o.Security, ct);
        if (!string.IsNullOrEmpty(o.Username))
            await client.AuthenticateAsync(o.Username, o.Password ?? "", ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}

/// <summary>
/// Fallback when no SMTP host is configured. Logs only that an email was dropped: never the body
/// (it can contain secrets such as guest access codes) and only a masked recipient address.
/// </summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogWarning("Email not sent (no SMTP host configured) to {To}: {Subject}",
            MaskAddress(message.To), message.Subject);
        return Task.CompletedTask;
    }

    /// <summary>"gina.guest@example.com" → "g***@example.com".</summary>
    internal static string MaskAddress(string address)
    {
        var at = address.IndexOf('@');
        return at <= 0 ? "***" : $"{address[0]}***{address[at..]}";
    }
}
