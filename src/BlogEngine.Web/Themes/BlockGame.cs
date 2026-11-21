using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlogEngine.Core.Services;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Services;

namespace BlogEngine.Web.Themes;

/// <summary>
/// Codice [gioco:blocchi] nel testo di pagine e post: al suo posto compare il gioco "Blocchi" (blocchi che cadono,
/// Scripts/blockgame.js) con la classifica. Si gioca liberamente; in classifica entrano solo gli utenti registrati.
/// Nei feed e nelle email resta un collegamento alla pagina.
/// </summary>
public sealed partial class GameShortcodeHandler(ILinkGenerator links, ILabels labels, IHttpContextAccessor http, ISettingsService settings)
    : IEventHandler<ServingEvent<Post>>, IEventHandler<ServingEvent<Page>>
{
    /// <summary>Etichette usate dallo script del gioco.</summary>
    public static readonly string[] LabelKeys =
    [
        "gameName", "gameStart", "gamePlayAgain", "gamePause", "gameResume", "gameOver", "gameScore", "gameLines", "gameLevel",
        "gameNext", "gameHelp", "gameHelpTouch", "gameLeaderboard", "gameLeaderboardEmpty", "gameLoginToRank", "gameLogin",
        "gameRegister", "gameNewBest", "gameSaved", "gameYourBest", "gameYourRank", "gameInvalid", "gameError", "gameLeft",
        "gameRight", "gameRotate", "gameDown", "gameDrop", "gamePlayer",
    ];

    [GeneratedRegex(@"\[gioco:\s*(?<game>[a-z0-9-]+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex ShortCodeRegex();

    public async Task HandleAsync(ServingEvent<Post> blogEvent, CancellationToken cancellationToken) =>
        blogEvent.Body = await RenderAsync(blogEvent.Body, blogEvent.Location, await links.PostAsync(blogEvent.Item, cancellationToken));

    public async Task HandleAsync(ServingEvent<Page> blogEvent, CancellationToken cancellationToken) =>
        blogEvent.Body = await RenderAsync(blogEvent.Body, blogEvent.Location, await links.PageAsync(blogEvent.Item, cancellationToken));

    private async Task<string> RenderAsync(string body, ServingLocation location, string itemUrl)
    {
        if (string.IsNullOrEmpty(body) || body.IndexOf("[gioco:", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return body;
        }

        var registerEnabled = (await settings.GetAsync()).EnableSelfRegistration;
        var first = true;
        return ShortCodeRegex().Replace(body, match =>
        {
            var game = match.Groups["game"].Value.ToLowerInvariant();
            if (!GameScoreService.IsGame(game))
            {
                return match.Value;
            }

            if (location is ServingLocation.Feed or ServingLocation.Email)
            {
                return $"<a href=\"{WebUtility.HtmlEncode(itemUrl)}\">{WebUtility.HtmlEncode(labels["gameName"])}</a>";
            }

            // Nell'elenco dei post il gioco sarebbe scomodo: un link alla pagina dove giocare.
            if (location == ServingLocation.PostList)
            {
                return $"<p><a href=\"{WebUtility.HtmlEncode(itemUrl)}\">{WebUtility.HtmlEncode(labels["gameName"])}</a></p>";
            }

            var html = Markup(game, itemUrl, registerEnabled, first);
            first = false;
            return html;
        });
    }

    private string Markup(string game, string itemUrl, bool registerEnabled, bool withScript)
    {
        var root = links.BlogRoot;
        var appRoot = SiteRenderer.ApplicationRoot(http.HttpContext?.Request.PathBase.Value, root);
        var returnUrl = Uri.EscapeDataString(itemUrl);
        var texts = JsonSerializer.Serialize(LabelKeys.ToDictionary(k => k, k => labels[k]));
        string A(string value) => WebUtility.HtmlEncode(value);

        var html = $"<div class=\"block-game\" data-game=\"{A(game)}\" data-api=\"{A(root + "game/" + game + "/")}\" "
            + $"data-login=\"{A(root + "account/login?ReturnUrl=" + returnUrl)}\" "
            + $"data-register=\"{A(registerEnabled ? root + "account/register" : string.Empty)}\" "
            + $"data-labels=\"{A(texts)}\">"
            + $"<noscript>{A(labels["gameNoScript"])}</noscript></div>";
        return withScript ? html + $"<script src=\"{A(appRoot + "Scripts/blockgame.js")}\" defer></script>" : html;
    }
}
