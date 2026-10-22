using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlogEngine.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>
/// API dell'amministrazione AngularJS sul sito reale: permessi, formato JSON (identico alla Web API originale)
/// e flussi completi.
/// </summary>
[TestClass]
public sealed partial class AdminApiTests
{
    private BlogWebFactory _factory = null!;
    private HttpClient _admin = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _factory = new BlogWebFactory();
        _admin = await _factory.InstallAsync(blogName: "Blog di prova");
        _admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _admin.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task AdminPages_RequireLoginAndRenderAngularShell()
    {
        using var anonymous = _factory.CreateBrowser(followRedirects: false);
        var redirect = await anonymous.GetAsync("/admin/");
        Assert.AreEqual(HttpStatusCode.Redirect, redirect.StatusCode);
        StringAssert.Contains(redirect.Headers.Location!.ToString(), "/account/login?returnUrl=%2Fadmin%2F");

        var noSlash = await anonymous.GetAsync("/admin");
        Assert.AreEqual("/admin/", noSlash.Headers.Location?.OriginalString, "Gli indirizzi relativi dell'app richiedono la barra finale.");

        var shell = await _admin.GetStringAsync("/admin/");
        StringAssert.Contains(shell, "ng-view");
        StringAssert.Contains(shell, "\"IsAdmin\":\"True\"");
        StringAssert.Contains(shell, "admin/app/content/posts/postController.js");
        StringAssert.Contains(shell, "admin/#/settings/advanced", "Impostazioni avanzate (con import/export BlogML) visibili anche con un solo blog.");
        foreach (var section in new[] { "admin/#/security/users", "admin/#/security/roles", "admin/#/content/blogs" })
        {
            StringAssert.Contains(shell, section, "Utenti, ruoli e blog sono visibili all'amministratore senza configurazioni particolari.");
        }

        var editor = await _admin.GetStringAsync("/admin/app/editor/editpost.cshtml");
        StringAssert.Contains(editor, "PostEditorController");
        StringAssert.Contains(editor, "tinymce.min.js");

        StringAssert.Contains(await _admin.GetStringAsync("/admin/app/editor/editpage.cshtml"), "PageEditorController");
        StringAssert.Contains(await _admin.GetStringAsync("/admin/app/editor/filemanager.cshtml"), "FileManagerController");
        StringAssert.Contains(await _admin.GetStringAsync("/admin/about.cshtml"), "Xml");
    }

    [TestMethod]
    public async Task AdminResources_ProvideSiteVarsAndTranslatedLabels()
    {
        _admin.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("it-IT"));
        var script = await _admin.GetStringAsync("/admin/admin.res.axd");

        StringAssert.Contains(script, "SiteVars = {");
        StringAssert.Contains(script, "\"RelativeWebRoot\":\"/\"");
        StringAssert.Contains(script, "\"IsPrimary\":\"True\"");
        StringAssert.Contains(script, "BlogAdmin = { i18n: {");
        StringAssert.Contains(script, "\"dashboard\":");
        StringAssert.Contains(script, "\"login\":\"Accedi\"", "Le etichette seguono la lingua del browser.");
    }

    [TestMethod]
    public async Task Api_RejectsChangesWithoutAdminHeaderAndAnonymousCalls()
    {
        using var noHeader = _factory.CreateBrowser();
        await noHeader.PostFormAsync("/account/login", new() { ["UserName"] = BlogWebFactory.AdminUser, ["Password"] = BlogWebFactory.AdminPassword });
        var forged = await noHeader.PutAsync("/api/settings", Json("{}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, forged.StatusCode, "Protezione CSRF: senza x-blog-instance la modifica è rifiutata.");

        using var anonymous = _factory.CreateBrowser();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/settings")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users")).StatusCode);
    }

    [TestMethod]
    public async Task Posts_JsonMatchesOriginalFormatAndFullLifecycleWorks()
    {
        var list = await GetJsonAsync<JsonArray>("/api/posts?take=0&skip=0");
        var welcome = list.Single()!.AsObject();
        Assert.IsTrue(welcome.ContainsKey("Title") && welcome.ContainsKey("IsPublished") && welcome.ContainsKey("RelativeLink"), "Proprietà in PascalCase.");
        Assert.IsTrue(DateRegex().IsMatch(welcome["DateCreated"]!.GetValue<string>()), "Date nel formato yyyy-MM-dd HH:mm.");
        StringAssert.StartsWith(welcome["RelativeLink"]!.GetValue<string>(), "/post/");

        var created = await SendJsonAsync<JsonObject>(HttpMethod.Post, "/api/posts", """
            { "Title": "Dal nuovo admin", "Content": "<p>Ciao</p>", "IsPublished": true, "HasCommentsEnabled": true,
              "DateCreated": "2025-03-09 10:30", "Categories": [ { "Title": "Novità" } ], "Tags": [ { "TagName": "net8" }, { "TagName": "net8" } ] }
            """, HttpStatusCode.Created);
        var id = created["Id"]!.GetValue<string>();
        Assert.AreEqual("Dal-nuovo-admin", created["Slug"]!.GetValue<string>());
        Assert.AreEqual("2025-03-09 10:30", created["DateCreated"]!.GetValue<string>(), "La data inserita resta quella mostrata (ora del blog).");
        Assert.AreEqual(1, created["Tags"]!.AsArray().Count, "Tag duplicati eliminati.");
        Assert.AreEqual("Novità", created["Categories"]![0]!["Title"]!.GetValue<string>(), "Categoria creata al volo.");

        created["Title"] = "Titolo modificato";
        await SendAsync(HttpMethod.Put, "/api/posts/update/foo", created.ToJsonString());
        Assert.AreEqual("Titolo modificato", (await GetJsonAsync<JsonObject>($"/api/posts/{id}"))["Title"]!.GetValue<string>());

        await SendAsync(HttpMethod.Put, "/api/posts/processchecked/unpublish", $$"""[ { "Id": "{{id}}", "IsChecked": true } ]""");
        Assert.IsFalse((await GetJsonAsync<JsonObject>($"/api/posts/{id}"))["IsPublished"]!.GetValue<bool>());

        await SendAsync(HttpMethod.Delete, $"/api/posts/{id}");
        var trash = await GetJsonAsync<JsonObject>("/api/trash?type=2&take=0");
        Assert.AreEqual(id, trash["Items"]![0]!["Id"]!.GetValue<string>());

        await SendAsync(HttpMethod.Put, "/api/trash/restore/item", $$"""{ "Id": "{{id}}", "ObjectType": "Post" }""");
        Assert.AreEqual(2, (await GetJsonAsync<JsonArray>("/api/posts?take=0")).Count);

        await SendAsync(HttpMethod.Delete, $"/api/posts/{id}");
        await SendAsync(HttpMethod.Put, "/api/trash/purgeall/all", "{}");
        Assert.AreEqual(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/posts/{id}")).StatusCode);
    }

    /// <summary>
    /// Dati esattamente come li invia l'editor AngularJS per un post o una pagina nuovi: Id vuoto e
    /// alcuni elenchi inizializzati come stringa vuota (la vecchia Web API li accettava).
    /// </summary>
    [TestMethod]
    public async Task Editor_NewPostAndPage_WithEmptyStringsAreAccepted()
    {
        var post = await SendJsonAsync<JsonObject>(HttpMethod.Post, "/api/posts", """
            { "Id": "", "Title": "Primo post", "Author": "admin", "Content": "<p>Testo</p>", "DateCreated": "2026-10-21 18:00",
              "Slug": "", "Categories": [], "Tags": [], "Comments": "", "HasCommentsEnabled": true, "IsPublished": true }
            """, HttpStatusCode.Created);
        Assert.IsTrue(Guid.TryParse(post["Id"]!.GetValue<string>(), out var id) && id != Guid.Empty);

        var noLists = await _admin.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/posts")
        {
            Content = Json("""{ "Id": "", "Title": "Secondo", "Author": "admin", "Content": "x", "DateCreated": "2026-10-21 18:00", "Slug": "", "Categories": "", "Tags": "", "Comments": "", "IsPublished": false }"""),
        });
        Assert.AreEqual(HttpStatusCode.Created, noLists.StatusCode, await noLists.Content.ReadAsStringAsync());

        var page = await _admin.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/pages")
        {
            Content = Json("""{ "Id": "", "Title": "Chi sono", "Content": "<p>Io</p>", "DateCreated": "2026-10-21 18:00", "Slug": "", "Parent": null, "IsPublished": true, "ShowInList": true }"""),
        });
        Assert.AreEqual(HttpStatusCode.Created, page.StatusCode, await page.Content.ReadAsStringAsync());
    }

    /// <summary>TinyMCE salva le lettere accentate come entità (&amp;egrave;): nei testi semplici devono tornare caratteri.</summary>
    [TestMethod]
    public async Task AccentedLettersSavedAsEntities_AreShownAsCharacters()
    {
        var post = await SendJsonAsync<JsonObject>(HttpMethod.Post, "/api/posts", """
            { "Id": "", "Title": "Accenti", "Author": "admin", "Content": "<p>Perch&egrave; s&igrave;, &egrave; vero</p>", "DateCreated": "2026-10-21 18:00",
              "Slug": "", "Categories": [], "Tags": [], "IsPublished": true }
            """, HttpStatusCode.Created);
        Assert.AreEqual("Perchè sì, è vero", post["Description"]!.GetValue<string>(), "Descrizione automatica in testo semplice.");

        var home = await _admin.GetStringAsync("/");
        StringAssert.Contains(home, "Perchè sì, è vero", "Il testo è codificato una sola volta e gli accenti restano leggibili.");
        Assert.IsFalse(home.Contains("&amp;egrave;"), "Niente entità codificate due volte.");

        var single = await _admin.GetStringAsync("/post/Accenti");
        Assert.IsFalse(single.Contains("&amp;egrave;"));
        StringAssert.Contains(single, "\"description\": \"Perch\\u00E8 s\\u00EC");

        StringAssert.Contains(await _admin.GetStringAsync("/search?q=perch%C3%A8"), "Accenti", "La ricerca trova la parola con l'accento.");
    }

    [TestMethod]
    public async Task Validation_ErrorsBecomeBadRequestWithMessage()
    {
        var response = await _admin.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/posts") { Content = Json("""{ "Title": "", "Content": "" }""") });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "The title is required.");
    }

    [TestMethod]
    public async Task Comments_ReplyModerateAndDeleteAll()
    {
        var postId = (await GetJsonAsync<JsonArray>("/api/posts?take=0"))[0]!["Id"]!.GetValue<string>();
        var reply = await SendJsonAsync<JsonObject>(HttpMethod.Post, "/api/comments", $$"""{ "PostId": "{{postId}}", "Content": "Risposta dell'admin" }""", HttpStatusCode.Created);
        var commentId = reply["Id"]!.GetValue<string>();
        Assert.IsTrue(reply["IsApproved"]!.GetValue<bool>());
        Assert.AreEqual(BlogWebFactory.AdminUser, reply["Author"]!.GetValue<string>());

        var vm = await GetJsonAsync<JsonObject>("/api/comments");
        Assert.AreEqual(1, vm["Items"]!.AsArray().Count);
        Assert.IsTrue(vm.ContainsKey("SelectedItem") && vm.ContainsKey("Detail"));

        await SendAsync(HttpMethod.Put, "/api/comments/processchecked/unapprove", $$"""[ { "Id": "{{commentId}}", "IsChecked": true } ]""");
        Assert.IsTrue((await GetJsonAsync<JsonObject>("/api/comments"))["Items"]![0]!["IsSpam"]!.GetValue<bool>());

        await SendAsync(HttpMethod.Put, "/api/comments/deleteall/spam", "{}");
        Assert.AreEqual(0, (await GetJsonAsync<JsonObject>("/api/comments"))["Items"]!.AsArray().Count);
        Assert.AreEqual(1, (await GetJsonAsync<JsonObject>("/api/trash?type=1&take=0"))["TotalCount"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task SettingsLookupsAndDashboard()
    {
        var settings = await GetJsonAsync<JsonObject>("/api/settings");
        Assert.AreEqual("Blog di prova", settings["Settings"]!["Name"]!.GetValue<string>());
        Assert.IsTrue(settings["FacebookLanguages"]!.AsArray().Count > 100);

        var updated = settings["Settings"]!.AsObject();
        updated["Name"] = "Nome aggiornato";
        updated["PostsPerPage"] = "7";
        await SendAsync(HttpMethod.Put, "/api/settings", updated.ToJsonString());
        var reloaded = (await GetJsonAsync<JsonObject>("/api/settings"))["Settings"]!;
        Assert.AreEqual("Nome aggiornato", reloaded["Name"]!.GetValue<string>());
        Assert.AreEqual(7, reloaded["PostsPerPage"]!.GetValue<int>(), "I numeri inviati come stringa sono accettati, come con la Web API originale.");

        var lookups = await GetJsonAsync<JsonObject>("/api/lookups");
        Assert.AreEqual("Auto", lookups["Cultures"]![0]!["OptionValue"]!.GetValue<string>());
        Assert.AreEqual(BlogWebFactory.AdminUser, lookups["AuthorList"]![0]!["OptionValue"]!.GetValue<string>());
        Assert.AreEqual("Post", lookups["PostOptions"]!["OptionType"]!.GetValue<string>());

        var dashboard = await GetJsonAsync<JsonObject>("/api/dashboard");
        Assert.AreEqual(1, dashboard["PostPublishedCnt"]!.GetValue<int>());
        Assert.AreEqual(1, (await GetJsonAsync<JsonObject>("/api/stats"))["PublishedPostsCount"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task UsersAndRoles()
    {
        await SendAsync(HttpMethod.Post, "/api/users", """
            { "UserName": "mario", "Email": "mario@example.com", "Password": "Password-Mario-1",
              "Roles": [ { "RoleName": "Editors", "IsChecked": true } ], "Profile": { "DisplayName": "Mario R." } }
            """, HttpStatusCode.Created);

        var mario = await GetJsonAsync<JsonObject>("/api/users?id=mario");
        Assert.AreEqual("Mario R.", mario["Profile"]!["DisplayName"]!.GetValue<string>());
        Assert.AreEqual("Editors", mario["Roles"]![0]!["RoleName"]!.GetValue<string>());

        var rights = await GetJsonAsync<JsonArray>("/api/roles/getrights/Editors");
        var posts = rights.First(g => g!["Title"]!.GetValue<string>() == "Posts")!["Permissions"]!.AsArray();
        Assert.IsTrue(posts.Any(p => p!["Id"]!.GetValue<string>() == "PublishOwnPosts" && p["IsChecked"]!.GetValue<bool>()));
        Assert.AreEqual("Publish Own Posts", posts.First(p => p!["Id"]!.GetValue<string>() == "PublishOwnPosts")!["Title"]!.GetValue<string>());

        await SendAsync(HttpMethod.Post, "/api/roles", """{ "RoleName": "Writers" }""", HttpStatusCode.Created);
        await SendAsync(HttpMethod.Put, "/api/roles/saverights/Writers", """[ { "Title": "Posts", "Permissions": [ { "Id": "CreateNewPosts", "IsChecked": true } ] } ]""");
        var writers = await GetJsonAsync<JsonArray>("/api/roles/getrights/Writers");
        Assert.AreEqual(1, writers.SelectMany(g => g!["Permissions"]!.AsArray()).Count(p => p!["IsChecked"]!.GetValue<bool>()));

        await SendAsync(HttpMethod.Delete, "/api/users/mario");
        Assert.AreEqual(HttpStatusCode.NotFound, (await _admin.GetAsync("/api/users/mario")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await _admin.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/users/admin"))).StatusCode,
            "L'ultimo amministratore non può essere eliminato.");
    }

    [TestMethod]
    public async Task CustomFields_SafeFilterOnly()
    {
        await SendAsync(HttpMethod.Post, "/api/customfields", """{ "CustomType": "POST", "ObjectId": "1", "Key": "colore", "Value": "rosso" }""", HttpStatusCode.Created);
        await SendAsync(HttpMethod.Post, "/api/customfields", """{ "CustomType": "POST", "ObjectId": "2", "Key": "colore", "Value": "blu" }""", HttpStatusCode.Created);

        var filtered = await GetJsonAsync<JsonArray>("/api/customfields?filter=" + Uri.EscapeDataString("CustomType == \"POST\" && ObjectId == \"1\""));
        Assert.AreEqual("rosso", filtered.Single()!["Value"]!.GetValue<string>());

        var attack = await _admin.GetAsync("/api/customfields?filter=" + Uri.EscapeDataString("\"\".GetType().Assembly.FullName != null"));
        Assert.AreEqual(HttpStatusCode.BadRequest, attack.StatusCode, "Le espressioni LINQ arbitrarie sono rifiutate.");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/posts?order=Title;drop")).StatusCode);
    }

    [TestMethod]
    public async Task CategoriesTagsBlogrollPingsAndFilters()
    {
        var categories = await GetJsonAsync<JsonArray>("/api/categories?take=0");
        Assert.AreEqual("General", categories.Single()!["Title"]!.GetValue<string>());
        Assert.AreEqual(1, categories.Single()!["Count"]!.GetValue<int>());

        var postId = (await GetJsonAsync<JsonArray>("/api/posts?take=0"))[0]!["Id"]!.GetValue<string>();
        var post = await GetJsonAsync<JsonObject>($"/api/posts/{postId}");
        post["Tags"] = JsonNode.Parse("""[ { "TagName": "vecchio" } ]""");
        await SendAsync(HttpMethod.Put, "/api/posts/update/foo", post.ToJsonString());
        await SendAsync(HttpMethod.Put, "/api/tags/update/foo", """{ "OldTag": "vecchio", "NewTag": "nuovo" }""");
        Assert.AreEqual("nuovo", (await GetJsonAsync<JsonArray>("/api/tags?take=0")).Single()!["TagName"]!.GetValue<string>());

        await SendAsync(HttpMethod.Post, "/api/blogroll", """{ "Title": "Amico", "BlogUrl": "amico.it", "FeedUrl": "https://amico.it/feed" }""", HttpStatusCode.Created);
        Assert.AreEqual("http://amico.it/", (await GetJsonAsync<JsonArray>("/api/blogroll")).Single()!["BlogUrl"]!.GetValue<string>());

        Assert.AreEqual(1, (await GetJsonAsync<JsonArray>("/api/pingservices")).Count, "Servizio di ping predefinito.");

        await SendAsync(HttpMethod.Post, "/api/commentfilter", """{ "Action": "Block", "Subject": "Email", "Operation": "Contains", "Filter": "spam.com" }""", HttpStatusCode.Created);
        Assert.AreEqual("spam.com", (await GetJsonAsync<JsonArray>("/api/commentfilter?take=0")).Single()!["Filter"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task UploadsAreStoredOutsidePublicFolderAndServedSafely()
    {
        using var form = new MultipartFormDataContent();
        var image = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "file", "foto prova.png");

        var upload = await _admin.PostAsync("/api/upload?action=image", form);
        Assert.AreEqual(HttpStatusCode.Created, upload.StatusCode);
        var url = (await upload.Content.ReadAsStringAsync()).Trim('"');
        StringAssert.StartsWith(url, "/image.axd?picture=");

        var served = await _admin.GetAsync(url);
        Assert.AreEqual("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());

        var year = DateTime.UtcNow.Year.ToString();
        var listing = await GetJsonAsync<JsonArray>("/api/filemanager?path=" + Uri.EscapeDataString("/" + year));
        Assert.IsTrue(listing.Any(f => f!["Name"]!.GetValue<string>() == "..."), "Voce per tornare alla cartella superiore.");

        Assert.AreEqual(HttpStatusCode.NotFound, (await _admin.GetAsync("/file.axd?file=" + Uri.EscapeDataString("../../installation.json"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _admin.GetAsync("/image.axd?picture=" + Uri.EscapeDataString("../../data/blogs.xml"))).StatusCode);
    }

    [TestMethod]
    public async Task Blogs_CreateChildBlogServedUnderItsPath()
    {
        var created = await SendJsonAsync<JsonObject>(HttpMethod.Post, "/api/blogs",
            """{ "Name": "secondo", "UserName": "luigi", "Email": "luigi@example.com", "Password": "Password-Luigi-1", "ConfirmPassword": "Password-Luigi-1" }""",
            HttpStatusCode.Created);
        Assert.AreEqual("/secondo/", created["RelativeWebRoot"]!.GetValue<string>());
        Assert.AreEqual(2, (await GetJsonAsync<JsonArray>("/api/blogs?take=0")).Count);

        var reserved = await _admin.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/blogs")
        {
            Content = Json("""{ "Name": "admin", "UserName": "x", "Email": "x@example.com", "Password": "Password-X-123", "ConfirmPassword": "Password-X-123" }"""),
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, reserved.StatusCode);

        StringAssert.Contains(await _admin.GetStringAsync("/secondo/"), "secondo");
        using var luigi = _factory.CreateBrowser(followRedirects: false);
        var login = await luigi.PostFormAsync("/secondo/account/login", new() { ["UserName"] = "luigi", ["Password"] = "Password-Luigi-1" });
        Assert.AreEqual(HttpStatusCode.Redirect, login.StatusCode, "L'amministratore del nuovo blog può accedere al suo blog.");
    }

    /// <summary>
    /// Creare blog figli è riservato agli amministratori, che possono concederlo ad altri ruoli;
    /// modificare ed eliminare i blog resta comunque solo agli amministratori.
    /// </summary>
    [TestMethod]
    public async Task Blogs_CreationRequiresTheRightGrantedByAdministrators()
    {
        await _factory.RunAsSystemAsync(services =>
            services.GetRequiredService<BlogEngine.Core.Services.IUserService>().CreateUserAsync("redattore", "redattore@example.com", "Password-Redattore-1", ["Editors"]));
        using var editor = _factory.CreateBrowser();
        await editor.PostFormAsync("/account/login", new() { ["UserName"] = "redattore", ["Password"] = "Password-Redattore-1" });
        editor.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        var newBlog = """{ "Name": "redazione", "UserName": "redattore", "Email": "redattore@example.com", "Password": "Password-Redattore-1", "ConfirmPassword": "Password-Redattore-1" }""";

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await editor.PostAsync("/api/blogs", Json(newBlog))).StatusCode, "Per default i redattori non creano blog.");

        // L'amministratore concede il permesso al ruolo dei redattori dalla pagina dei ruoli.
        var rightsJson = await _admin.GetStringAsync("/api/roles/getrights/Editors");
        var groups = JsonNode.Parse(rightsJson)!.AsArray();
        var permission = groups.SelectMany(g => g!["Permissions"]!.AsArray()).Single(p => p!["Id"]!.GetValue<string>() == "CreateNewBlogs")!;
        Assert.IsFalse(permission["IsChecked"]!.GetValue<bool>());
        permission["IsChecked"] = true;
        Assert.AreEqual(HttpStatusCode.OK, (await _admin.PutAsync("/api/roles/saverights/Editors", Json(groups.ToJsonString()))).StatusCode);

        var created = await editor.PostAsync("/api/blogs", Json(newBlog));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());

        var id = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["Id"]!.GetValue<string>();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await editor.DeleteAsync($"/api/blogs/{id}")).StatusCode, "Eliminare i blog resta riservato agli amministratori.");
    }

    /// <summary>Il pulsante di uscita del menu dell'amministrazione invia il token antiforgery.</summary>
    [TestMethod]
    public async Task AdminLogoutButton_SignsOut()
    {
        using var browser = _factory.CreateBrowser(followRedirects: false);
        await browser.PostFormAsync("/account/login", new() { ["UserName"] = BlogWebFactory.AdminUser, ["Password"] = BlogWebFactory.AdminPassword });

        var shell = await browser.GetStringAsync("/admin/");
        var form = Regex.Match(shell, "<form id=\"admin-logout\"[^>]*>(?<body>.*?)</form>", RegexOptions.Singleline);
        Assert.IsTrue(form.Success);
        var token = Regex.Match(form.Groups["body"].Value, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<value>[^\"]+)\"");
        Assert.IsTrue(token.Success, "Il modulo di uscita deve contenere il token antiforgery.");

        var logout = await browser.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(token.Groups["value"].Value),
        }));
        Assert.AreEqual(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.AreEqual(HttpStatusCode.Redirect, (await browser.GetAsync("/admin/")).StatusCode, "Dopo l'uscita l'amministrazione richiede di nuovo l'accesso.");
    }

    [TestMethod]
    public async Task Packages_ListLocalThemesOnly()
    {
        Assert.AreEqual(0, (await GetJsonAsync<JsonArray>("/api/packages?filter=all")).Count, "La galleria online non esiste più.");
        Assert.AreEqual(HttpStatusCode.BadRequest, (await _admin.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/packages/install/qualcosa") { Content = Json("{}") })).StatusCode);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<T> GetJsonAsync<T>(string url)
        where T : JsonNode
    {
        var response = await _admin.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return (T)JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string url, string json, HttpStatusCode expected)
        where T : JsonNode
    {
        var response = await _admin.SendAsync(new HttpRequestMessage(method, url) { Content = Json(json) });
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, $"{method} {url}: {body}");
        return (T)JsonNode.Parse(body)!;
    }

    private async Task SendAsync(HttpMethod method, string url, string? json = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var request = new HttpRequestMessage(method, url);
        if (json is not null)
        {
            request.Content = Json(json);
        }

        var response = await _admin.SendAsync(request);
        Assert.AreEqual(expected, response.StatusCode, $"{method} {url}: {await response.Content.ReadAsStringAsync()}");
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$")]
    private static partial Regex DateRegex();
}
