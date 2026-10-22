using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using BlogEngine.Core.Configuration;
using BlogEngine.Core.Extensions;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Admin;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Pages.Admin;

/// <summary>Un controllo della pagina di diagnostica.</summary>
public sealed record DiagnosticItem(string Name, string Value, bool? Ok = null);

/// <summary>
/// Pagina di diagnostica per gli amministratori: versione di .NET, percorsi, permessi di scrittura
/// delle cartelle di App_Data, estensioni, tema, posta. Utile per verificare l'installazione sull'hosting.
/// </summary>
public sealed class DiagnosticsModel(
    BlogEnginePaths paths,
    InstallationState installation,
    ExtensionRegistry extensions,
    ExtensionStates extensionStates,
    ThemeCatalog themes,
    ISettingsService settings,
    IBlogService blogs,
    IPostService posts,
    IRightsService rights,
    IWebHostEnvironment environment) : AdminPageModel(Rights.AccessAdminSettingsPages)
{
    public List<(string Title, List<DiagnosticItem> Items)> Sections { get; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!rights.IsAdministrator)
        {
            return Forbid();
        }

        var process = Process.GetCurrentProcess();
        Sections.Add(("Sistema", [
            new("Versione", AdminViewContext.Version),
            new(".NET", RuntimeInformation.FrameworkDescription),
            new("Sistema operativo", RuntimeInformation.OSDescription),
            new("Processo", $"{RuntimeInformation.ProcessArchitecture}, {(Environment.Is64BitProcess ? "64" : "32")} bit, {process.ProcessName}"),
            new("Ambiente", environment.EnvironmentName),
            new("Avviato", process.StartTime.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)),
            new("Ora del server (UTC)", DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)),
            new("Memoria", $"{process.WorkingSet64 / 1024 / 1024} MB"),
            new("Utente del processo", $@"{Environment.UserDomainName}\{Environment.UserName}"),
        ]));

        var folders = new List<DiagnosticItem>
        {
            new("Cartella del sito", paths.ContentRoot),
            new("Cartella dei dati", paths.DataRoot),
        };
        foreach (var (name, folder) in new[]
        {
            ("App_Data", paths.DataRoot), ("Dati", paths.Data), ("File caricati", paths.Files), ("Cache", paths.Cache),
            ("Chiavi", paths.Keys), ("Log", paths.Logs), ("Temi caricati", paths.InstalledThemes),
            ("Estensioni caricate", paths.InstalledExtensions), ("Widget caricati", paths.InstalledWidgets),
        })
        {
            var (ok, message) = CanWrite(folder);
            folders.Add(new($"Scrittura: {name}", message, ok));
        }

        Sections.Add(("Cartelle e permessi", folders));

        var blogSettings = await settings.GetAsync();
        var theme = themes.Find(blogSettings.Theme);
        var keys = Directory.Exists(paths.Keys) ? Directory.EnumerateFiles(paths.Keys, "*.xml").Count() : 0;
        var log = new FileInfo(Path.Combine(paths.Logs, "blogengine.log"));
        Sections.Add(("Blog", [
            new("Provider dei dati", installation.DataProvider.ToString()),
            new("Blog", (await blogs.GetBlogsAsync()).Count.ToString(CultureInfo.InvariantCulture)),
            new("Post", (await posts.GetPostsAsync()).Count.ToString(CultureInfo.InvariantCulture)),
            new("Tema attivo", theme is null ? $"{blogSettings.Theme} (non trovato, si usa quello di riserva)" : $"{theme.Title} ({(theme.IsInstalledByUpload ? "App_Data/themes" : "Custom/Themes")})", theme is not null),
            new("Lingua", string.IsNullOrEmpty(blogSettings.Language) ? "automatica" : blogSettings.Language),
            new("Fuso orario", ValidTimeZone(blogSettings.TimeZoneId), TimeZoneExists(blogSettings.TimeZoneId)),
            new("Server di posta", string.IsNullOrWhiteSpace(blogSettings.SmtpServer) ? "non configurato" : $"{blogSettings.SmtpServer}:{blogSettings.SmtpServerPort}{(blogSettings.EnableSsl ? " (SSL)" : string.Empty)}", !string.IsNullOrWhiteSpace(blogSettings.SmtpServer)),
            new("Chiavi di cifratura", $"{keys} in App_Data/keys", keys > 0),
            new("File di log", log.Exists ? $"{log.FullName} ({log.Length / 1024} KB)" : "non ancora creato"),
        ]));

        Sections.Add(("Estensioni", extensions.All.Select(e => new DiagnosticItem(
            e.Name,
            e.Error ?? $"{(extensionStates.IsEnabled(e.Name) ? "attiva" : "disattivata")}, {(e.IsUploaded ? "App_Data/extensions" : "Custom/Extensions")}",
            e.Error is null)).ToList()));

        return Page();
    }

    /// <summary>Prova a scrivere e cancellare un file nella cartella.</summary>
    private static (bool Ok, string Message) CanWrite(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var test = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            System.IO.File.WriteAllText(test, "ok");
            System.IO.File.Delete(test);
            return (true, "OK");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ex.Message);
        }
    }

    private static bool TimeZoneExists(string id)
    {
        try
        {
            return string.IsNullOrEmpty(id) || TimeZoneInfo.FindSystemTimeZoneById(id) is not null;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static string ValidTimeZone(string id) =>
        string.IsNullOrEmpty(id) ? "UTC" : TimeZoneExists(id) ? id : $"{id} (non riconosciuto dal server)";
}
