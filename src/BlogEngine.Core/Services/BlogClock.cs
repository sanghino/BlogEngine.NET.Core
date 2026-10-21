using BlogEngine.Services;

namespace BlogEngine.Core.Services;

public sealed class BlogClock(TimeProvider timeProvider, ISettingsService settings) : IBlogClock
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<DateTime> ToBlogTimeAsync(DateTime utc)
    {
        var zone = await GetTimeZoneAsync().ConfigureAwait(false);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
    }

    public async Task<DateTime> ToUtcAsync(DateTime blogTime)
    {
        var zone = await GetTimeZoneAsync().ConfigureAwait(false);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(blogTime, DateTimeKind.Unspecified), zone);
    }

    /// <summary>
    /// Fuso orario del blog. Accetta id Windows e IANA (.NET li converte su entrambe le piattaforme);
    /// un id sconosciuto ricade su UTC invece di far fallire la pagina.
    /// </summary>
    public static TimeZoneInfo FindTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    private async Task<TimeZoneInfo> GetTimeZoneAsync() =>
        FindTimeZone((await settings.GetAsync().ConfigureAwait(false)).TimeZoneId);
}
