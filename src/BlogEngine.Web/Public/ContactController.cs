using System.Net.Mail;
using BlogEngine.Models;
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
public sealed class ContactController(
    SiteRenderer site,
    PublicContent content,
    ContactMailer mailer,
    ContactSpamFilter spamFilter,
    ContactMessageService blocked,
    Extensions.CaptchaService captcha,
    TimeProvider time,
    ILogger<ContactController> logger) : Controller
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
    public async Task<IActionResult> Send([FromForm] string? name, [FromForm] string? email, [FromForm] string? subject, [FromForm] string? message, [FromForm] string? hp, [FromForm] string? opened, IFormFile? attachment)
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

        if (!await captcha.ValidateAsync(CaptchaForm.Contact, Request.Form))
        {
            errors.Add(site.T("incorrectSimpleCaptcha"));
        }

        if (attachment is { Length: > MaxAttachmentSize })
        {
            errors.Add($"{site.T("attachFile")}: max {MaxAttachmentSize / 1024 / 1024} MB");
        }

        if (errors.Count > 0)
        {
            return await ShowAsync(new ContactModel { Name = form.Name, Email = form.Email, Subject = form.Subject, Text = form.Text, Errors = errors });
        }

        var submitted = new ContactMessage
        {
            DateCreated = time.GetUtcNow().UtcDateTime,
            Name = form.Name,
            Email = form.Email,
            Subject = form.Subject,
            Text = form.Text,
            IP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            UserAgent = Request.Headers.UserAgent.ToString(),
        };

        if (attachment is { Length: > 0 } && blogSettings.EnableContactAttachments)
        {
            using var buffer = new MemoryStream();
            await attachment.CopyToAsync(buffer);
            submitted.AttachmentName = Path.GetFileName(attachment.FileName);
            submitted.AttachmentType = attachment.ContentType ?? "application/octet-stream";
            submitted.AttachmentData = buffer.ToArray();
        }

        // Spam: il messaggio non parte ma resta nell'amministrazione (Contenuto → Messaggi bloccati), da dove si
        // può recapitare. Chi l'ha inviato vede il solito ringraziamento, così un programma non capisce cosa evitare.
        if (await spamFilter.CheckAsync(submitted, hp, opened) is { } block)
        {
            await blocked.BlockAsync(submitted, block);
            logger.LogInformation("Modulo di contatto bloccato ({Filter}): {Reason}", block.BlockedBy, block.Reason);
            return await ShowAsync(new ContactModel { Sent = true });
        }

        var error = await mailer.SendAsync(submitted);

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
            Captcha = form.Sent ? HtmlString.Empty : await captcha.RenderAsync(CaptchaForm.Contact),
            Sent = form.Sent,
            Failed = form.Failed,
            Name = form.Name,
            Email = form.Email,
            Subject = form.Subject,
            Text = form.Text,
            Errors = form.Errors,
            OpenedToken = form.Sent ? string.Empty : spamFilter.IssueToken(),
        };

        return await site.PageAsync("contact", new PageMeta
        {
            Title = site.T("contact"),
            Description = HtmlText.ToPlainText(blogSettings.ContactFormMessage),
        }, new Dictionary<string, object?>
        {
            ["contact"] = model,
        });
    }
}
