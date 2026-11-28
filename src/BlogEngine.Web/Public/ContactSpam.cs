using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BlogEngine.Core.Data;
using BlogEngine.Core.Extensions;
using BlogEngine.Core.Services;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.DataProtection;

namespace BlogEngine.Web.Public;

/// <summary>Motivo per cui il filtro antispam ha trattenuto un messaggio del modulo di contatto.</summary>
public sealed record ContactBlock(string BlockedBy, string Reason);

/// <summary>
/// Filtro antispam del modulo di contatto. Ogni controllo si attiva o disattiva dall'amministrazione
/// (Contenuto → Messaggi bloccati → Regole); di serie sono tutti attivi. Nell'ordine:
/// 1. campo trappola nascosto compilato (lo vedono solo i programmi automatici);
/// 2. modulo inviato troppo in fretta dopo l'apertura, o con l'ora di apertura alterata;
/// 3. regole personalizzate: lista nera (blocca) e lista bianca (fa passare senza gli altri controlli)
///    di email, domini, IP, parole, domini dei link;
/// 4. link nel nome o troppi link nel testo;
/// 5. filtri dei commenti impostati dall'amministratore ("consenti" fa passare il messaggio);
/// 6. qualità dei link: domini segnalati nelle liste pubbliche di spam, phishing e malware;
/// 7. filtri antispam esterni attivi per i commenti (StopForumSpam, Akismet).
/// </summary>
public sealed partial class ContactSpamFilter(
    SiteRenderer site,
    ICommentFilterStore filterStore,
    IEnumerable<ICommentSpamFilter> spamFilters,
    IExtensionGate gate,
    IBlogContext context,
    IDataProtectionProvider protection,
    ContactFilterSettingsService settingsService,
    ILinkReputationService linkReputation,
    TimeProvider time,
    ILogger<ContactSpamFilter> logger)
{
    private IDataProtector Protector => protection.CreateProtector("BlogEngine.Contact.Opened");

    /// <summary>Ora di apertura del modulo, cifrata perché non si possa falsificare.</summary>
    public string IssueToken() =>
        Protector.Protect(time.GetUtcNow().UtcTicks.ToString(CultureInfo.InvariantCulture));

    public async Task<ContactBlock?> CheckAsync(ContactMessage message, string? honeypot, string? openedToken, CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        if (settings.Honeypot && !string.IsNullOrEmpty(honeypot))
        {
            return new("Honeypot", site.T("contactSpamHoneypot"));
        }

        // Un tema personalizzato potrebbe non avere il campo: senza gettone il controllo del tempo si salta.
        if (settings.Timer && !string.IsNullOrEmpty(openedToken))
        {
            if (OpenedAt(openedToken) is not { } opened)
            {
                return new("Timer", site.T("contactSpamBadToken"));
            }

            var elapsed = time.GetUtcNow() - opened;
            if (elapsed < TimeSpan.FromSeconds(Math.Max(0, settings.MinSeconds)))
            {
                return new("Timer", Format("contactSpamTooFast", Math.Max(0, (int)elapsed.TotalSeconds)));
            }
        }

        var hosts = DnsLinkReputationService.LinkHosts($"{message.Name}\n{message.Subject}\n{message.Text}");
        if (settings.Rules.FirstOrDefault(r => r.Enabled && r.Matches(message, hosts)) is { } custom)
        {
            await settingsService.RecordHitAsync(custom.Id, cancellationToken);
            if (custom.Action == ContactRuleAction.Allow)
            {
                return null;
            }

            return new("Rule", Format("contactSpamRule", RuleText(custom)));
        }

        if (settings.LinkInName && CountLinks(message.Name) > 0)
        {
            return new("Links", site.T("contactSpamLinkInName"));
        }

        var links = CountLinks(message.Subject) + CountLinks(message.Text);
        if (settings.MaxLinksEnabled && links > Math.Max(0, settings.MaxLinks))
        {
            return new("Links", Format("contactSpamLinks", links, settings.MaxLinks));
        }

        var comment = new Comment
        {
            Author = message.Name,
            Email = message.Email,
            IP = message.IP,
            Content = $"{message.Subject}\n{message.Text}",
            DateCreated = message.DateCreated,
        };

        if (settings.CommentFilters)
        {
            var rules = await filterStore.GetRulesAsync(context.Blog.Id, cancellationToken);
            if (rules.FirstOrDefault(r => CommentModerationHandler.Matches(r, comment)) is { } rule)
            {
                return rule.Action == CommentFilterAction.Allow
                    ? null
                    : new("Filter", Format("contactSpamFilter", rule.Subject, rule.Operator, rule.Filter));
            }
        }

        if (settings.LinkReputation && hosts.Count > 0)
        {
            try
            {
                if (await linkReputation.CheckAsync(hosts, cancellationToken) is { } bad)
                {
                    return new("LinkReputation", Format("contactSpamBadLink", bad.Domain, bad.Category, bad.List));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Controllo della qualità dei link non riuscito.");
            }
        }

        if (!settings.ExternalServices)
        {
            return null;
        }

        // I filtri esterni sono pensati per i commenti: ricevono il modulo di contatto come commento a una "pagina".
        var page = new Post { BlogId = context.Blog.Id, Title = site.T("contact"), Slug = "contact" };
        foreach (var filter in spamFilters.Where(gate.IsActive).OrderBy(f => f.Priority).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (await filter.CheckAsync(page, comment, cancellationToken) == SpamCheckResult.Spam)
                {
                    return new(filter.Name, Format("contactSpamService", filter.Name));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Il filtro {Filter} non ha potuto controllare il modulo di contatto.", filter.Name);
            }
        }

        return null;
    }

    /// <summary>"Dominio email = spam.com", "IP = 203.0.113.0/24"... nella lingua del blog.</summary>
    private string RuleText(ContactFilterRule rule)
    {
        var field = site.T("contactField" + rule.Field);
        var op = rule.Field is ContactRuleField.EmailDomain or ContactRuleField.IP or ContactRuleField.LinkDomain
            ? "="
            : site.T("contactOperator" + rule.Operator);
        return $"{field} {op} \"{rule.Value.Trim()}\"";
    }

    /// <summary>Indirizzi web nel testo: http(s)://, www., [url] dei forum, tag &lt;a&gt;.</summary>
    public static int CountLinks(string text) => LinkPattern().Count(text);

    private DateTimeOffset? OpenedAt(string token)
    {
        try
        {
            return long.TryParse(Protector.Unprotect(token), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks > 0 && ticks <= DateTimeOffset.MaxValue.UtcTicks
                ? new DateTimeOffset(ticks, TimeSpan.Zero)
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private string Format(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, site.T(key), values);

    [GeneratedRegex(@"https?://|www\.|\[url|<a\s", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}

/// <summary>Email al proprietario del blog con il messaggio del modulo di contatto (anche per quelli sbloccati).</summary>
public sealed class ContactMailer(SiteRenderer site, IEmailSender mailer)
{
    /// <summary>Null se l'invio è riuscito, altrimenti la descrizione dell'errore.</summary>
    public async Task<string?> SendAsync(ContactMessage message, CancellationToken cancellationToken = default)
    {
        var blogSettings = await site.GetSettingsAsync();
        static string E(string value) => WebUtility.HtmlEncode(value);
        var body = new StringBuilder("<div style=\"font: 11px verdana, arial\">");
        body.Append(E(message.Text).Replace("\n", "<br />", StringComparison.Ordinal)).Append("<br /><br /><hr /><br />");
        body.Append($"<h3>{E(site.T("contactAuthorInformation"))}</h3><div style=\"font-size:10px;line-height:16px\">");
        body.Append($"<strong>{E(site.T("name"))}:</strong> {E(message.Name)}<br />");
        body.Append($"<strong>{E(site.T("email"))}:</strong> {E(message.Email)}<br />");
        body.Append($"<strong>{E(site.T("contactIPAddress"))}:</strong> {E(message.IP)}<br />");
        body.Append($"<strong>{E(site.T("contactUserAgent"))}:</strong> {E(message.UserAgent)}</div></div>");

        var attachments = message.AttachmentData is { Length: > 0 } data
            ? new List<EmailAttachment> { new(message.AttachmentName, string.IsNullOrEmpty(message.AttachmentType) ? "application/octet-stream" : message.AttachmentType, data) }
            : [];

        return await mailer.SendAsync(new EmailMessage
        {
            To = [blogSettings.Email],
            Subject = $"{site.T("email").ToLowerInvariant()} - {message.Subject}",
            HtmlBody = body.ToString(),
            ReplyTo = message.Email,
            Attachments = attachments,
        }, cancellationToken);
    }
}

/// <summary>
/// Archivio dei messaggi trattenuti dal filtro antispam: si possono rileggere, recapitare o eliminare.
/// I messaggi più vecchi di <see cref="Retention"/> vengono eliminati automaticamente.
/// </summary>
public sealed class ContactMessageService(IEntityStore<ContactMessage> store, IBlogContext context, TimeProvider time, ContactMailer mailer)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IReadOnlyList<ContactMessage>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await store.GetAllAsync(context.Blog.Id, cancellationToken))
            .OrderByDescending(m => m.DateCreated)
            .ToList();

    /// <summary>Registra un messaggio bloccato ed elimina quelli scaduti.</summary>
    public async Task BlockAsync(ContactMessage message, ContactBlock block, CancellationToken cancellationToken = default)
    {
        message.BlogId = context.Blog.Id;
        message.Status = ContactMessageStatus.Blocked;
        message.BlockedBy = block.BlockedBy;
        message.Reason = block.Reason;
        await store.SaveAsync(message, cancellationToken);

        var limit = time.GetUtcNow().UtcDateTime - Retention;
        foreach (var old in (await store.GetAllAsync(context.Blog.Id, cancellationToken)).Where(m => m.DateCreated < limit))
        {
            await store.DeleteAsync(old.BlogId, old.Id, cancellationToken);
        }
    }

    /// <summary>
    /// Recapita un messaggio bloccato. Null se è stato inviato, altrimenti l'errore (il messaggio resta bloccato).
    /// </summary>
    public async Task<string?> ReleaseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var message = (await store.GetAllAsync(context.Blog.Id, cancellationToken)).FirstOrDefault(m => m.Id == id)
                ?? throw new KeyNotFoundException();
            if (message.Status == ContactMessageStatus.Released)
            {
                return null;
            }

            if (await mailer.SendAsync(message, cancellationToken) is { } error)
            {
                return error;
            }

            message.Status = ContactMessageStatus.Released;
            message.DateReleased = time.GetUtcNow().UtcDateTime;
            message.ReleasedBy = context.UserName ?? string.Empty;
            await store.SaveAsync(message, cancellationToken);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if ((await store.GetAllAsync(context.Blog.Id, cancellationToken)).All(m => m.Id != id))
        {
            return false;
        }

        await store.DeleteAsync(context.Blog.Id, id, cancellationToken);
        return true;
    }

    /// <summary>Elimina tutti i messaggi ancora bloccati (quelli recapitati restano come promemoria).</summary>
    public async Task<int> DeleteBlockedAsync(CancellationToken cancellationToken = default)
    {
        var blocked = (await store.GetAllAsync(context.Blog.Id, cancellationToken)).Where(m => m.Status == ContactMessageStatus.Blocked).ToList();
        foreach (var message in blocked)
        {
            await store.DeleteAsync(message.BlogId, message.Id, cancellationToken);
        }

        return blocked.Count;
    }
}
