using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlogEngine.Core.Services;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Sondaggi: gestione dall'amministrazione, widget, voto singolo e multiplo, doppio voto, scadenza, storico.</summary>
[TestClass]
public sealed partial class PollTests
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    [TestMethod]
    public async Task Administrator_CreatesEditsClosesAndDeletesPolls()
    {
        using var factory = new BlogWebFactory();
        using var admin = await Admin(factory);

        // Servono la domanda e almeno due risposte.
        var invalid = await admin.PostAsync("/api/polls", Json("""{ "Question": "Solo una?", "Options": [ { "Text": "Sì" } ] }"""));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        var wrongDates = await admin.PostAsync("/api/polls", Json("""{ "Question": "Date", "Options": [ { "Text": "A" }, { "Text": "B" } ], "Starts": "2030-01-10T10:00", "Ends": "2030-01-01T10:00" }"""));
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongDates.StatusCode);

        var created = await admin.PostAsync("/api/polls", Json("""
            { "Question": "Miglior portiere?", "Description": "Scegline uno", "Options": [ { "Text": "Toldo" }, { "Text": "Frey" }, { "Text": "" } ],
              "Ends": "2099-12-31T18:00", "ResultsVisibility": "AfterClose" }
            """));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var poll = JsonNode.Parse(await created.Content.ReadAsStringAsync())!;
        var id = poll["Id"]!.GetValue<string>();
        Assert.AreEqual(2, poll["Options"]!.AsArray().Count, "Le risposte vuote vengono scartate.");
        Assert.AreEqual("open", poll["Status"]!.GetValue<string>());
        Assert.AreEqual("2099-12-31T18:00", poll["Ends"]!.GetValue<string>(), "La scadenza resta nell'ora del blog.");
        Assert.AreEqual("AfterClose", poll["ResultsVisibility"]!.GetValue<string>());

        // Modifica: nuova risposta in fondo.
        var options = poll["Options"]!.AsArray();
        var edit = $$"""{ "Question": "Miglior portiere viola?", "Options": [ { "Id": "{{options[0]!["Id"]}}", "Text": "Toldo" }, { "Id": "{{options[1]!["Id"]}}", "Text": "Frey" }, { "Text": "Viviano" } ], "MultipleChoice": true, "MaxChoices": 2 }""";
        var updated = await admin.PutAsync($"/api/polls/{id}", Json(edit));
        Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode, await updated.Content.ReadAsStringAsync());
        poll = JsonNode.Parse(await admin.GetStringAsync($"/api/polls/{id}"))!;
        Assert.AreEqual("Miglior portiere viola?", poll["Question"]!.GetValue<string>());
        Assert.AreEqual(3, poll["Options"]!.AsArray().Count);
        Assert.IsTrue(poll["MultipleChoice"]!.GetValue<bool>());
        Assert.AreEqual(2, poll["MaxChoices"]!.GetValue<int>());

        // Chiusura e riapertura.
        Assert.AreEqual("closed", JsonNode.Parse(await (await admin.PutAsync($"/api/polls/{id}/close", Json("{}"))).Content.ReadAsStringAsync())!["Status"]!.GetValue<string>());
        Assert.AreEqual("open", JsonNode.Parse(await (await admin.PutAsync($"/api/polls/{id}/reopen", Json("{}"))).Content.ReadAsStringAsync())!["Status"]!.GetValue<string>());

        Assert.AreEqual(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/polls/{id}")).StatusCode);
        Assert.AreEqual(0, JsonNode.Parse(await admin.GetStringAsync("/api/polls"))!.AsArray().Count);

        // Senza il permesso sui widget non si accede.
        using var anonymous = factory.CreateBrowser();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/polls")).StatusCode);
    }

    [TestMethod]
    public async Task Visitor_VotesOnce_AndSeesTheResults()
    {
        using var factory = new BlogWebFactory();
        using var admin = await Admin(factory);
        var poll = await CreateAsync(factory, new Poll { Question = "Chi vince lo scudetto?", Options = [new() { Text = "Fiorentina" }, new() { Text = "Juventus" }] });
        await PlaceWidgetAsync(factory);

        using var visitor = Browser(factory, Chrome, "203.0.113.10");
        var home = await visitor.GetStringAsync("/");
        StringAssert.Contains(home, "Chi vince lo scudetto?");
        StringAssert.Contains(home, $"name=\"poll-{poll.Id:N}\"", "Il visitatore che non ha votato vede le risposte.");
        StringAssert.Contains(home, "type=\"radio\"", "Scelta singola: pulsanti di opzione.");

        // Nessuna risposta, o più risposte su una scelta singola: rifiutato con un messaggio.
        var none = await VoteAsync(visitor, home, poll);
        Assert.AreEqual(HttpStatusCode.BadRequest, none.StatusCode);
        StringAssert.Contains(await none.Content.ReadAsStringAsync(), "message");
        var both = await VoteAsync(visitor, home, poll, poll.Options[0].Id, poll.Options[1].Id);
        Assert.AreEqual(HttpStatusCode.BadRequest, both.StatusCode);

        var vote = await VoteAsync(visitor, home, poll, poll.Options[0].Id);
        Assert.AreEqual(HttpStatusCode.OK, vote.StatusCode, await vote.Content.ReadAsStringAsync());
        var result = JsonNode.Parse(await vote.Content.ReadAsStringAsync())!;
        Assert.AreEqual("OK", result["status"]!.GetValue<string>());
        Assert.IsTrue(result["poll"]!["ShowResults"]!.GetValue<bool>(), "Con \"dopo aver votato\" i risultati arrivano subito.");
        Assert.AreEqual(100, result["poll"]!["Options"]![0]!["Percent"]!.GetValue<int>());

        // Lo stesso browser non vota di nuovo, nemmeno ricaricando la pagina.
        var again = await VoteAsync(visitor, await visitor.GetStringAsync("/"), poll, poll.Options[1].Id);
        Assert.AreEqual("VOTED", JsonNode.Parse(await again.Content.ReadAsStringAsync())!["status"]!.GetValue<string>());

        // Stessa rete e stesso browser ma senza cookie (cookie cancellati): ancora bloccato.
        using var noCookies = Browser(factory, Chrome, "203.0.113.10");
        var page = await noCookies.GetStringAsync("/");
        Assert.IsFalse(page.Contains($"name=\"poll-{poll.Id:N}\""), "Riconosciuto dall'impronta di rete e browser: vede i risultati.");
        StringAssert.Contains(page, "poll-results");
        Assert.AreEqual("VOTED", JsonNode.Parse(await (await VoteAsync(noCookies, page, poll, poll.Options[1].Id)).Content.ReadAsStringAsync())!["status"]!.GetValue<string>());

        // Un altro visitatore (altro dispositivo) vota. Nel server di prova tutti hanno lo stesso indirizzo:
        // a distinguerli è il browser.
        using var other = Browser(factory, "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0", "198.51.100.20");
        var otherVote = await VoteAsync(other, await other.GetStringAsync("/"), poll, poll.Options[1].Id);
        Assert.AreEqual("OK", JsonNode.Parse(await otherVote.Content.ReadAsStringAsync())!["status"]!.GetValue<string>());

        var stored = await GetAsync(factory, poll.Id);
        Assert.AreEqual(2, stored.Votes.Count);
        Assert.IsFalse(string.Join(",", stored.Votes.SelectMany(v => v.Keys)).Contains("203.0.113.10", StringComparison.Ordinal), "Gli indirizzi IP non vengono salvati.");

        // Il riepilogo in amministrazione.
        var item = JsonNode.Parse(await admin.GetStringAsync($"/api/polls/{poll.Id}"))!;
        Assert.AreEqual(2, item["Voters"]!.GetValue<int>());
        Assert.AreEqual(50, item["Options"]![0]!["Percent"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task MultipleChoice_RespectsTheMaximum_AndRegisteredOnlyNeedsLogin()
    {
        using var factory = new BlogWebFactory();
        using var admin = await Admin(factory);
        var poll = await CreateAsync(factory, new Poll
        {
            Question = "Quali giocatori convochi?",
            MultipleChoice = true,
            MaxChoices = 2,
            RegisteredOnly = true,
            ResultsVisibility = PollResultsVisibility.AfterClose,
            Options = [new() { Text = "Kean" }, new() { Text = "Gudmundsson" }, new() { Text = "Dodo" }],
        });
        await PlaceWidgetAsync(factory);

        using var visitor = Browser(factory, Chrome, "203.0.113.30");
        var home = await visitor.GetStringAsync("/");
        Assert.IsFalse(home.Contains($"name=\"poll-{poll.Id:N}\""), "Un visitatore anonimo non può votare.");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await VoteAsync(visitor, home, poll, poll.Options[0].Id)).StatusCode);

        // L'amministratore è un utente collegato.
        admin.DefaultRequestHeaders.UserAgent.ParseAdd(Chrome);
        var adminHome = await admin.GetStringAsync("/");
        StringAssert.Contains(adminHome, "type=\"checkbox\"", "Scelta multipla: caselle.");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await VoteAsync(admin, adminHome, poll, poll.Options[0].Id, poll.Options[1].Id, poll.Options[2].Id)).StatusCode, "Oltre il massimo di risposte.");

        var vote = JsonNode.Parse(await (await VoteAsync(admin, adminHome, poll, poll.Options[0].Id, poll.Options[2].Id)).Content.ReadAsStringAsync())!;
        Assert.AreEqual("OK", vote["status"]!.GetValue<string>());
        Assert.IsFalse(vote["poll"]!["ShowResults"]!.GetValue<bool>(), "Risultati solo a sondaggio concluso.");
        Assert.AreEqual("VOTED", JsonNode.Parse(await (await VoteAsync(admin, adminHome, poll, poll.Options[1].Id)).Content.ReadAsStringAsync())!["status"]!.GetValue<string>());

        var stored = await GetAsync(factory, poll.Id);
        CollectionAssert.AreEquivalent(new[] { poll.Options[0].Id, poll.Options[2].Id }, stored.Votes.Single().Options);
        Assert.IsTrue(stored.Votes.Single().Keys.Any(k => k.StartsWith("user:", StringComparison.Ordinal)));
        Assert.IsFalse(stored.Votes.Single().Keys.Any(k => k.Contains(BlogWebFactory.AdminUser, StringComparison.OrdinalIgnoreCase)), "Il nome utente non è salvato in chiaro.");

        // Un solo votante con due risposte: 50% ciascuna, non 100% (le percentuali sono sulle scelte espresse).
        var item = JsonNode.Parse(await admin.GetStringAsync($"/api/polls/{poll.Id}"))!;
        Assert.AreEqual(1, item["Voters"]!.GetValue<int>());
        CollectionAssert.AreEqual(new[] { 50, 0, 50 }, item["Options"]!.AsArray().Select(o => o!["Percent"]!.GetValue<int>()).ToArray());
    }

    [TestMethod]
    public void Percentages_AddUpTo100()
    {
        CollectionAssert.AreEqual(new[] { 50, 50 }, Poll.Percentages([1, 1]));
        CollectionAssert.AreEqual(new[] { 34, 33, 33 }, Poll.Percentages([1, 1, 1]));
        CollectionAssert.AreEqual(new[] { 67, 33, 0 }, Poll.Percentages([2, 1, 0]));
        CollectionAssert.AreEqual(new[] { 0, 0 }, Poll.Percentages([0, 0]));
        CollectionAssert.AreEqual(new[] { 100 }, Poll.Percentages([7]));
    }

    [TestMethod]
    public async Task ExpiredPolls_StopVoting_AndStayInTheArchive()
    {
        using var factory = new BlogWebFactory();
        using var admin = await Admin(factory);
        var expired = await CreateAsync(factory, new Poll
        {
            Question = "Sondaggio scaduto",
            ResultsVisibility = PollResultsVisibility.AfterClose,
            Options = [new() { Text = "Sì" }, new() { Text = "No" }],
        });
        await factory.RunAsSystemAsync(async services =>
        {
            // Un voto mentre era aperto, poi la scadenza passa.
            var polls = services.GetRequiredService<PollService>();
            Assert.AreEqual(PollVoteResult.Accepted, await polls.VoteAsync(expired.Id, [expired.Options[1].Id], ["browser:prova"], false));
            var poll = (await polls.GetAsync(expired.Id))!;
            poll.EndsUtc = DateTime.UtcNow.AddMinutes(-1);
            Assert.IsNotNull((await polls.SaveAsync(poll)).Poll);
        });
        var scheduled = await CreateAsync(factory, new Poll { Question = "Non ancora iniziato", StartsUtc = DateTime.UtcNow.AddDays(3), Options = [new() { Text = "A" }, new() { Text = "B" }] });

        using var visitor = Browser(factory, Chrome, "203.0.113.40");
        var archive = await visitor.GetStringAsync("/polls");
        StringAssert.Contains(archive, "Sondaggio scaduto");
        StringAssert.Contains(archive, "poll-results", "A sondaggio concluso i risultati sono pubblici.");
        StringAssert.Contains(archive, "100% (1)");
        Assert.IsFalse(archive.Contains("Non ancora iniziato"), "I sondaggi programmati non si vedono.");

        var vote = await VoteAsync(visitor, archive, expired, expired.Options[0].Id);
        Assert.AreEqual("CLOSED", JsonNode.Parse(await vote.Content.ReadAsStringAsync())!["status"]!.GetValue<string>());
        Assert.AreEqual("closed", JsonNode.Parse(await admin.GetStringAsync($"/api/polls/{expired.Id}"))!["Status"]!.GetValue<string>());
        Assert.AreEqual("scheduled", JsonNode.Parse(await admin.GetStringAsync($"/api/polls/{scheduled.Id}"))!["Status"]!.GetValue<string>());

        // Senza sondaggi aperti il widget (con "il più recente aperto") non compare.
        await PlaceWidgetAsync(factory);
        Assert.IsFalse((await visitor.GetStringAsync("/")).Contains("poll-widget"));
    }

    [TestMethod]
    public async Task RemovingAnAnswerWithVotes_IsRefused()
    {
        using var factory = new BlogWebFactory();
        using var admin = await Admin(factory);
        var poll = await CreateAsync(factory, new Poll { Question = "Domanda", Options = [new() { Text = "A" }, new() { Text = "B" }, new() { Text = "C" }] });
        await factory.RunAsSystemAsync(async services =>
            Assert.AreEqual(PollVoteResult.Accepted, await services.GetRequiredService<PollService>().VoteAsync(poll.Id, [poll.Options[0].Id], ["browser:x"], false)));

        var without = $$"""{ "Question": "Domanda", "Options": [ { "Id": "{{poll.Options[1].Id}}", "Text": "B" }, { "Id": "{{poll.Options[2].Id}}", "Text": "C" } ] }""";
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/polls/{poll.Id}", Json(without))).StatusCode);

        var renamed = $$"""{ "Question": "Domanda", "Options": [ { "Id": "{{poll.Options[0].Id}}", "Text": "A (rinominata)" }, { "Id": "{{poll.Options[1].Id}}", "Text": "B" } ] }""";
        Assert.AreEqual(HttpStatusCode.OK, (await admin.PutAsync($"/api/polls/{poll.Id}", Json(renamed))).StatusCode, "Si può togliere una risposta senza voti e rinominare le altre.");
        var stored = await GetAsync(factory, poll.Id);
        Assert.AreEqual("A (rinominata)", stored.Options[0].Text);
        Assert.AreEqual(1, stored.Results()[0].Votes, "I voti restano legati alla risposta.");
    }

    [TestMethod]
    public async Task FeaturedPoll_ShowsABar_UntilTheVisitorVotes_AndHasItsOwnPage()
    {
        using var factory = new BlogWebFactory();
        using var admin = await Admin(factory);
        var poll = await CreateAsync(factory, new Poll { Question = "Qual è la console migliore?", Featured = true, Options = [new() { Text = "Commodore 64" }, new() { Text = "Amiga" }] });
        var twin = await CreateAsync(factory, new Poll { Question = "Qual è la console migliore?", Options = [new() { Text = "A" }, new() { Text = "B" }] });
        Assert.AreEqual("qual-e-la-console-migliore", poll.Slug, "L'indirizzo nasce dalla domanda.");
        Assert.AreEqual("qual-e-la-console-migliore-2", twin.Slug, "Domande uguali hanno indirizzi diversi.");

        await PlaceWidgetAsync(factory);
        using var visitor = Browser(factory, Chrome, "203.0.113.50");
        var home = await visitor.GetStringAsync("/");
        StringAssert.Contains(home, "poll-widget");
        StringAssert.Contains(home, "class=\"poll-banner\"", "La barra compare per chi non ha ancora votato.");
        Assert.IsTrue(home.IndexOf("</nav>", StringComparison.Ordinal) < home.IndexOf("poll-banner\"", StringComparison.Ordinal), "La barra viene subito dopo il menu.");
        StringAssert.Contains(home, "<span class=\"poll-banner-status\" hidden>", "Chi non ha votato non vede l'avviso.");
        StringAssert.Contains(home, "href=\"/poll/qual-e-la-console-migliore\"");
        Assert.IsTrue(home.IndexOf("poll-banner", StringComparison.Ordinal) < home.IndexOf("class=\"post", StringComparison.Ordinal), "La barra viene prima del primo post.");

        // La pagina del sondaggio: niente barra (c'è già il sondaggio), link a tutti i sondaggi.
        var page = await visitor.GetStringAsync("/poll/qual-e-la-console-migliore");
        StringAssert.Contains(page, "<title>");
        StringAssert.Contains(page, $"name=\"poll-{poll.Id:N}\"");
        StringAssert.Contains(page, "href=\"/polls\"");
        StringAssert.Contains(page, "class=\"poll-banner\"", "La barra c'è anche sulla pagina del sondaggio.");
        Assert.AreEqual(1, Regex.Matches(page, $"id=\"poll-{poll.Id:N}\"").Count, "Il widget non ripete il sondaggio della pagina (mostra l'altro aperto).");
        StringAssert.Contains(page, $"data-poll=\"{twin.Id:N}\"");
        Assert.AreEqual(HttpStatusCode.OK, (await visitor.GetAsync($"/poll/{poll.Id:N}")).StatusCode, "Funziona anche con l'identificativo.");
        Assert.AreEqual(HttpStatusCode.NotFound, (await visitor.GetAsync("/poll/non-esiste")).StatusCode);

        // Dopo il voto la barra resta, con l'avviso e il link ai risultati.
        Assert.AreEqual(HttpStatusCode.OK, (await VoteAsync(visitor, page, poll, poll.Options[0].Id)).StatusCode);
        var after = await visitor.GetStringAsync("/");
        StringAssert.Contains(after, "class=\"poll-banner poll-banner-voted\"");
        var status = Regex.Match(after, "<span class=\"poll-banner-status\">(?<text>.*?)</span>", RegexOptions.Singleline).Groups["text"].Value;
        StringAssert.Contains(status, "You have already voted");
        StringAssert.Contains(status, "href=\"/poll/qual-e-la-console-migliore\"");
        StringAssert.Contains(status, "See the results");

        // Chiuso il sondaggio, la barra sparisce per tutti; la pagina resta con i risultati.
        await factory.RunAsSystemAsync(services => services.GetRequiredService<PollService>().SetClosedAsync(poll.Id, true));
        using var other = Browser(factory, "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0", "198.51.100.60");
        Assert.IsFalse((await other.GetStringAsync("/")).Contains("class=\"poll-banner\""));
        StringAssert.Contains(await other.GetStringAsync("/poll/qual-e-la-console-migliore"), "poll-results");
    }

    [TestMethod]
    public void DoubleVote_IsRecognised_ByUserAndCookieForever_ByNetworkFor24Hours()
    {
        var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        var poll = new Poll
        {
            Votes =
            [
                new PollVote { DateUtc = now.AddHours(-2), Keys = ["user:mario", "browser:b1", "network:n1"] },
                new PollVote { DateUtc = now.AddDays(-3), Keys = ["browser:b2", "network:n2"] },
            ],
        };

        Assert.IsTrue(PollService.HasVoted(poll, ["user:mario", "browser:altro"], now), "Stesso utente da un altro browser.");
        Assert.IsTrue(PollService.HasVoted(poll, ["browser:b2", "network:nuova"], now), "Stesso cookie, anche dopo giorni.");
        Assert.IsTrue(PollService.HasVoted(poll, ["browser:nuovo", "network:n1"], now), "Cookie cancellati, stessa rete e browser entro 24 ore.");
        Assert.IsFalse(PollService.HasVoted(poll, ["browser:nuovo", "network:n2"], now), "Dopo 24 ore la sola impronta di rete non blocca più.");
        Assert.IsFalse(PollService.HasVoted(poll, ["user:luigi", "browser:nuovo", "network:n1"], now), "Un utente collegato diverso non è bloccato dalla rete condivisa.");
    }

    private static async Task<HttpClient> Admin(BlogWebFactory factory)
    {
        var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        return admin;
    }

    private static HttpClient Browser(BlogWebFactory factory, string userAgent, string forwardedFor)
    {
        var browser = factory.CreateBrowser();
        browser.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        browser.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return browser;
    }

    private static async Task<Poll> CreateAsync(BlogWebFactory factory, Poll poll)
    {
        Poll? saved = null;
        await factory.RunAsSystemAsync(async services => saved = (await services.GetRequiredService<PollService>().SaveAsync(poll)).Poll);
        Assert.IsNotNull(saved);
        return saved;
    }

    private static async Task<Poll> GetAsync(BlogWebFactory factory, Guid id)
    {
        Poll? poll = null;
        await factory.RunAsSystemAsync(async services => poll = await services.GetRequiredService<PollService>().GetAsync(id));
        return poll!;
    }

    private static Task PlaceWidgetAsync(BlogWebFactory factory) =>
        factory.RunAsSystemAsync(services =>
            services.GetRequiredService<WidgetService>().SaveZoneAsync("sidebar", [new WidgetPlacement { Name = "Poll", Title = "Sondaggio" }]));

    private static async Task<HttpResponseMessage> VoteAsync(HttpClient client, string page, Poll poll, params Guid[] options)
    {
        var token = Regex.Match(page, "\"antiforgery\":\"(?<token>[^\"]+)\"").Groups["token"].Value;
        Assert.IsFalse(string.IsNullOrEmpty(token), "Token antifalsificazione non trovato nella pagina.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/poll/vote")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = poll.Id.ToString("N"),
                ["options"] = string.Join(",", options.Select(o => o.ToString("N"))),
            }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
