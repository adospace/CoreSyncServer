using System.Net;
using System.Net.Mail;
using CoreSyncServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoreSyncServer.Server.Services;

public class SmtpNotificationService(
    IOptions<SmtpSettings> options,
    ILogger<SmtpNotificationService> logger) : INotificationService
{
    private readonly SmtpSettings _settings = options.Value;

    public async Task SendAsync(string subject, string message, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            logger.LogWarning("SMTP is disabled. Notification not sent — Subject: {Subject}, Message: {Message}", subject, message);
            return;
        }

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl
        };

        if (_settings.Username is not null)
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }

        using var mailMessage = new MailMessage(_settings.FromAddress, _settings.ToAddress, subject, message);

        logger.LogInformation("Sending notification email to {To}: {Subject}", _settings.ToAddress, subject);
        await client.SendMailAsync(mailMessage, cancellationToken);
    }
}
