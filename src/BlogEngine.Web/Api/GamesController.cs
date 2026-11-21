using System.Globalization;
using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>Classifiche dei giochi (Contenuto → Giochi): elenco completo, rimozione di un utente, azzeramento.</summary>
[Route("api/games")]
public sealed class GamesAdminController(GameScoreService scores, IProfileService profiles, IRightsService rights, IBlogClock clock, ILabels labels) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<GameItem>> Get()
    {
        await DemandAsync();
        var games = new List<GameItem>();
        foreach (var game in GameScoreService.Games)
        {
            var entries = new List<GameScoreItem>();
            var rank = 0;
            foreach (var entry in await scores.GetAllAsync(game))
            {
                var profile = await profiles.GetAsync(entry.UserName);
                entries.Add(new GameScoreItem
                {
                    Rank = ++rank,
                    UserName = entry.UserName,
                    DisplayName = string.IsNullOrWhiteSpace(profile?.DisplayName) ? entry.UserName : profile!.DisplayName,
                    Score = entry.Score,
                    Lines = entry.Lines,
                    Level = entry.Level,
                    Seconds = entry.Seconds,
                    Games = entry.Games,
                    Date = await Local(entry.DateUtc),
                    LastPlayed = await Local(entry.LastPlayedUtc),
                });
            }

            games.Add(new GameItem
            {
                Game = game,
                Title = Themes.GameShortcodeHandler.Label(labels, "gameName", game),
                LinesLabel = Themes.GameShortcodeHandler.Label(labels, "gameLines", game),
                Code = $"[gioco:{game}]",
                Scores = entries,
            });
        }

        return games;
    }

    /// <summary>Toglie un utente dalla classifica.</summary>
    [HttpDelete("{game}/{userName}")]
    public async Task<IActionResult> Delete(string game, string userName)
    {
        await DemandAsync();
        return GameScoreService.IsGame(game) && await scores.DeleteAsync(game, userName) ? Ok() : NotFound();
    }

    /// <summary>Azzera la classifica del gioco.</summary>
    [HttpDelete("{game}")]
    public async Task<IActionResult> Reset(string game)
    {
        await DemandAsync();
        if (!GameScoreService.IsGame(game))
        {
            return NotFound();
        }

        await scores.ResetAsync(game);
        return Ok();
    }

    private async Task<string> Local(DateTime utc) =>
        (await clock.ToBlogTimeAsync(utc)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.ManageWidgets))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

public sealed record GameItem
{
    public string Game { get; init; } = string.Empty;

    /// <summary>Nome del gioco nella lingua del blog.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Che cosa conta la colonna Lines: righe (Blocchi) o ondate (Difesa spaziale).</summary>
    public string LinesLabel { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public IReadOnlyList<GameScoreItem> Scores { get; init; } = [];
}

public sealed record GameScoreItem
{
    public int Rank { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public int Score { get; init; }

    public int Lines { get; init; }

    public int Level { get; init; }

    public int Seconds { get; init; }

    public int Games { get; init; }

    public string Date { get; init; } = string.Empty;

    public string LastPlayed { get; init; } = string.Empty;
}
