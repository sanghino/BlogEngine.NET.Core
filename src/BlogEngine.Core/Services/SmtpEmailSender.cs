using System.Net;
using System.Net.Mail;
using BlogEngine.Events;
using BlogEngine.Services;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Core.Services;

/// <summary>
/// Invio email via SMTP con le impostazioni del blog (server, porta, SSL, credenziali, mittente).
/// Porting di Utils.SendMailMessage.
/// </summary>
public sealed class SmtpEmailSender(
    IBlogContext context,
    ISettingsService settings,
    IEventPublisher events,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var blogSettings = await settings.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            using var mail = new MailMessage
            {
                From = new MailAddress(blogSettings.Email, blogSettings.Name),
                Subject = string.IsNullOrWhiteSpace(blogSettings.EmailSubjectPrefix)
                    ? message.Subject
                    : $"{blogSettings.EmailSubjectPrefix} - {message.Subject}",
                Body = message.HtmlBody,
                IsBodyHtml = true,
            };

            foreach (var to in message.To)
            {
                mail.To.Add(to);
            }

            if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            {
                mail.ReplyToList.Add(message.ReplyTo);
            }

            foreach (var attachment in message.Attachments)
            {
                mail.Attachments.Add(new Attachment(new MemoryStream(attachment.Content), attachment.FileName, attachment.ContentType));
            }

            using var client = new SmtpClient(blogSettings.SmtpServer, blogSettings.SmtpServerPort)
            {
                EnableSsl = blogSettings.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };

            if (!string.IsNullOrWhiteSpace(blogSettings.SmtpUserName))
            {
                client.Credentials = new NetworkCredential(blogSettings.SmtpUserName, blogSettings.SmtpPassword);
            }

            await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException or ArgumentException)
        {
            logger.LogWarning(ex, "Invio email non riuscito: {Subject}", message.Subject);
            await events.PublishAsync(new EmailFailedEvent { BlogId = context.Blog.Id, Message = message, Error = ex.Message }, cancellationToken).ConfigureAwait(false);
            return ex.Message;
        }

        await events.PublishAsync(new EmailSentEvent { BlogId = context.Blog.Id, Message = message }, cancellationToken).ConfigureAwait(false);
        return null;
    }
}
