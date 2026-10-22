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
    IWebHostEnvironment environment,
    Microsoft.Extensions.Localization.IStringLocalizer<Labels> labels) : AdminPageModel(Rights.AccessAdminSettingsPages)
{
    private string T(string key) => labels[key];

    public List<(string Title, List<DiagnosticItem> Items)> Sections { get; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!rights.IsAdministrator)
        {
            return Forbid();
        }

        var process = Process.GetCurrentProcess();
        Sections.Add((T("diagSystem"), [
            new(T("diagVersion"), AdminViewContext.Version),
            new(".NET", RuntimeInformation.FrameworkDescription),
            new(T("diagOperatingSystem"), RuntimeInformation.OSDescription),
            new(T("diagProcess"), $"{RuntimeInformation.ProcessArchitecture}, {(Environment.Is64BitProcess ? "64" : "32")} bit, {process.ProcessName}"),
            new(T("diagEnvironment"), environment.EnvironmentName),
            new(T("diagStarted"), process.StartTime.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)),
            new(T("diagServerTime"), DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)),
            new(T("diagMemory"), $"{process.WorkingSet64 / 1024 / 1024} MB"),
            new(T("diagProcessUser"), $@"{Environment.UserDomainName}\{Environment.UserName}"),
        ]));

        var folders = new List<DiagnosticItem>
        {
            new(T("diagSiteFolder"), paths.ContentRoot),
            new(T("diagDataFolder"), paths.DataRoot),
        };
        foreach (var (name, folder) in new[]
        {
            ("App_Data", paths.DataRoot), (T("diagData"), paths.Data), (T("diagUploadedFiles"), paths.Files), ("Cache", paths.Cache),
            (T("diagKeys"), paths.Keys), ("Log", paths.Logs), (T("diagUploadedThemes"), paths.InstalledThemes),
            (T("diagUploadedExtensions"), paths.InstalledExtensions), (T("diagUploadedWidgets"), paths.InstalledWidgets),
        })
        {
            var (ok, message) = CanWrite(folder);
            folders.Add(new(string.Format(CultureInfo.CurrentCulture, T("diagWritable"), name), message, ok));
        }

        Sections.Add((T("diagFolders"), folders));

        var blogSettings = await settings.GetAsync();
        var theme = themes.Find(blogSettings.Theme);
        var keys = Directory.Exists(paths.Keys) ? Directory.EnumerateFiles(paths.Keys, "*.xml").Count() : 0;
        var log = new FileInfo(Path.Combine(paths.Logs, "blogengine.log"));
        Sections.Add(("Blog", [
            new(T("diagDataProvider"), installation.DataProvider.ToString()),
            new("Blog", (await blogs.GetBlogsAsync()).Count.ToString(CultureInfo.InvariantCulture)),
            new("Post", (await posts.GetPostsAsync()).Count.ToString(CultureInfo.InvariantCulture)),
            new(T("diagActiveTheme"), theme is null ? string.Format(CultureInfo.CurrentCulture, T("diagThemeMissing"), blogSettings.Theme) : $"{theme.Title} ({(theme.IsInstalledByUpload ? "App_Data/themes" : "Custom/Themes")})", theme is not null),
            new(T("diagLanguage"), string.IsNullOrEmpty(blogSettings.Language) ? T("diagAutomatic") : blogSettings.Language),
            new(T("diagTimeZone"), ValidTimeZone(blogSettings.TimeZoneId), TimeZoneExists(blogSettings.TimeZoneId)),
            new(T("diagMailServer"), string.IsNullOrWhiteSpace(blogSettings.SmtpServer) ? T("diagNotConfigured") : $"{blogSettings.SmtpServer}:{blogSettings.SmtpServerPort}{(blogSettings.EnableSsl ? " (SSL)" : string.Empty)}", !string.IsNullOrWhiteSpace(blogSettings.SmtpServer)),
            new(T("diagEncryptionKeys"), $"{keys} in App_Data/keys", keys > 0),
            new(T("diagLogFile"), log.Exists ? $"{log.FullName} ({log.Length / 1024} KB)" : T("diagNotCreated")),
        ]));

        Sections.Add((T("diagExtensions"), extensions.All.Select(e => new DiagnosticItem(
            e.Name,
            e.Error ?? $"{(extensionStates.IsEnabled(e.Name) ? T("diagEnabled") : T("diagDisabled"))}, {(e.IsUploaded ? "App_Data/extensions" : "Custom/Extensions")}",
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

    private string ValidTimeZone(string id) =>
        string.IsNullOrEmpty(id) ? "UTC" : TimeZoneExists(id) ? id : string.Format(CultureInfo.CurrentCulture, T("diagTimeZoneUnknown"), id);
}
