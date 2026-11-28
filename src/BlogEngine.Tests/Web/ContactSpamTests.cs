using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlogEngine.Core.Data;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Tests.Fakes;
using BlogEngine.Web.Public;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogEngine.Tests.Web;

/// <summary>
/// Filtro antispam del modulo di contatto: i messaggi sospetti non partono, restano nell'amministrazione
/// con il motivo del blocco e da lì si possono recapitare o eliminare.
/// </summary>
[TestClass]
public sealed partial class ContactSpamTests
{
    [TestMethod]
    public async Task SpamIsBlockedAndStored_AndCanBeReleasedFromTheAdmin()
    {
        var mail = new RecordingEmailSender();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var factory = CreateFactory(mail, clock);

        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogEngine.Web.Hosting.BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        await factory.RunAsSystemAsync(services => services.GetRequiredService<ICommentFilterStore>().SaveRulesAsync(
            services.GetRequiredService<IBlogContext>().Blog.Id,
            [new CommentFilterRule { Action = CommentFilterAction.Block, Subject = CommentFilterSubject.Email, Operator = CommentFilterOperator.Contains, Filter = "spammer.example" }]));

        using var visitor = factory.CreateBrowser();

        // Una persona: compila il modulo con calma e il messaggio parte.
        var sent = await SendAsync(visitor, clock, TimeSpan.FromSeconds(20), "Mario", "mario@example.com", "Domanda", "Ciao, una domanda sul blog: https://example.com/pagina");
        StringAssert.Contains(sent, "id=\"thanks\"");
        Assert.AreEqual(1, mail.Sent.Count);
        StringAssert.Contains(mail.Sent.Single().HtmlBody, "una domanda sul blog");

        // Spam: inviato in un secondo, campo trappola, troppi link, filtro dei commenti. Il visitatore vede sempre i ringraziamenti.
        StringAssert.Contains(await SendAsync(visitor, clock, TimeSpan.FromSeconds(1), "Bot", "bot@example.com", "Veloce", "testo"), "id=\"thanks\"");
        await SendAsync(visitor, clock, TimeSpan.FromSeconds(20), "Bot", "bot@example.com", "Trappola", "testo", honeypot: "riempito");
        await SendAsync(visitor, clock, TimeSpan.FromSeconds(20), "Offerte", "offerte@example.com", "Link", "http://a.example http://b.example www.c.example");
        await SendAsync(visitor, clock, TimeSpan.FromSeconds(20), "Spammer", "x@spammer.example", "Filtro", "compra ora");
        await SendAsync(visitor, clock, TimeSpan.FromSeconds(20), "Falso", "falso@example.com", "Gettone", "testo", openedToken: "gettone-alterato");
        Assert.AreEqual(1, mail.Sent.Count, "Nessuno dei messaggi sospetti è stato inviato.");

        var list = JsonNode.Parse(await admin.GetStringAsync("/api/contactmessages"))!.AsArray();
        Assert.AreEqual(5, list.Count);
        string By(string subject) => list.Single(m => m!["Subject"]!.GetValue<string>() == subject)!["BlockedBy"]!.GetValue<string>();
        Assert.AreEqual("Timer", By("Veloce"));
        Assert.AreEqual("Honeypot", By("Trappola"));
        Assert.AreEqual("Links", By("Link"));
        Assert.AreEqual("Filter", By("Filtro"));
        Assert.AreEqual("Timer", By("Gettone"));
        var links = list.Single(m => m!["Subject"]!.GetValue<string>() == "Link")!;
        StringAssert.Contains(links["Reason"]!.GetValue<string>(), "3");
        Assert.AreEqual("offerte@example.com", links["Email"]!.GetValue<string>());
        Assert.AreEqual("http://a.example http://b.example www.c.example", links["Text"]!.GetValue<string>());

        // Verifica manuale: un falso positivo viene recapitato.
        var id = links["Id"]!.GetValue<string>();
        var release = await admin.PostAsync($"/api/contactmessages/{id}/release", JsonContent());
        Assert.AreEqual(HttpStatusCode.OK, release.StatusCode, await release.Content.ReadAsStringAsync());
        Assert.AreEqual(2, mail.Sent.Count);
        Assert.AreEqual("offerte@example.com", mail.Sent.Last().ReplyTo);
        StringAssert.Contains(mail.Sent.Last().HtmlBody, "www.c.example");
        list = JsonNode.Parse(await admin.GetStringAsync("/api/contactmessages"))!.AsArray();
        var released = list.Single(m => m!["Id"]!.GetValue<string>() == id)!;
        Assert.IsTrue(released["Released"]!.GetValue<bool>());
        Assert.AreEqual(BlogWebFactory.AdminUser, released["ReleasedBy"]!.GetValue<string>());
        Assert.AreEqual(HttpStatusCode.OK, (await admin.PostAsync($"/api/contactmessages/{id}/release", JsonContent())).StatusCode);
        Assert.AreEqual(2, mail.Sent.Count, "Un messaggio già recapitato non riparte.");

        // Eliminazione di uno e poi di tutti i bloccati: il recapitato resta.
        var trap = list.Single(m => m!["Subject"]!.GetValue<string>() == "Trappola")!["Id"]!.GetValue<string>();
        Assert.AreEqual(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/contactmessages/{trap}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await admin.DeleteAsync("/api/contactmessages")).StatusCode);
        list = JsonNode.Parse(await admin.GetStringAsync("/api/contactmessages"))!.AsArray();
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(id, list[0]!["Id"]!.GetValue<string>());

        // Solo chi modera i commenti vede i messaggi bloccati.
        Assert.AreNotEqual(HttpStatusCode.OK, (await visitor.GetAsync("/api/contactmessages")).StatusCode);
    }

    [TestMethod]
    public async Task BlockedMessages_OlderThanTheRetention_AreDeleted()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var factory = CreateFactory(new RecordingEmailSender(), clock);

        using var admin = await factory.InstallAsync();
        using var visitor = factory.CreateBrowser();

        await SendAsync(visitor, clock, TimeSpan.Zero, "Bot", "bot@example.com", "Vecchio", "testo");
        clock.Now += BlogEngine.Web.Public.ContactMessageService.Retention + TimeSpan.FromDays(1);
        await SendAsync(visitor, clock, TimeSpan.Zero, "Bot", "bot@example.com", "Nuovo", "testo");

        // Si legge dall'archivio: dopo 91 giorni anche l'accesso dell'amministratore è scaduto.
        IReadOnlyList<ContactMessage> stored = [];
        await factory.RunAsSystemAsync(async services => stored = await services.GetRequiredService<IEntityStore<ContactMessage>>().GetAllAsync(services.GetRequiredService<IBlogContext>().Blog.Id));
        Assert.AreEqual("Nuovo", stored.Single().Subject);
    }

    [TestMethod]
    public async Task Rules_CanBeSwitchedOff_AndCustomRulesBlockOrAllow()
    {
        var mail = new RecordingEmailSender();
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var links = new FakeLinkReputation("phishing.example");
        using var factory = CreateFactory(mail, clock, links);
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogEngine.Web.Hosting.BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        using var visitor = factory.CreateBrowser();

        // Di serie tutti i controlli sono attivi e non ci sono regole personalizzate.
        var settings = JsonNode.Parse(await admin.GetStringAsync("/api/contactmessages/settings"))!.AsObject();
        foreach (var check in new[] { "Honeypot", "Timer", "LinkInName", "MaxLinksEnabled", "LinkReputation", "CommentFilters", "ExternalServices" })
        {
            Assert.IsTrue(settings[check]!.GetValue<bool>(), check);
        }

        Assert.AreEqual(0, settings["Rules"]!.AsArray().Count);

        // Qualità dei link: un dominio segnalato blocca il messaggio anche con un solo link.
        await SendAsync(visitor, clock, TimeSpan.FromSeconds(20), "Anna", "anna@example.com", "Banca", "Accedi qui: https://login.phishing.example/conto");
        Assert.AreEqual(0, mail.Sent.Count);
        CollectionAssert.Contains(links.Checked, "login.phishing.example");

        // Si spengono il tempo minimo e i troppi link; si aggiungono una lista nera e una lista bianca.
        settings["Timer"] = false;
        settings["MaxLinksEnabled"] = false;
        settings["Rules"] = new JsonArray(
            Rule(1, 0, "Spam.Example", 0),                    // dominio dell'email, sottodomini compresi
            Rule(2, 0, "198.51.100.0/24", 0),                 // rete IP
            Rule(5, 2, @"\bviagra\b", 0),                     // espressione regolare nel testo
            Rule(0, 1, "amica@example.com", 1),               // lista bianca
            Rule(5, 0, "parola spenta", 0, enabled: false));  // regola disattivata
        var saved = await admin.PutAsync("/api/contactmessages/settings", JsonBody(settings.ToJsonString()));
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());

        // Ora un invio rapido con tanti link passa: quei controlli sono spenti.
        await SendAsync(visitor, clock, TimeSpan.Zero, "Luca", "luca@example.com", "Link utili", "http://a.example http://b.example http://c.example");
        Assert.AreEqual(1, mail.Sent.Count);

        // Liste nere: dominio dell'email (anche un sottodominio), IP, parola; la regola spenta non interviene.
        await SendAsync(visitor, clock, TimeSpan.Zero, "X", "x@posta.spam.example", "Dominio", "testo");
        await SendAsync(visitor, clock, TimeSpan.Zero, "Y", "y@example.com", "Testo", "compra VIAGRA ora");
        await SendAsync(visitor, clock, TimeSpan.Zero, "Z", "z@example.com", "Spenta", "una parola spenta qui");
        Assert.AreEqual(2, mail.Sent.Count, "Solo il messaggio con la regola spenta è partito.");

        // Lista bianca: passa anche con un link segnalato.
        await SendAsync(visitor, clock, TimeSpan.Zero, "Amica", "amica@example.com", "Ciao", "guarda https://phishing.example");
        Assert.AreEqual(3, mail.Sent.Count);

        var list = JsonNode.Parse(await admin.GetStringAsync("/api/contactmessages"))!.AsArray();
        string Reason(string subject) => list.Single(m => m!["Subject"]!.GetValue<string>() == subject)!["Reason"]!.GetValue<string>();
        StringAssert.Contains(Reason("Banca"), "login.phishing.example");
        StringAssert.Contains(Reason("Dominio"), "Spam.Example");
        StringAssert.Contains(Reason("Testo"), "viagra");

        settings = JsonNode.Parse(await admin.GetStringAsync("/api/contactmessages/settings"))!.AsObject();
        var rules = settings["Rules"]!.AsArray();
        Assert.AreEqual(1, rules[0]!["Hits"]!.GetValue<int>(), "Le regole contano gli interventi.");
        Assert.AreEqual(1, rules[3]!["Hits"]!.GetValue<int>());
        Assert.AreEqual(0, rules[4]!["Hits"]!.GetValue<int>());

        // Dal messaggio bloccato: lista nera dell'IP del mittente (una sola volta).
        var banca = list.Single(m => m!["Subject"]!.GetValue<string>() == "Banca")!["Id"]!.GetValue<string>();
        var added = JsonNode.Parse(await (await admin.PostAsync($"/api/contactmessages/{banca}/blacklist/EmailDomain", JsonBody("{}"))).Content.ReadAsStringAsync())!;
        Assert.IsTrue(added["added"]!.GetValue<bool>());
        Assert.AreEqual("example.com", added["value"]!.GetValue<string>());
        added = JsonNode.Parse(await (await admin.PostAsync($"/api/contactmessages/{banca}/blacklist/EmailDomain", JsonBody("{}"))).Content.ReadAsStringAsync())!;
        Assert.IsFalse(added["added"]!.GetValue<bool>());

        // Regole non valide rifiutate; prova delle liste dei link.
        settings["Rules"]!.AsArray().Add(Rule(5, 2, "([", 0));
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/contactmessages/settings", JsonBody(settings.ToJsonString()))).StatusCode);
        StringAssert.Contains(await admin.GetStringAsync("/api/contactmessages/linktest"), "listed");
    }

    [TestMethod]
    public void RuleMatching_CoversDomainsIpsAndLinks()
    {
        var message = new ContactMessage { Email = "Mario@Posta.Spam.com", IP = "203.0.113.45", Name = "Mario", Text = "vai su https://www.casino.example/x e www.altro.example" };
        var hosts = DnsLinkReputationService.LinkHosts(message.Text);
        CollectionAssert.AreEquivalent(new[] { "www.casino.example", "altro.example" }, hosts.ToArray());

        static ContactFilterRule R(ContactRuleField field, string value, ContactRuleOperator op = ContactRuleOperator.Contains) => new() { Field = field, Value = value, Operator = op };
        Assert.IsTrue(R(ContactRuleField.EmailDomain, "spam.com").Matches(message, hosts));
        Assert.IsTrue(R(ContactRuleField.EmailDomain, "@posta.spam.com").Matches(message, hosts));
        Assert.IsFalse(R(ContactRuleField.EmailDomain, "pam.com").Matches(message, hosts), "Un dominio diverso con la stessa fine non vale.");
        Assert.IsTrue(R(ContactRuleField.IP, "203.0.113.45").Matches(message, hosts));
        Assert.IsTrue(R(ContactRuleField.IP, "203.0.113.").Matches(message, hosts));
        Assert.IsTrue(R(ContactRuleField.IP, "203.0.*").Matches(message, hosts));
        Assert.IsTrue(R(ContactRuleField.IP, "203.0.113.0/24").Matches(message, hosts));
        Assert.IsFalse(R(ContactRuleField.IP, "203.0.114.0/24").Matches(message, hosts));
        Assert.IsFalse(R(ContactRuleField.IP, "203.0.113.4").Matches(message, hosts), "L'IP esatto non vale per i suoi \"prefissi\" numerici.");
        Assert.IsTrue(R(ContactRuleField.LinkDomain, "casino.example").Matches(message, hosts));
        Assert.IsTrue(R(ContactRuleField.Email, "mario@posta.spam.com", ContactRuleOperator.Equals).Matches(message, hosts));
        Assert.IsTrue(R(ContactRuleField.Text, @"casino\.\w+", ContactRuleOperator.Regex).Matches(message, hosts));
        Assert.IsNotNull(R(ContactRuleField.Text, "([", ContactRuleOperator.Regex).Validate());
        Assert.IsNotNull(R(ContactRuleField.IP, "300.1.1.1/99").Validate());
        Assert.AreEqual("esempio.co.uk", DnsLinkReputationService.RegisteredDomain("posta.esempio.co.uk"));
        Assert.AreEqual("spam.com", DnsLinkReputationService.RegisteredDomain("www.spam.com"));
    }

    private static JsonObject Rule(int field, int op, string value, int action, bool enabled = true) => new()
    {
        ["Id"] = Guid.NewGuid().ToString(),
        ["Enabled"] = enabled,
        ["Field"] = field,
        ["Operator"] = op,
        ["Value"] = value,
        ["Action"] = action,
        ["Note"] = string.Empty,
    };

    private static StringContent JsonBody(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    private static BlogWebFactory CreateFactory(RecordingEmailSender mail, ManualTimeProvider clock, FakeLinkReputation? links = null) => new()
    {
        TestServices = services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(mail);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);

            // Niente DNS veri nei test: le liste dei domini segnalati sono simulate.
            services.RemoveAll<ILinkReputationService>();
            services.AddSingleton<ILinkReputationService>(links ?? new FakeLinkReputation());
        },
    };

    /// <summary>Segnala come phishing i domini dell'elenco.</summary>
    private sealed class FakeLinkReputation(params string[] listed) : ILinkReputationService
    {
        public List<string> Checked { get; } = [];

        public Task<LinkReputationHit?> CheckAsync(IReadOnlyCollection<string> hosts, CancellationToken cancellationToken = default)
        {
            Checked.AddRange(hosts);
            var bad = hosts.FirstOrDefault(h => listed.Any(d => ContactFilterRule.IsDomainOf(h, d)));
            return Task.FromResult(bad is null ? null : new LinkReputationHit(bad, "Lista di prova", "phishing"));
        }

        public Task<IReadOnlyList<LinkReputationStatus>> TestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LinkReputationStatus>>([new("Lista di prova", "prova.example", "listed")]);
    }

    private static async Task<string> SendAsync(HttpClient client, ManualTimeProvider clock, TimeSpan wait, string name, string email, string subject, string text,
        string honeypot = "", string? openedToken = null)
    {
        var page = await client.GetStringAsync("/contact");
        clock.Now += wait;
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(Field(page, "__RequestVerificationToken")),
            ["opened"] = openedToken ?? WebUtility.HtmlDecode(Field(page, "opened")),
            ["name"] = name,
            ["email"] = email,
            ["subject"] = subject,
            ["message"] = text,
            ["hp"] = honeypot,
        };
        var response = await client.PostAsync("/contact", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Field(string page, string name)
    {
        var match = Regex.Match(page, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
        Assert.IsTrue(match.Success && match.Groups[1].Value.Length > 0, $"Campo {name} non trovato nel modulo.");
        return match.Groups[1].Value;
    }

    private static StringContent JsonContent() => new("{}", System.Text.Encoding.UTF8, "application/json");

    private sealed class RecordingEmailSender : IEmailSender
    {
        public ConcurrentQueue<EmailMessage> Queue { get; } = new();

        public IReadOnlyList<EmailMessage> Sent => Queue.ToList();

        public Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Queue.Enqueue(message);
            return Task.FromResult<string?>(null);
        }
    }
}
