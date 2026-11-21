using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlogEngine.Core.Services;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Gioco "Blocchi" ([gioco:blocchi]): si gioca liberamente, in classifica solo gli utenti registrati.</summary>
[TestClass]
public sealed class GameTests
{
    [TestMethod]
    public async Task BlockGame_AnyoneCanPlay_OnlyRegisteredUsersEnterTheLeaderboard()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        await factory.RunAsSystemAsync(services => services.GetRequiredService<IPageService>().SaveAsync(new Page
        {
            Title = "Giochi",
            IsPublished = true,
            Content = "<p>Prova a battere il record!</p><p>[gioco:blocchi]</p><p>[gioco:inesistente]</p>",
        }));

        // Il codice diventa il gioco, con lo script; un gioco che non esiste resta com'è.
        using var visitor = factory.CreateBrowser();
        var page = await visitor.GetStringAsync("/page/Giochi");
        StringAssert.Contains(page, "class=\"block-game\"");
        StringAssert.Contains(page, "data-api=\"/game/blocchi/\"");
        StringAssert.Contains(page, "Scripts/blockgame.js");
        StringAssert.Contains(page, "account/login?ReturnUrl=");
        Assert.IsFalse(page.Contains("[gioco:blocchi]", StringComparison.Ordinal));
        StringAssert.Contains(page, "[gioco:inesistente]");
        StringAssert.Contains(await visitor.GetStringAsync("/Scripts/blockgame.js"), "block-game");

        // Un visitatore gioca, ma per entrare in classifica deve accedere.
        var visitorToken = await StartAsync(visitor, page);
        var anonymous = await ScoreAsync(visitor, page, visitorToken, 300, 1);
        Assert.AreEqual("LOGIN", anonymous["status"]!.GetValue<string>());
        var board = JsonNode.Parse(await visitor.GetStringAsync("/game/blocchi/scores"))!;
        Assert.IsFalse(board["loggedIn"]!.GetValue<bool>());
        Assert.AreEqual(0, board["top"]!.AsArray().Count);

        // Un utente registrato entra in classifica con il suo miglior punteggio.
        var adminPage = await admin.GetStringAsync("/page/Giochi");
        var token = await StartAsync(admin, adminPage);
        var best = await ScoreAsync(admin, adminPage, token, 400, 1);
        Assert.AreEqual("BEST", best["status"]!.GetValue<string>(), best.ToJsonString());
        Assert.AreEqual(1, best["me"]!["rank"]!.GetValue<int>());
        Assert.AreEqual("SAVED", (await ScoreAsync(admin, adminPage, token, 50, 0))["status"]!.GetValue<string>(), "Sotto il record: registrato ma il record resta.");
        Assert.AreEqual("INVALID", (await ScoreAsync(admin, adminPage, token, 100000, 1))["status"]!.GetValue<string>(), "Impossibile in pochi secondi.");
        Assert.AreEqual("INVALID", (await ScoreAsync(admin, adminPage, "gettone-falso", 10, 0))["status"]!.GetValue<string>(), "Senza un inizio di partita valido.");

        board = JsonNode.Parse(await admin.GetStringAsync("/game/blocchi/scores"))!;
        Assert.IsTrue(board["loggedIn"]!.GetValue<bool>());
        Assert.AreEqual(400, board["me"]!["score"]!.GetValue<int>());
        var top = board["top"]!.AsArray();
        Assert.AreEqual(1, top.Count, "Una sola riga per utente.");
        Assert.AreEqual(400, top[0]!["score"]!.GetValue<int>());
        Assert.IsTrue(top[0]!["me"]!.GetValue<bool>());
        Assert.IsFalse(JsonNode.Parse(await visitor.GetStringAsync("/game/blocchi/scores"))!["top"]![0]!["me"]!.GetValue<bool>(), "Per il visitatore la riga non è sua.");

        // Amministrazione: elenco completo, rimozione di un giocatore, azzeramento.
        var games = JsonNode.Parse(await admin.GetStringAsync("/api/games"))!.AsArray();
        var entry = games.Single(g => g!["Game"]!.GetValue<string>() == "blocchi")!["Scores"]![0]!;
        Assert.AreEqual(BlogWebFactory.AdminUser, entry["UserName"]!.GetValue<string>());
        Assert.AreEqual(2, entry["Games"]!.GetValue<int>(), "Le partite valide (record e sotto record), non quelle scartate.");
        Assert.AreEqual(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/games/blocchi/{BlogWebFactory.AdminUser}")).StatusCode);
        Assert.AreEqual(0, JsonNode.Parse(await visitor.GetStringAsync("/game/blocchi/scores"))!["top"]!.AsArray().Count);

        await ScoreAsync(admin, adminPage, token, 200, 1);
        Assert.AreEqual(HttpStatusCode.OK, (await admin.DeleteAsync("/api/games/blocchi")).StatusCode);
        Assert.AreEqual(0, JsonNode.Parse(await visitor.GetStringAsync("/game/blocchi/scores"))!["top"]!.AsArray().Count);

        // L'amministrazione delle classifiche richiede il login; giochi sconosciuti non esistono.
        Assert.AreNotEqual(HttpStatusCode.OK, (await visitor.GetAsync("/api/games")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await visitor.GetAsync("/game/altro/scores")).StatusCode);
    }

    [TestMethod]
    public void MaxScore_AllowsRealGames_AndRejectsImpossibleOnes()
    {
        // Partita vera: 40 righe in 10 minuti, molte cadute veloci.
        Assert.IsTrue(GameScoreService.MaxScore(40, 600) > 40 * 1200 / 5 * 4);
        // Nessuna riga e pochi secondi: al massimo qualche caduta e una bomba.
        Assert.IsTrue(GameScoreService.MaxScore(0, 5) < 2000);
        Assert.AreEqual(240 * 10 + 480 * 2 + 160 * 30 + 500 * 11, GameScoreService.MaxScore(12, 30));
    }

    private static string Token(string page)
    {
        var token = Regex.Match(page, "\"antiforgery\":\"(?<token>[^\"]+)\"").Groups["token"].Value;
        Assert.IsFalse(string.IsNullOrEmpty(token), "Token antifalsificazione non trovato nella pagina.");
        return token;
    }

    private static async Task<string> StartAsync(HttpClient client, string page)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/game/blocchi/start") { Content = new FormUrlEncodedContent([]) };
        request.Headers.Add("RequestVerificationToken", Token(page));
        var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["token"]!.GetValue<string>();
    }

    private static async Task<JsonNode> ScoreAsync(HttpClient client, string page, string token, int score, int lines)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/game/blocchi/score")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token,
                ["score"] = score.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["lines"] = lines.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }),
        };
        request.Headers.Add("RequestVerificationToken", Token(page));
        var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}
