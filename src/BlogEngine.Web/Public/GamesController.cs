using System.Globalization;
using BlogEngine.Core.Services;
using BlogEngine.Services;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogEngine.Web.Public;

/// <summary>
/// Giochi del blog (codice [gioco:nome]): inizio della partita, punteggio finale e classifica. Si gioca senza
/// login; il punteggio entra in classifica solo per gli utenti collegati. La durata della partita la misura il
/// server (gettone firmato dato all'inizio), così un punteggio impossibile per quel tempo viene scartato.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
public sealed class GamesController(
    GameScoreService scores,
    IProfileService profiles,
    IBlogContext context,
    IBlogClock clock,
    SiteRenderer site,
    IDataProtectionProvider protection) : Controller
{
    private ITimeLimitedDataProtector Protector =>
        protection.CreateProtector("BlogEngine.Games.Start").ToTimeLimitedDataProtector();

    /// <summary>Inizio di una partita: restituisce il gettone da rimandare con il punteggio.</summary>
    [HttpPost("/game/{game}/start")]
    public IActionResult Start(string game)
    {
        if (!GameScoreService.IsGame(game))
        {
            return NotFound();
        }

        var payload = $"{context.Blog.Id:N}|{game}|{scores.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)}";
        return Json(new { token = Protector.Protect(payload, GameScoreService.MaxDuration + TimeSpan.FromMinutes(10)) });
    }

    /// <summary>Punteggio di fine partita. Chi non è collegato riceve l'invito ad accedere.</summary>
    [HttpPost("/game/{game}/score")]
    [EnableRateLimiting(BlogActionsController.FormsRateLimit)]
    public async Task<IActionResult> Score(string game, [FromForm] string? token, [FromForm] int score, [FromForm] int lines)
    {
        if (!GameScoreService.IsGame(game))
        {
            return NotFound();
        }

        var user = context.UserName;
        if (string.IsNullOrEmpty(user))
        {
            return Json(new { status = "LOGIN", message = site.T("gameLoginToRank"), top = await TopAsync(game) });
        }

        if (StartOf(game, token) is not { } start)
        {
            return Json(new { status = "INVALID", message = site.T("gameInvalid"), top = await TopAsync(game) });
        }

        var seconds = (int)Math.Max(0, (scores.UtcNow - start).TotalSeconds);
        var result = await scores.SubmitAsync(game, user, score, lines, seconds);
        var me = await MeAsync(game, user);
        return Json(new
        {
            status = result switch { GameScoreResult.Best => "BEST", GameScoreResult.Saved => "SAVED", _ => "INVALID" },
            message = site.T(result switch { GameScoreResult.Best => "gameNewBest", GameScoreResult.Saved => "gameSaved", _ => "gameInvalid" }),
            me,
            top = await TopAsync(game),
        });
    }

    /// <summary>Classifica (i migliori) e, per chi è collegato, il suo record e la sua posizione.</summary>
    [HttpGet("/game/{game}/scores")]
    public async Task<IActionResult> Scores(string game)
    {
        if (!GameScoreService.IsGame(game))
        {
            return NotFound();
        }

        var user = context.UserName;
        return Json(new
        {
            loggedIn = !string.IsNullOrEmpty(user),
            me = string.IsNullOrEmpty(user) ? null : await MeAsync(game, user),
            top = await TopAsync(game),
        });
    }

    private DateTime? StartOf(string game, string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            var parts = Protector.Unprotect(token).Split('|');
            return parts.Length == 3 && parts[0] == context.Blog.Id.ToString("N") && parts[1] == game
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                ? new DateTime(ticks, DateTimeKind.Utc)
                : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Gettone falso, di un altro sito o scaduto.
            return null;
        }
    }

    private async Task<object?> MeAsync(string game, string user)
    {
        var all = await scores.GetAllAsync(game);
        var index = all.ToList().FindIndex(s => string.Equals(s.UserName, user, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : new { rank = index + 1, score = all[index].Score, games = all[index].Games };
    }

    private async Task<IReadOnlyList<object>> TopAsync(string game)
    {
        var list = new List<object>();
        var rank = 0;
        foreach (var entry in await scores.GetTopAsync(game))
        {
            var profile = await profiles.GetAsync(entry.UserName);
            var name = string.IsNullOrWhiteSpace(profile?.DisplayName) ? entry.UserName : profile!.DisplayName;
            list.Add(new
            {
                rank = ++rank,
                name,
                score = entry.Score,
                lines = entry.Lines,
                level = entry.Level,
                date = (await clock.ToBlogTimeAsync(entry.DateUtc)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                me = string.Equals(entry.UserName, context.UserName, StringComparison.OrdinalIgnoreCase),
            });
        }

        return list;
    }
}
