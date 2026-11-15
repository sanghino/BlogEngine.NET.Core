using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlogEngine.Core.Configuration;
using BlogEngine.Services;

namespace BlogEngine.Web.Hosting;

/// <summary>Visite di un giorno: pagine viste, visitatori e da dove sono arrivati.</summary>
public sealed class VisitDay
{
    /// <summary>Giorno nell'ora locale del blog.</summary>
    public DateOnly Date { get; set; }

    public int Views { get; set; }

    public int Visitors { get; set; }

    /// <summary>Visitatori da cellulare o tablet.</summary>
    public int Mobile { get; set; }

    /// <summary>Pagine viste per indirizzo (per esempio "/post/titolo").</summary>
    public Dictionary<string, int> Pages { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Visitatori per provenienza (chiavi di <see cref="VisitSources"/>).</summary>
    public Dictionary<string, int> Sources { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Visitatori arrivati da altri siti, per nome del sito.</summary>
    public Dictionary<string, int> Sites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Provenienza di un visitatore.</summary>
public static class VisitSources
{
    public const string Search = "search";
    public const string Social = "social";
    public const string Sites = "sites";
    public const string Direct = "direct";

    private static readonly string[] SearchEngines =
        ["google.", "bing.", "duckduckgo.", "yahoo.", "ecosia.", "yandex.", "baidu.", "qwant.", "startpage.", "search.brave.", "virgilio.", "libero."];

    private static readonly string[] SocialNetworks =
        ["facebook.", "fb.", "instagram.", "t.co", "twitter.", "x.com", "linkedin.", "lnkd.in", "reddit.", "pinterest.", "youtube.", "youtu.be",
         "tiktok.", "whatsapp.", "telegram.", "t.me", "threads.", "bsky.", "mastodon.", "tumblr."];

    /// <summary>Provenienza dalla pagina precedente (intestazione Referer); i link interni contano come accesso diretto.</summary>
    public static string Classify(Uri? referrer, string ownHost)
    {
        if (referrer is null || string.Equals(Host(referrer), Host(ownHost), StringComparison.OrdinalIgnoreCase))
        {
            return Direct;
        }

        var host = Host(referrer);
        if (Matches(host, SearchEngines))
        {
            return Search;
        }

        return Matches(host, SocialNetworks) ? Social : Sites;
    }

    /// <summary>Nome del sito senza "www." (per esempio "example.com").</summary>
    public static string Host(Uri uri) => Host(uri.Host);

    private static string Host(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..].ToLowerInvariant() : host.ToLowerInvariant();

    /// <summary>
    /// "google." vale per qualunque dominio di Google (google.it, news.google.com); "t.co" solo per t.co e i suoi
    /// sottodomini, non per t.corriere.it.
    /// </summary>
    private static bool Matches(string host, string[] names) =>
        names.Any(n => n.EndsWith('.')
            ? host.StartsWith(n, StringComparison.Ordinal) || host.Contains("." + n, StringComparison.Ordinal)
            : host == n || host.EndsWith("." + n, StringComparison.Ordinal));
}

/// <summary>
/// Statistiche delle visite di ogni blog, contate dal server senza cookie. I contatori stanno in memoria e
/// vengono salvati periodicamente in App_Data/stats/visits-{blog}.json (<see cref="VisitStatisticsWriter"/>).
/// I visitatori si riconoscono da un codice ricavato da indirizzo IP e browser con una chiave casuale che
/// cambia ogni giorno e non viene mai salvata: l'indirizzo IP non finisce da nessuna parte.
/// </summary>
public sealed class VisitStatistics(BlogEnginePaths paths, ILogger<VisitStatistics> logger)
{
    /// <summary>Giorni conservati; quelli più vecchi si cancellano al salvataggio.</summary>
    public const int RetentionDays = 400;

    /// <summary>Limite di pagine e siti diversi per giorno, contro gli indirizzi inventati dai robot.</summary>
    public const int MaxKeysPerDay = 500;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ConcurrentDictionary<Guid, BlogVisits> _blogs = new();

    public string FolderPath => Path.Combine(paths.DataRoot, "stats");

    public string FilePath(Guid blogId) => Path.Combine(FolderPath, $"visits-{blogId:N}.json");

    /// <summary>Conta una pagina vista.</summary>
    public void Record(Guid blogId, DateOnly day, string path, string visitorKey, bool isMobile, Uri? referrer, string ownHost)
    {
        var blog = Load(blogId);
        lock (blog)
        {
            var today = blog.Day(day);
            today.Views++;
            Increment(today.Pages, path);

            if (blog.IsNewVisitor(day, visitorKey))
            {
                today.Visitors++;
                if (isMobile)
                {
                    today.Mobile++;
                }

                var source = VisitSources.Classify(referrer, ownHost);
                Increment(today.Sources, source);
                if (source == VisitSources.Sites && referrer is not null)
                {
                    Increment(today.Sites, VisitSources.Host(referrer));
                }
            }

            blog.Dirty = true;
        }
    }

    /// <summary>Copia dei giorni dal <paramref name="from"/> al <paramref name="to"/> compresi.</summary>
    public IReadOnlyList<VisitDay> GetDays(Guid blogId, DateOnly from, DateOnly to)
    {
        var blog = Load(blogId);
        lock (blog)
        {
            return blog.Days.Values
                .Where(d => d.Date >= from && d.Date <= to)
                .OrderBy(d => d.Date)
                .Select(d => new VisitDay
                {
                    Date = d.Date,
                    Views = d.Views,
                    Visitors = d.Visitors,
                    Mobile = d.Mobile,
                    Pages = new(d.Pages, StringComparer.OrdinalIgnoreCase),
                    Sources = new(d.Sources, StringComparer.Ordinal),
                    Sites = new(d.Sites, StringComparer.OrdinalIgnoreCase),
                })
                .ToList();
        }
    }

    /// <summary>Salva su disco i blog con conteggi nuovi.</summary>
    public void Flush()
    {
        foreach (var (blogId, blog) in _blogs)
        {
            string? json = null;
            lock (blog)
            {
                if (!blog.Dirty)
                {
                    continue;
                }

                var oldest = blog.Days.Keys.DefaultIfEmpty().Max().AddDays(-RetentionDays);
                foreach (var day in blog.Days.Keys.Where(d => d < oldest).ToList())
                {
                    blog.Days.Remove(day);
                }

                json = JsonSerializer.Serialize(blog.Days.Values.OrderBy(d => d.Date), JsonOptions);
                blog.Dirty = false;
            }

            try
            {
                Directory.CreateDirectory(FolderPath);
                var file = FilePath(blogId);
                var temp = file + ".tmp";
                File.WriteAllText(temp, json, Encoding.UTF8);
                File.Move(temp, file, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (blog)
                {
                    blog.Dirty = true;
                }

                logger.LogWarning(ex, "Statistiche delle visite non salvate.");
            }
        }
    }

    private BlogVisits Load(Guid blogId) => _blogs.GetOrAdd(blogId, id =>
    {
        var blog = new BlogVisits();
        try
        {
            if (File.Exists(FilePath(id)))
            {
                foreach (var day in JsonSerializer.Deserialize<List<VisitDay>>(File.ReadAllText(FilePath(id)), JsonOptions) ?? [])
                {
                    day.Pages = new(day.Pages, StringComparer.OrdinalIgnoreCase);
                    day.Sources = new(day.Sources, StringComparer.Ordinal);
                    day.Sites = new(day.Sites, StringComparer.OrdinalIgnoreCase);
                    blog.Days[day.Date] = day;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Statistiche delle visite non lette: si riparte da zero.");
        }

        return blog;
    });

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        if (counts.TryGetValue(key, out var count))
        {
            counts[key] = count + 1;
        }
        else if (counts.Count < MaxKeysPerDay)
        {
            counts[key] = 1;
        }
    }

    private sealed class BlogVisits
    {
        private readonly HashSet<string> _visitors = new(StringComparer.Ordinal);
        private DateOnly _visitorsDay;
        private byte[] _salt = [];

        public Dictionary<DateOnly, VisitDay> Days { get; } = [];

        public bool Dirty { get; set; }

        public VisitDay Day(DateOnly date)
        {
            if (!Days.TryGetValue(date, out var day))
            {
                Days[date] = day = new VisitDay { Date = date };
            }

            return day;
        }

        /// <summary>Il codice del visitatore vale solo per il giorno: a mezzanotte chiave ed elenco ripartono da zero.</summary>
        public bool IsNewVisitor(DateOnly date, string visitorKey)
        {
            if (date != _visitorsDay)
            {
                _visitorsDay = date;
                _visitors.Clear();
                _salt = RandomNumberGenerator.GetBytes(32);
            }

            var hash = Convert.ToBase64String(HMACSHA256.HashData(_salt, Encoding.UTF8.GetBytes(visitorKey)), 0, 16);
            return _visitors.Add(hash);
        }
    }
}

/// <summary>Salva le statistiche delle visite ogni 5 minuti e alla chiusura del sito.</summary>
public sealed class VisitStatisticsWriter(VisitStatistics statistics) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                statistics.Flush();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        statistics.Flush();
    }
}

/// <summary>
/// Conta le pagine pubbliche viste dai visitatori (impostazione EnableVisitStatistics). Non conta: robot,
/// utenti collegati, file, pagine di errore, amministrazione e API, anteprime del browser.
/// </summary>
public sealed class VisitTrackingMiddleware(RequestDelegate next)
{
    private static readonly string[] SkippedPrefixes =
        ["/admin", "/api", "/account", "/setup", "/error", "/Custom", "/Content", "/Scripts", "/lib", "/image.axd", "/file.axd", "/media"];

    private static readonly string[] BotSigns =
        ["bot", "crawl", "spider", "slurp", "facebookexternalhit", "embedly", "preview", "curl", "wget", "python", "java/", "httpclient",
         "okhttp", "go-http", "headless", "lighthouse", "pagespeed", "monitor", "uptime", "scan", "feed", "fetch", "archive", "validator",
         "phantom", "selenium", "puppeteer", "playwright", "axios", "node-fetch", "libwww", "perl", "ruby", "php/"];

    public async Task InvokeAsync(HttpContext http, RequestBlogContext context, InstallationState installation)
    {
        await next(http);

        try
        {
            if (!ShouldCount(http, context, installation)
                || !(await http.RequestServices.GetRequiredService<ISettingsService>().GetAsync()).EnableVisitStatistics)
            {
                return;
            }

            var clock = http.RequestServices.GetRequiredService<IBlogClock>();
            var day = DateOnly.FromDateTime(await clock.ToBlogTimeAsync(clock.UtcNow));
            var userAgent = http.Request.Headers.UserAgent.ToString();
            Uri.TryCreate(http.Request.Headers.Referer.ToString(), UriKind.Absolute, out var referrer);
            http.RequestServices.GetRequiredService<VisitStatistics>().Record(
                context.Blog.Id,
                day,
                NormalizePath(http.Request.Path.Value),
                $"{http.Connection.RemoteIpAddress}|{userAgent}",
                IsMobile(userAgent),
                referrer is { Scheme: "http" or "https" } ? referrer : null,
                http.Request.Host.Host);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Le statistiche non devono mai impedire di vedere la pagina.
            http.RequestServices.GetRequiredService<ILogger<VisitTrackingMiddleware>>().LogWarning(ex, "Visita non registrata.");
        }
    }

    public static bool IsBot(string userAgent) =>
        string.IsNullOrWhiteSpace(userAgent) || !userAgent.StartsWith("Mozilla/", StringComparison.Ordinal)
        || BotSigns.Any(s => userAgent.Contains(s, StringComparison.OrdinalIgnoreCase));

    public static bool IsMobile(string userAgent) =>
        userAgent.Contains("Mobi", StringComparison.Ordinal) || userAgent.Contains("Android", StringComparison.Ordinal)
        || userAgent.Contains("iPad", StringComparison.Ordinal) || userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Indirizzo senza barra finale e senza parametri (maiuscole e minuscole contano come uguali).</summary>
    public static string NormalizePath(string? path)
    {
        var value = (path ?? "/").TrimEnd('/');
        if (value.Length == 0)
        {
            return "/";
        }

        return value.Length > 200 ? value[..200] : value;
    }

    private static bool ShouldCount(HttpContext http, RequestBlogContext context, InstallationState installation) =>
        installation.IsInstalled
        && context.HasBlog
        && HttpMethods.IsGet(http.Request.Method)
        && http.Response.StatusCode == StatusCodes.Status200OK
        && (http.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ?? false)
        && http.User.Identity?.IsAuthenticated != true
        && !Path.HasExtension(http.Request.Path.Value)
        && !SkippedPrefixes.Any(p => http.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
        && !IsPrefetch(http.Request)
        && !IsBot(http.Request.Headers.UserAgent.ToString());

    private static bool IsPrefetch(HttpRequest request) =>
        request.Headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
        || request.Headers["Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase);
}
