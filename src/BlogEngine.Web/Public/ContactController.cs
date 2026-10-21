using System.Net;
using System.Net.Mail;
using System.Text;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Text;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogEngine.Web.Public;

/// <summary>Modulo di contatto (porting di contact.aspx): invia un'email all'indirizzo del blog.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ContactController(SiteRenderer site, PublicContent content, IEmailSender mailer) : Controller
{
    /// <summary>Dimensione massima dell'allegato (5 MB).</summary>
    public const long MaxAttachmentSize = 5 * 1024 * 1024;

    [HttpGet("/contact")]
    public async Task<IActionResult> Index(string? name, string? email, string? subject) =>
        await ShowAsync(new ContactModel { Name = name ?? string.Empty, Email = email ?? string.Empty, Subject = subject ?? string.Empty });

    [HttpPost("/contact")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(BlogActionsController.FormsRateLimit)]
    [RequestSizeLimit(MaxAttachmentSize + (1024 * 1024))]
    public async Task<IActionResult> Send([FromForm] string? name, [FromForm] string? email, [FromForm] string? subject, [FromForm] string? message, [FromForm] string? hp, IFormFile? attachment)
    {
        var blogSettings = await site.GetSettingsAsync();
        var form = new ContactModel
        {
            Name = name?.Trim() ?? string.Empty,
            Email = email?.Trim() ?? string.Empty,
            Subject = subject?.Trim() ?? string.Empty,
            Text = message?.Trim() ?? string.Empty,
        };

        var errors = new List<string>();
        if (form.Name.Length is 0 or > 100)
        {
            errors.Add($"{site.T("name")}: {site.T("required")}");
        }

        if (form.Email.Length is 0 or > 200 || !MailAddress.TryCreate(form.Email, out _))
        {
            errors.Add(site.T("enterValidEmail"));
        }

        if (form.Subject.Length is 0 or > 200)
        {
            errors.Add($"{site.T("subject")}: {site.T("required")}");
        }

        if (form.Text.Length is 0 or > 20000)
        {
            errors.Add($"{site.T("message")}: {site.T("required")}");
        }

        if (attachment is { Length: > MaxAttachmentSize })
        {
            errors.Add($"{site.T("attachFile")}: max {MaxAttachmentSize / 1024 / 1024} MB");
        }

        // Il campo nascosto "hp" resta vuoto per le persone: se è compilato il modulo viene da un programma automatico.
        if (errors.Count > 0 || !string.IsNullOrEmpty(hp))
        {
            return await ShowAsync(new ContactModel { Name = form.Name, Email = form.Email, Subject = form.Subject, Text = form.Text, Errors = errors });
        }

        var attachments = new List<EmailAttachment>();
        if (attachment is { Length: > 0 } && blogSettings.EnableContactAttachments)
        {
            using var buffer = new MemoryStream();
            await attachment.CopyToAsync(buffer);
            attachments.Add(new EmailAttachment(Path.GetFileName(attachment.FileName), attachment.ContentType ?? "application/octet-stream", buffer.ToArray()));
        }

        string E(string value) => WebUtility.HtmlEncode(value);
        var body = new StringBuilder("<div style=\"font: 11px verdana, arial\">");
        body.Append(E(form.Text).Replace("\n", "<br />", StringComparison.Ordinal)).Append("<br /><br /><hr /><br />");
        body.Append($"<h3>{E(site.T("contactAuthorInformation"))}</h3><div style=\"font-size:10px;line-height:16px\">");
        body.Append($"<strong>{E(site.T("name"))}:</strong> {E(form.Name)}<br />");
        body.Append($"<strong>{E(site.T("email"))}:</strong> {E(form.Email)}<br />");
        body.Append($"<strong>{E(site.T("contactIPAddress"))}:</strong> {E(HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty)}<br />");
        body.Append($"<strong>{E(site.T("contactUserAgent"))}:</strong> {E(Request.Headers.UserAgent.ToString())}</div></div>");

        var error = await mailer.SendAsync(new EmailMessage
        {
            To = [blogSettings.Email],
            Subject = $"{site.T("email").ToLowerInvariant()} - {form.Subject}",
            HtmlBody = body.ToString(),
            ReplyTo = form.Email,
            Attachments = attachments,
        });

        var detailed = await content.CanAsync(Rights.ViewDetailedErrorMessages);
        return await ShowAsync(new ContactModel
        {
            Sent = error is null,
            Failed = error is not null,
            Name = form.Name,
            Email = form.Email,
            Subject = form.Subject,
            Text = error is null ? string.Empty : form.Text,
            Errors = error is not null && detailed ? [error] : [],
        });
    }

    private async Task<IActionResult> ShowAsync(ContactModel form)
    {
        var blogSettings = await site.GetSettingsAsync();
        var model = new ContactModel
        {
            Message = new HtmlString(blogSettings.ContactFormMessage),
            ThankMessage = new HtmlString(blogSettings.ContactThankMessage),
            ErrorMessage = new HtmlString(blogSettings.ContactErrorMessage),
            AllowAttachments = blogSettings.EnableContactAttachments,
            Sent = form.Sent,
            Failed = form.Failed,
            Name = form.Name,
            Email = form.Email,
            Subject = form.Subject,
            Text = form.Text,
            Errors = form.Errors,
        };

        return await site.PageAsync("contact", new PageMeta
        {
            Title = site.T("contact"),
            Description = HtmlText.Strip(blogSettings.ContactFormMessage),
        }, new Dictionary<string, object?>
        {
            ["contact"] = model,
        });
    }
}
