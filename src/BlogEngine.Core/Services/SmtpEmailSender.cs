using BlogEngine.Events;
using BlogEngine.Services;
using BlogEngine.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BlogEngine.Core.Services;

/// <summary>
/// Invio email via SMTP con le impostazioni del blog (server, porta, SSL, credenziali, mittente).
/// Porting di Utils.SendMailMessage, con MailKit: con "SSL" attivo la porta 465 usa SSL diretto
/// (come smtps.aruba.it) e le altre porte STARTTLS; senza SSL la connessione resta in chiaro.
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
            using var mail = new MimeMessage();
            mail.From.Add(new MailboxAddress(blogSettings.Name, blogSettings.Email));
            mail.Subject = string.IsNullOrWhiteSpace(blogSettings.EmailSubjectPrefix)
                ? message.Subject
                : $"{blogSettings.EmailSubjectPrefix} - {message.Subject}";

            foreach (var to in message.To)
            {
                mail.To.Add(MailboxAddress.Parse(to));
            }

            if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            {
                mail.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
            }

            var body = new BodyBuilder { HtmlBody = message.HtmlBody };
            foreach (var attachment in message.Attachments)
            {
                body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
            }

            mail.Body = body.ToMessageBody();

            await DeliverAsync(mail, blogSettings.SmtpServer, blogSettings.SmtpServerPort, blogSettings.EnableSsl,
                blogSettings.SmtpUserName, blogSettings.SmtpPassword, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Invio email non riuscito: {Subject}", message.Subject);
            await events.PublishAsync(new EmailFailedEvent { BlogId = context.Blog.Id, Message = message, Error = ex.Message }, cancellationToken).ConfigureAwait(false);
            return ex.Message;
        }

        await events.PublishAsync(new EmailSentEvent { BlogId = context.Blog.Id, Message = message }, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Consegna un messaggio al server SMTP indicato. Usato dall'invio normale e dalla prova delle impostazioni
    /// nell'amministrazione, così la prova si comporta esattamente come le email vere (anche con l'SSL diretto
    /// sulla porta 465 di smtps.aruba.it, che System.Net.Mail.SmtpClient non supporta).
    /// </summary>
    public static async Task DeliverAsync(MimeMessage mail, string server, int port, bool enableSsl, string? userName, string? password, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        await client.ConnectAsync(server, port, Security(enableSsl, port), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(userName))
        {
            await client.AuthenticateAsync(userName, password ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(mail, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Con SSL attivo la porta 465 usa SSL diretto e le altre STARTTLS; senza SSL la connessione resta in chiaro.</summary>
    public static SecureSocketOptions Security(bool enableSsl, int port) =>
        !enableSsl ? SecureSocketOptions.None
        : port == 465 ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTls;
}
