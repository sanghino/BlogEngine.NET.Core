using BlogEngine.Core.Caching;
using BlogEngine.Models;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

public sealed class ReferrerService(
    EntityRepository<Referrer> referrers,
    IBlogContext context,
    ISettingsService settings,
    IBlogClock clock) : IReferrerService
{
    public async Task<IReadOnlyDictionary<DateTime, IReadOnlyList<Referrer>>> GetByDayAsync(CancellationToken cancellationToken = default)
    {
        var days = Math.Max(1, (await settings.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).NumberOfReferrerDays);
        var firstDay = clock.UtcNow.Date.AddDays(1 - days);
        return (await referrers.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false))
            .Where(r => r.Day >= firstDay)
            .GroupBy(r => r.Day.Date)
            .OrderByDescending(g => g.Key)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Referrer>)g.OrderByDescending(r => r.Count).ToList());
    }

    public async Task RegisterAsync(Uri url, Uri referrerUrl, bool possibleSpam, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(referrerUrl);

        var today = clock.UtcNow.Date;
        var all = await referrers.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false);
        var referrer = all.FirstOrDefault(r => r.Day == today && r.Url == url && r.ReferrerUrl == referrerUrl)
            ?? new Referrer { BlogId = context.Blog.Id, Day = today, Url = url, ReferrerUrl = referrerUrl, PossibleSpam = possibleSpam };

        referrer.Count++;
        await referrers.SaveAsync(referrer, cancellationToken).ConfigureAwait(false);
    }
}
