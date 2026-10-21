// Invia un'email al proprietario del blog per ogni nuovo commento (se attivato nelle impostazioni
// dei commenti: "invia email per i nuovi commenti"). Porting dell'estensione SendCommentMail.

using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;

[Extension("Sends an e-mail to the blog owner whenever a comment is added", "4.0", "BlogEngine.NET")]
public sealed class SendCommentMail : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IEventHandler<CommentChangedEvent>, SendCommentMailHandler>();
}

public sealed class SendCommentMailHandler(
    ISettingsService settings,
    IBlogContext context,
    ILinkGenerator links,
    IEmailQueue emails,
    IHttpContextAccessor http) : IEventHandler<CommentChangedEvent>
{
    public async Task HandleAsync(CommentChangedEvent blogEvent, CancellationToken cancellationToken)
    {
        var (post, comment) = (blogEvent.Post, blogEvent.Comment);
        var blogSettings = await settings.GetAsync(cancellationToken: cancellationToken);
        if (blogEvent.Action != CommentAction.Add || !blogSettings.SendMailOnComment || string.IsNullOrWhiteSpace(blogSettings.Email) || comment.IsSpam)
        {
            return;
        }

        // Chi commenta il proprio post non riceve l'avviso.
        if (string.Equals(context.UserName, post.Author, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Senza moderazione si avvisa solo dei commenti pubblicati.
        if (!blogSettings.EnableCommentsModeration && !comment.IsApproved)
        {
            return;
        }

        var subject = comment.Email switch
        {
            Comment.TrackbackEmail => " trackback on ",
            Comment.PingbackEmail => " pingback on ",
            _ => " comment on ",
        };

        var postUrl = links.ToAbsolute(await links.PostAsync(post, cancellationToken)).ToString();
        var adminUrl = links.ToAbsolute(links.BlogRoot + "admin/#/content/comments").ToString();
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        var body = new StringBuilder("<div style=\"font: 11px verdana, arial\">");
        body.Append(E(comment.Content).Replace("\n", "<br />", StringComparison.Ordinal)).Append("<br /><br />");
        body.Append($"<strong>Post</strong>: <a href=\"{E(postUrl)}#id_{comment.Id}\">{E(post.Title)}</a><br /><br />");
        body.Append($"<a href=\"{E(adminUrl)}\">{(comment.IsApproved ? "Manage comments" : "Approve or delete")}</a>");
        body.Append("<br />_______________________________________________________________________________<br />");
        body.Append("<h3>Author information</h3><div style=\"font-size:10px;line-height:16px\">");
        body.Append($"<strong>Name:</strong> {E(comment.Author)}<br />");
        body.Append($"<strong>E-mail:</strong> {E(comment.Email)}<br />");
        if (comment.Website is not null)
        {
            body.Append($"<strong>Website:</strong> <a href=\"{E(comment.Website.ToString())}\">{E(comment.Website.ToString())}</a><br />");
        }

        if (!string.IsNullOrEmpty(comment.Country))
        {
            body.Append($"<strong>Country code:</strong> {E(comment.Country.ToUpperInvariant())}<br />");
        }

        body.Append($"<strong>IP address:</strong> {E(comment.IP)}<br />");
        body.Append($"<strong>User-agent:</strong> {E(http.HttpContext?.Request.Headers.UserAgent.ToString())}");
        body.Append("</div></div>");

        emails.Enqueue(new EmailMessage
        {
            To = [blogSettings.Email],
            Subject = subject.TrimStart() + post.Title,
            HtmlBody = body.ToString(),
            ReplyTo = comment.Email.Contains('@') ? comment.Email : null,
        });
    }
}
