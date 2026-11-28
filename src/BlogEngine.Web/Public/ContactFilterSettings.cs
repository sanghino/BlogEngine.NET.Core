using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlogEngine.Core.Data;
using BlogEngine.Models;
using BlogEngine.Services;

namespace BlogEngine.Web.Public;

/// <summary>Dato del messaggio controllato da una regola personalizzata.</summary>
public enum ContactRuleField
{
    /// <summary>Indirizzo email completo del mittente.</summary>
    Email,

    /// <summary>Dominio dell'email ("spam.com" vale anche per "posta.spam.com").</summary>
    EmailDomain,

    /// <summary>Indirizzo IP: esatto, iniziale ("203.0.113." o "203.0.113.*") o rete CIDR ("203.0.113.0/24").</summary>
    IP,

    Name,

    Subject,

    Text,

    /// <summary>Dominio di uno dei link scritti in oggetto o messaggio (sottodomini compresi).</summary>
    LinkDomain,
}

/// <summary>Confronto per nome, email, oggetto e testo (per domini e IP il confronto è sempre quello del tipo).</summary>
public enum ContactRuleOperator
{
    Contains,
    Equals,

    /// <summary>Espressione regolare (senza distinguere le maiuscole).</summary>
    Regex,
}

public enum ContactRuleAction
{
    /// <summary>Lista nera: il messaggio viene trattenuto.</summary>
    Block,

    /// <summary>Lista bianca: il messaggio parte senza altri controlli (tranne il campo trappola e il tempo).</summary>
    Allow,
}

/// <summary>Regola personalizzata del filtro dei contatti (lista nera o bianca).</summary>
public sealed class ContactFilterRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public bool Enabled { get; set; } = true;

    public ContactRuleField Field { get; set; }

    public ContactRuleOperator Operator { get; set; }

    public string Value { get; set; } = string.Empty;

    public ContactRuleAction Action { get; set; }

    /// <summary>Promemoria dell'amministratore (es. "spam di gennaio").</summary>
    public string Note { get; set; } = string.Empty;

    public DateTime DateCreated { get; set; }

    /// <summary>Quante volte la regola è scattata.</summary>
    public int Hits { get; set; }

    public DateTime? LastHit { get; set; }

    /// <summary>Vero se la regola vale per il messaggio.</summary>
    public bool Matches(ContactMessage message, IReadOnlyCollection<string> linkHosts)
    {
        var value = Value.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        return Field switch
        {
            ContactRuleField.EmailDomain => IsDomainOf(DomainOf(message.Email), value.TrimStart('@')),
            ContactRuleField.IP => IpMatches(message.IP, value),
            ContactRuleField.LinkDomain => linkHosts.Any(host => IsDomainOf(host, value)),
            ContactRuleField.Email => Compare(message.Email, value),
            ContactRuleField.Name => Compare(message.Name, value),
            ContactRuleField.Subject => Compare(message.Subject, value),
            _ => Compare(message.Text, value),
        };
    }

    /// <summary>Errore della regola (valore vuoto, espressione o rete non valide), null se è valida.</summary>
    public string? Validate()
    {
        var value = Value.Trim();
        if (value.Length == 0)
        {
            return "empty";
        }

        if (Field == ContactRuleField.IP && value.Contains('/', StringComparison.Ordinal) && !IPNetwork.TryParse(value, out _))
        {
            return "ip";
        }

        if (Field is ContactRuleField.Email or ContactRuleField.Name or ContactRuleField.Subject or ContactRuleField.Text && Operator == ContactRuleOperator.Regex)
        {
            try
            {
                _ = new Regex(value, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                return "regex";
            }
        }

        return null;
    }

    public static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at < 0 ? string.Empty : email[(at + 1)..].Trim().TrimEnd('.');
    }

    /// <summary>"posta.spam.com" è del dominio "spam.com"; "nospam.com" no.</summary>
    public static bool IsDomainOf(string host, string domain)
    {
        domain = domain.Trim().TrimStart('.').TrimEnd('.');
        return domain.Length > 0
            && (host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IpMatches(string ip, string rule)
    {
        if (ip.Length == 0)
        {
            return false;
        }

        if (rule.Contains('/', StringComparison.Ordinal))
        {
            return IPNetwork.TryParse(rule, out var network) && IPAddress.TryParse(ip, out var address)
                && network.Contains(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
        }

        if (rule.EndsWith('*'))
        {
            return ip.StartsWith(rule.TrimEnd('*'), StringComparison.OrdinalIgnoreCase);
        }

        return rule.EndsWith('.') || rule.EndsWith(':')
            ? ip.StartsWith(rule, StringComparison.OrdinalIgnoreCase)
            : ip.Equals(rule, StringComparison.OrdinalIgnoreCase);
    }

    private bool Compare(string text, string value)
    {
        switch (Operator)
        {
            case ContactRuleOperator.Equals:
                return text.Trim().Equals(value, StringComparison.OrdinalIgnoreCase);
            case ContactRuleOperator.Regex:
                try
                {
                    return Regex.IsMatch(text, value, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
                }
                catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
                {
                    return false;
                }

            default:
                return text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>
/// Impostazioni del filtro antispam dei contatti: controlli automatici attivabili uno per uno
/// (tutti attivi di serie) e regole personalizzate.
/// </summary>
public sealed class ContactFilterSettings
{
    public bool Honeypot { get; set; } = true;

    public bool Timer { get; set; } = true;

    public int MinSeconds { get; set; } = 3;

    public bool LinkInName { get; set; } = true;

    public bool MaxLinksEnabled { get; set; } = true;

    public int MaxLinks { get; set; } = 2;

    /// <summary>Link controllati nelle liste pubbliche dei domini di spam, phishing e malware.</summary>
    public bool LinkReputation { get; set; } = true;

    /// <summary>Regole dei filtri dei commenti (Commenti → Filtri).</summary>
    public bool CommentFilters { get; set; } = true;

    /// <summary>Filtri antispam esterni attivi per i commenti (StopForumSpam, Akismet).</summary>
    public bool ExternalServices { get; set; } = true;

    public List<ContactFilterRule> Rules { get; set; } = [];
}

/// <summary>Lettura e salvataggio delle impostazioni del filtro, per blog.</summary>
public sealed class ContactFilterSettingsService(IExtensionDataStore store, IBlogContext context, TimeProvider time)
{
    public const string DataId = "contact-filter";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<ContactFilterSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var json = await store.LoadAsync(context.Blog.Id, ExtensionDataKind.Extension, DataId, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ContactFilterSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<ContactFilterSettings>(json, Json) ?? new ContactFilterSettings();
        }
        catch (JsonException)
        {
            return new ContactFilterSettings();
        }
    }

    public async Task SaveAsync(ContactFilterSettings settings, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            // I contatori delle regole li aggiorna il filtro: si tengono quelli salvati.
            var saved = (await GetAsync(cancellationToken)).Rules.ToDictionary(r => r.Id);
            foreach (var rule in settings.Rules)
            {
                if (saved.TryGetValue(rule.Id, out var previous))
                {
                    rule.Hits = previous.Hits;
                    rule.LastHit = previous.LastHit;
                    rule.DateCreated = previous.DateCreated;
                }
                else if (rule.DateCreated == default)
                {
                    rule.DateCreated = time.GetUtcNow().UtcDateTime;
                }
            }

            await WriteAsync(settings, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Aggiunge una regola (es. dal messaggio bloccato: "blocca questo dominio"); false se c'era già.</summary>
    public async Task<bool> AddRuleAsync(ContactFilterRule rule, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var settings = await GetAsync(cancellationToken);
            if (settings.Rules.Any(r => r.Field == rule.Field && r.Action == rule.Action && r.Value.Trim().Equals(rule.Value.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            rule.DateCreated = time.GetUtcNow().UtcDateTime;
            settings.Rules.Add(rule);
            await WriteAsync(settings, cancellationToken);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Conta un intervento della regola.</summary>
    public async Task RecordHitAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var settings = await GetAsync(cancellationToken);
            if (settings.Rules.FirstOrDefault(r => r.Id == ruleId) is { } rule)
            {
                rule.Hits++;
                rule.LastHit = time.GetUtcNow().UtcDateTime;
                await WriteAsync(settings, cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private Task WriteAsync(ContactFilterSettings settings, CancellationToken cancellationToken) =>
        store.SaveAsync(context.Blog.Id, ExtensionDataKind.Extension, DataId, JsonSerializer.Serialize(settings, Json), cancellationToken);
}

/// <summary>Esito del controllo di un dominio in una lista pubblica.</summary>
public sealed record LinkReputationHit(string Domain, string List, string Category);

/// <summary>Stato di una lista pubblica, per la prova dall'amministrazione.</summary>
public sealed record LinkReputationStatus(string List, string Zone, string State);

/// <summary>Qualità dei link: domini segnalati per spam, phishing o malware.</summary>
public interface ILinkReputationService
{
    /// <summary>Il primo dominio segnalato fra quelli indicati, o null (anche se le liste non rispondono).</summary>
    Task<LinkReputationHit?> CheckAsync(IReadOnlyCollection<string> hosts, CancellationToken cancellationToken = default);

    /// <summary>Prova le liste con i loro domini di test: "listed" vuol dire che funzionano.</summary>
    Task<IReadOnlyList<LinkReputationStatus>> TestAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Liste DNS dei domini usati negli spam (le stesse dei filtri di posta come SpamAssassin): Spamhaus DBL e SURBL.
/// Non servono chiavi: si chiede al DNS "dominio.lista" e una risposta 127.x vuol dire che il dominio è segnalato.
/// Le liste rifiutano le domande che arrivano dai DNS pubblici (Google, Cloudflare): in quel caso rispondono
/// con un codice di errore e il controllo viene saltato, senza bloccare il messaggio.
/// </summary>
public sealed partial class DnsLinkReputationService(ILogger<DnsLinkReputationService> logger) : ILinkReputationService
{
    /// <summary>Domini controllati al massimo per messaggio, per non rallentare l'invio.</summary>
    public const int MaxDomains = 8;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private static readonly (string Name, string Zone, string TestDomain)[] Lists =
    [
        ("Spamhaus DBL", "dbl.spamhaus.org", "dbltest.com"),
        ("SURBL", "multi.surbl.org", "surbl-org-permanent-test-point.com"),
    ];

    public async Task<LinkReputationHit?> CheckAsync(IReadOnlyCollection<string> hosts, CancellationToken cancellationToken = default)
    {
        var domains = hosts
            .Where(h => !IPAddress.TryParse(h, out _) && h.Contains('.', StringComparison.Ordinal))
            .SelectMany(h => new[] { h, RegisteredDomain(h) })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxDomains)
            .ToList();
        if (domains.Count == 0)
        {
            return null;
        }

        var checks = domains.SelectMany(d => Lists.Select(l => LookupAsync(d, l.Name, l.Zone, cancellationToken))).ToList();
        var results = await Task.WhenAll(checks);
        return results.FirstOrDefault(r => r is { State: "listed" }) is { } hit ? new LinkReputationHit(hit.Domain, hit.List, hit.Category) : null;
    }

    public async Task<IReadOnlyList<LinkReputationStatus>> TestAsync(CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(Lists.Select(l => LookupAsync(l.TestDomain, l.Name, l.Zone, cancellationToken)));
        return results.Select((r, i) => new LinkReputationStatus(Lists[i].Name, Lists[i].Zone, r.State)).ToList();
    }

    /// <summary>"posta.esempio.co.uk" → "esempio.co.uk"; "www.spam.com" → "spam.com" (approssimato, basta per le liste).</summary>
    public static string RegisteredDomain(string host)
    {
        var labels = host.Trim('.').Split('.');
        if (labels.Length <= 2)
        {
            return host;
        }

        var secondLevel = labels[^2];
        var keep = labels[^1].Length == 2 && secondLevel is "co" or "com" or "net" or "org" or "gov" or "ac" or "edu" ? 3 : 2;
        return string.Join('.', labels[^Math.Min(keep, labels.Length)..]);
    }

    /// <summary>Nomi host dei link scritti nel testo.</summary>
    public static IReadOnlyCollection<string> LinkHosts(string text)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in LinkHost().Matches(text))
        {
            var host = match.Groups["host"].Value.TrimEnd('.').ToLowerInvariant();
            if (host.Length > 0)
            {
                hosts.Add(host);
            }
        }

        return hosts;
    }

    private async Task<(string Domain, string List, string State, string Category)> LookupAsync(string domain, string list, string zone, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync($"{domain}.{zone}", AddressFamily.InterNetwork, timeout.Token);
            var answer = addresses.FirstOrDefault()?.GetAddressBytes();
            if (answer is null || answer[0] != 127)
            {
                return (domain, list, "notlisted", string.Empty);
            }

            return Interpret(zone, answer) is { } category
                ? (domain, list, "listed", category)
                : (domain, list, "refused", string.Empty);
        }
        catch (SocketException)
        {
            // Nome inesistente: il dominio non è nella lista.
            return (domain, list, "notlisted", string.Empty);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("{List}: nessuna risposta per {Domain}.", list, domain);
            return (domain, list, "timeout", string.Empty);
        }
    }

    /// <summary>Categoria della segnalazione, o null per i codici di errore (es. domanda da un DNS pubblico).</summary>
    private static string? Interpret(string zone, byte[] answer)
    {
        if (zone.StartsWith("dbl.", StringComparison.Ordinal))
        {
            // 127.0.1.x: 2 spam, 4 phishing, 5 malware, 6 botnet, 102-106 siti violati; 127.255.255.x errori.
            return answer[1] == 0 && answer[2] == 1 && answer[3] >= 2
                ? answer[3] switch { 4 or 104 => "phishing", 5 or 105 => "malware", 6 or 106 => "botnet", _ => "spam" }
                : null;
        }

        // SURBL: 127.0.0.x con i bit delle liste (8 phishing, 16 malware, 64 abusi, 128 cracked); 127.0.0.1 = accesso negato.
        return answer[1] == 0 && answer[2] == 0 && answer[3] > 1
            ? (answer[3] & 8) != 0 ? "phishing" : (answer[3] & 16) != 0 ? "malware" : "spam"
            : null;
    }

    [GeneratedRegex(@"(?:https?://|\bwww\.)(?<host>[a-z0-9](?:[a-z0-9\-\.]*[a-z0-9])?)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkHost();
}
