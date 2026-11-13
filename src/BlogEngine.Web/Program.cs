using BlogEngine.Core.Configuration;
using BlogEngine.Core.Data;
using BlogEngine.Web.Admin;
using BlogEngine.Web.Endpoints;
using BlogEngine.Web.Hosting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = PublishedLayout.ContentRoot() });
builder.Services.AddBlogEngineWeb(builder.Configuration, builder.Environment);

var app = builder.Build();

app.Services.GetRequiredService<BlogEnginePaths>().EnsureDataDirectories();
var installation = app.Services.GetRequiredService<InstallationState>();
if (installation.IsInstalled)
{
    await app.Services.GetRequiredService<IDataStoreInitializer>().InitializeAsync();
    await app.Services.GetRequiredService<BlogEngine.Core.Extensions.ExtensionStates>().LoadAsync();
}
else if (args is ["import-bludit" or BlogEngine.Web.Import.BlogEngineCommand.Name, ..])
{
    app.Logger.LogError("Prima di importare configura il blog: avvia il sito e apri /setup.");
    return 1;
}
else
{
    installation.EnsureSetupKey();
    app.Logger.LogWarning("Il blog non è ancora configurato: apri /setup e inserisci la chiave contenuta in {SetupKeyPath}.", installation.SetupKeyPath);
}

// Importazione da Bludit dal prompt dei comandi: BlogEngine.Web import-bludit <cartella bl-content> [autore]
if (args is ["import-bludit", var bluditFolder, ..])
{
    return await BlogEngine.Web.Import.BluditCommand.RunAsync(app.Services, bluditFolder, args.Length > 2 ? args[2] : null);
}

// Importazione dal BlogML di un vecchio BlogEngine.NET: BlogEngine.Web import-blogengine <BlogML.xml> [indirizzo vecchio sito] [fuso orario]
if (args is [BlogEngine.Web.Import.BlogEngineCommand.Name, ..])
{
    return await BlogEngine.Web.Import.BlogEngineCommand.RunAsync(app.Services, args);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
}

app.UseMiddleware<SecurityHeadersMiddleware>();
// Script, stili e template dell'amministrazione vengono ricontrollati a ogni uso (vedi StaticFileCaching).
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = StaticFileCaching.Apply });

// File dei temi e dei widget (CSS, immagini, script) dalle cartelle Custom/ e da quelle caricate in App_Data.
// I template .liquid non vengono mai serviti: hanno un'estensione sconosciuta ai file statici.
var paths = app.Services.GetRequiredService<BlogEnginePaths>();
app.UseCustomFolderFiles("/Custom/Themes", paths.BundledThemes, paths.InstalledThemes);
app.UseCustomFolderFiles("/Custom/Widgets", paths.BundledWidgets, paths.InstalledWidgets);

app.UseMiddleware<SetupGateMiddleware>();
app.UseMiddleware<BlogContextMiddleware>();
app.UseMiddleware<WwwRedirectMiddleware>();
app.UseMiddleware<LegacyUrlMiddleware>();
app.UseMiddleware<ConditionalCompressionMiddleware>();
app.UseMiddleware<ReferrerMiddleware>();

// Pagine di errore (404...) con il tema del blog, solo per le pagine pubbliche richieste in GET.
app.UseWhen(
    http => (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)) && !IsSystemPath(http.Request.Path),
    branch => branch.UseStatusCodePagesWithReExecute("/error/{0}"));

// L'amministrazione usa indirizzi relativi ("app/..."): va aperta con la barra finale.
app.Use((http, next) =>
{
    if (http.Request.Path.Equals("/admin", StringComparison.OrdinalIgnoreCase))
    {
        http.Response.Redirect($"{http.Request.PathBase}/admin/{http.Request.QueryString}");
        return Task.CompletedTask;
    }

    return next(http);
});

app.UseRequestLocalization();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseMiddleware<MaintenanceMiddleware>();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapFileEndpoints();
app.MapAdminResources();

app.Run();
return 0;

// Indirizzi con pagine di errore proprie (API, amministrazione, account, configurazione).
static bool IsSystemPath(PathString path) =>
    path.StartsWithSegments("/api") || path.StartsWithSegments("/admin") || path.StartsWithSegments("/account")
    || path.StartsWithSegments("/setup") || path.StartsWithSegments("/error");

// Rende la classe Program visibile ai test di integrazione (WebApplicationFactory).
public partial class Program;
