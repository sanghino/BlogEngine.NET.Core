using System.Globalization;
using System.Text.Json;
using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Admin;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Pages.Widgets;

/// <summary>
/// Impostazioni di un widget, mostrate nella finestra dell'amministrazione (sostituisce i vecchi
/// Custom/Widgets/&lt;Nome&gt;/edit.cshtml). Il modulo è generato dalle impostazioni descritte nel
/// widget.json del widget: anche i widget aggiunti copiando una cartella hanno la loro pagina.
/// </summary>
public sealed class EditModel(
    WidgetCatalog catalog,
    WidgetService widgets,
    NewsletterSubscribers newsletter,
    ICategoryService categories,
    IPostService posts) : AdminPageModel(Rights.ManageWidgets)
{
    public WidgetInfo Widget { get; private set; } = null!;

    public string WidgetId { get; private set; } = string.Empty;

    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> CategoryTitles { get; private set; } = [];

    public IReadOnlyList<string> Authors { get; private set; } = [];

    public IReadOnlyList<LinkListWidget.LinkItem> Links { get; private set; } = [];

    public IReadOnlyList<string> Subscribers { get; private set; } = [];

    public bool Saved { get; private set; }

    public async Task<IActionResult> OnGetAsync(string name, string? id) =>
        await LoadAsync(name, id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(string name, string? id, string? linkAction, string? linkTitle, string? linkUrl, bool linkNewWindow, string? removeEmail)
    {
        if (!await LoadAsync(name, id))
        {
            return NotFound();
        }

        foreach (var setting in Widget.Settings)
        {
            switch (setting.Type.ToLowerInvariant())
            {
                case "checkbox":
                    Values[setting.Key] = Request.Form.ContainsKey(setting.Key) ? "true" : "false";
                    break;
                case "number":
                    Values[setting.Key] = int.TryParse(Request.Form[setting.Key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                        ? Math.Clamp(number, 0, 1000).ToString(CultureInfo.InvariantCulture)
                        : setting.Default;
                    break;
                case "links":
                    // I pulsanti dei singoli link inviano "comando|id" (es. "delete|...").
                    var parts = (linkAction ?? string.Empty).Split('|', 2);
                    Values[setting.Key] = JsonSerializer.Serialize(ChangeLinks(parts[0], parts.Length > 1 ? parts[1] : null, linkTitle, linkUrl, linkNewWindow));
                    break;
                case "newsletter":
                    if (!string.IsNullOrWhiteSpace(removeEmail))
                    {
                        await newsletter.RemoveAsync(removeEmail);
                    }

                    break;
                case "blogroll":
                    break;
                default:
                    if (Request.Form.TryGetValue(setting.Key, out var value))
                    {
                        Values[setting.Key] = value.ToString();
                    }

                    break;
            }
        }

        await widgets.SaveSettingsAsync(WidgetId, Values);
        await LoadAsync(name, id);
        Saved = true;
        return Page();
    }

    public string Value(WidgetSettingDefinition setting) =>
        Values.TryGetValue(setting.Key, out var value) ? value : setting.Default;

    private async Task<bool> LoadAsync(string name, string? id)
    {
        if (catalog.Find(name) is not { Settings.Count: > 0 } widget || string.IsNullOrWhiteSpace(id) || id.Length > 100)
        {
            return false;
        }

        Widget = widget;
        WidgetId = id;
        Values.Clear();
        foreach (var (key, value) in await widgets.GetSettingsAsync(id))
        {
            Values[key] = value;
        }

        CategoryTitles = (await categories.GetCategoriesAsync()).Select(c => c.Title).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
        Authors = (await posts.GetPostsAsync()).Select(p => p.Author).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        Links = ReadLinks();
        Subscribers = widget.Settings.Any(s => s.Type == "newsletter") ? await newsletter.GetAsync() : [];
        return true;
    }

    private List<LinkListWidget.LinkItem> ReadLinks()
    {
        try
        {
            return Values.TryGetValue("links", out var json) ? JsonSerializer.Deserialize<List<LinkListWidget.LinkItem>>(json) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Aggiunge, modifica o elimina un link (porting del vecchio editor di LinkList).</summary>
    private List<LinkListWidget.LinkItem> ChangeLinks(string? action, string? id, string? title, string? url, bool newWindow)
    {
        var links = ReadLinks();
        switch (action)
        {
            case "add" when !string.IsNullOrWhiteSpace(title) && IsSafeUrl(url):
                links.Add(new LinkListWidget.LinkItem { Title = title.Trim(), Url = url!.Trim(), NewWindow = newWindow });
                break;
            case "update" when links.FirstOrDefault(l => l.Id == id) is { } existing && !string.IsNullOrWhiteSpace(title) && IsSafeUrl(url):
                existing.Title = title.Trim();
                existing.Url = url!.Trim();
                existing.NewWindow = newWindow;
                break;
            case "delete":
                links.RemoveAll(l => l.Id == id);
                break;
            case "up" or "down" when links.FindIndex(l => l.Id == id) is var index and >= 0:
                var target = action == "up" ? index - 1 : index + 1;
                if (target >= 0 && target < links.Count)
                {
                    (links[index], links[target]) = (links[target], links[index]);
                }

                break;
        }

        return links;
    }

    private static bool IsSafeUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.RelativeOrAbsolute, out var uri)
        && (!uri.IsAbsoluteUri || uri.Scheme is "http" or "https" or "mailto");
}
