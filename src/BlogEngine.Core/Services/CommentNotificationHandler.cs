using System.Net;
using System.Text;
using BlogEngine.Events;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>
/// Avvisa via email chi si è iscritto ai commenti di un post quando un nuovo commento viene pubblicato
/// (porting di Post.SendNotifications). Ogni messaggio contiene il link per cancellarsi.
/// </summary>
public sealed class CommentNotificationHandler(ILinkGenerator links, IEmailQueue emails) : IEventHandler<CommentChangedEvent>
{
    public async Task HandleAsync(CommentChangedEvent blogEvent, CancellationToken cancellationToken)
    {
        var (post, comment) = (blogEvent.Post, blogEvent.Comment);
        if (blogEvent.Action is not (CommentAction.Add or CommentAction.Approve) || !comment.IsPublished || post.NotificationEmails.Count == 0)
        {
            return;
        }

        var postUrl = links.ToAbsolute(await links.PostAsync(post, cancellationToken).ConfigureAwait(false)).ToString();
        foreach (var email in post.NotificationEmails.Where(e => !string.Equals(e, comment.Email, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var unsubscribe = $"{postUrl}{(postUrl.Contains('?') ? "&" : "?")}unsubscribe-email={Uri.EscapeDataString(email)}";
            var body = new StringBuilder();
            body.Append($"<div style=\"font: 11px verdana, arial\">New Comment added by {E(comment.Author)}<br /><br />");
            body.Append(E(comment.Content).Replace("\n", "<br />", StringComparison.Ordinal)).Append("<br /><br />");
            body.Append($"<strong>Post</strong>: <a href=\"{E(postUrl)}#id_{comment.Id}\">{E(post.Title)}</a><br/>");
            body.Append("<br />_______________________________________________________________________________<br />");
            body.Append($"<a href=\"{E(unsubscribe)}\">Unsubscribe</a></div>");

            emails.Enqueue(new EmailMessage { To = [email], Subject = $"New comment on {post.Title}", HtmlBody = body.ToString() });
        }
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}

/// <summary>
/// Coda delle email di riserva, usata quando il sito non ne registra una in background:
/// il messaggio viene inviato subito.
/// </summary>
public sealed class ImmediateEmailQueue(IEmailSender sender) : IEmailQueue
{
    public void Enqueue(EmailMessage message) => sender.SendAsync(message).GetAwaiter().GetResult();
}
