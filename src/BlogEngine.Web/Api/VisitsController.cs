using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>Riepilogo delle visite per il riquadro della dashboard.</summary>
[Route("api/visits")]
public sealed class VisitsController(
    VisitStatistics statistics,
    IRightsService rights,
    ISettingsService settings,
    IBlogContext context,
    IBlogClock clock,
    IPostService posts,
    IPageService pages) : AdminApiController
{
    public const int TopCount = 5;

    [HttpGet("")]
    public async Task<VisitsReport> Get(int days = 30)
    {
        if (!await rights.IsAuthorizedAsync(Rights.ViewDashboard))
        {
            throw new UnauthorizedAccessException();
        }

        days = Math.Clamp(days, 7, 365);
        var today = DateOnly.FromDateTime(await clock.ToBlogTimeAsync(clock.UtcNow));
        var first = today.AddDays(1 - days);
        var recorded = statistics.GetDays(context.Blog.Id, first, today).ToDictionary(d => d.Date);
        var all = Enumerable.Range(0, days).Select(i => first.AddDays(i))
            .Select(d => recorded.TryGetValue(d, out var day) ? day : new VisitDay { Date = d })
            .ToList();

        VisitTotals Totals(int lastDays)
        {
            var range = all.Where(d => d.Date > today.AddDays(-lastDays)).ToList();
            return new VisitTotals { Views = range.Sum(d => d.Views), Visitors = range.Sum(d => d.Visitors) };
        }

        static IEnumerable<KeyValuePair<string, int>> Sum(IEnumerable<Dictionary<string, int>> counts) =>
            counts.SelectMany(c => c)
                .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(c => c.Value)))
                .OrderByDescending(c => c.Value)
                .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase);

        var visitors = all.Sum(d => d.Visitors);
        var report = new VisitsReport
        {
            Enabled = (await settings.GetAsync()).EnableVisitStatistics,
            Days = all.Select(d => new VisitDayItem { Date = d.Date.ToString("yyyy-MM-dd"), Views = d.Views, Visitors = d.Visitors }).ToList(),
            Today = Totals(1),
            Week = Totals(7),
            Period = Totals(days),
            MobilePercent = visitors == 0 ? 0 : (int)Math.Round(100.0 * all.Sum(d => d.Mobile) / visitors),
            Sources = Sum(all.Select(d => d.Sources)).Select(s => new VisitCount { Name = s.Key, Count = s.Value }).ToList(),
            Sites = Sum(all.Select(d => d.Sites)).Take(TopCount).Select(s => new VisitCount { Name = s.Key, Count = s.Value }).ToList(),
        };

        foreach (var page in Sum(all.Select(d => d.Pages)).Take(TopCount))
        {
            report.Pages.Add(new VisitPage { Title = await TitleAsync(page.Key), Url = Request.PathBase + page.Key, Views = page.Value });
        }

        return report;
    }

    /// <summary>Titolo del post o della pagina; per gli altri indirizzi l'indirizzo stesso.</summary>
    private async Task<string> TitleAsync(string path)
    {
        var parts = path.Trim('/').Split('/');
        if (parts is ["post", var postSlug] && await posts.GetBySlugAsync(Uri.UnescapeDataString(postSlug)) is { } post)
        {
            return post.Title;
        }

        if (parts is ["page", var pageSlug] && await pages.GetBySlugAsync(Uri.UnescapeDataString(pageSlug)) is { } page)
        {
            return page.Title;
        }

        return path.ToLowerInvariant() switch
        {
            "/" => T("home"),
            "/archive" => T("archive"),
            "/contact" => T("contact"),
            _ => Uri.UnescapeDataString(path),
        };
    }
}

public sealed class VisitsReport
{
    public bool Enabled { get; set; }

    /// <summary>Un elemento per ogni giorno del periodo, dal più vecchio a oggi, anche senza visite.</summary>
    public List<VisitDayItem> Days { get; set; } = [];

    public VisitTotals Today { get; set; } = new();

    public VisitTotals Week { get; set; } = new();

    public VisitTotals Period { get; set; } = new();

    public int MobilePercent { get; set; }

    public List<VisitPage> Pages { get; set; } = [];

    /// <summary>Provenienza dei visitatori: search, social, sites, direct.</summary>
    public List<VisitCount> Sources { get; set; } = [];

    public List<VisitCount> Sites { get; set; } = [];
}

public sealed class VisitDayItem
{
    public string Date { get; set; } = string.Empty;

    public int Views { get; set; }

    public int Visitors { get; set; }
}

public sealed class VisitTotals
{
    public int Views { get; set; }

    public int Visitors { get; set; }
}

public sealed class VisitPage
{
    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public int Views { get; set; }
}

public sealed class VisitCount
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}
