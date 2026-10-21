using System.Globalization;
using BlogEngine.Core.Extensions;
using BlogEngine.Extensions;
using BlogEngine.Security;
using BlogEngine.Web.Admin;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Pages.Admin.Extensions;

/// <summary>
/// Impostazioni di un'estensione (porting di admin/Extensions/Settings.aspx): per ogni gruppo di
/// impostazioni un modulo (valori singoli) oppure una tabella a cui aggiungere o togliere righe.
/// </summary>
public sealed class SettingsModel(ExtensionRegistry registry, IExtensionSettingsService settings) : AdminPageModel(Rights.ManageExtensions)
{
    public ExtensionDescriptor Extension { get; private set; } = null!;

    public Dictionary<string, ExtensionSettingsValues> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Errori di validazione per gruppo di impostazioni.</summary>
    public Dictionary<string, List<string>> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Saved { get; private set; }

    public IEnumerable<ExtensionSettingsDefinition> VisibleSettings => Extension.Settings.Where(s => !s.Hidden);

    public async Task<IActionResult> OnGetAsync(string ext) => await LoadAsync(ext) ? Page() : NotFound();

    /// <summary>Salva un gruppo di valori singoli.</summary>
    public async Task<IActionResult> OnPostSaveAsync(string ext, string group)
    {
        if (!await LoadAsync(ext) || Definition(group) is not { IsScalar: true } definition)
        {
            return NotFound();
        }

        var row = ReadRow(definition, group);
        if (Errors.ContainsKey(group))
        {
            return Page();
        }

        await settings.SaveAsync(Extension.Name, new ExtensionSettingsValues { Name = definition.Name, Rows = [row] });
        return await ReloadAsync(ext, group);
    }

    /// <summary>Aggiunge una riga a una tabella.</summary>
    public async Task<IActionResult> OnPostAddAsync(string ext, string group)
    {
        if (!await LoadAsync(ext) || Definition(group) is not { IsScalar: false } definition)
        {
            return NotFound();
        }

        var row = ReadRow(definition, group);
        var current = Values[definition.Name];
        if (definition.Parameters.FirstOrDefault(p => p.KeyField) is { } key
            && current.Rows.Any(r => string.Equals(r.GetValueOrDefault(key.Name), row.GetValueOrDefault(key.Name), StringComparison.OrdinalIgnoreCase)))
        {
            AddError(group, $"{key.Label}: \"{row.GetValueOrDefault(key.Name)}\" è già presente.");
        }

        if (Errors.ContainsKey(group))
        {
            return Page();
        }

        current.Rows.Add(row);
        await settings.SaveAsync(Extension.Name, current);
        return await ReloadAsync(ext, group);
    }

    /// <summary>Elimina una riga di una tabella.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(string ext, string group, int index)
    {
        if (!await LoadAsync(ext) || Definition(group) is not { IsScalar: false } definition)
        {
            return NotFound();
        }

        var current = Values[definition.Name];
        if (index >= 0 && index < current.Rows.Count)
        {
            current.Rows.RemoveAt(index);
            await settings.SaveAsync(Extension.Name, current);
        }

        return await ReloadAsync(ext, group);
    }

    public static string Display(ExtensionParameter parameter, string? value) =>
        parameter.Type == ExtensionParameterType.Boolean
            ? (bool.TryParse(value, out var flag) && flag ? "✓" : string.Empty)
            : parameter.Options.Select(Option).FirstOrDefault(o => o.Value == value).Text ?? value ?? string.Empty;

    /// <summary>Le opzioni sono "valore" oppure "valore|testo" (come "en|English" in reCAPTCHA).</summary>
    public static (string Value, string Text) Option(string option)
    {
        var parts = option.Split('|', 2);
        return (parts[0], parts.Length > 1 ? parts[1] : parts[0]);
    }

    private async Task<IActionResult> ReloadAsync(string ext, string group)
    {
        await LoadAsync(ext);
        Saved = group;
        return Page();
    }

    private async Task<bool> LoadAsync(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext) || registry.Find(ext) is not { } extension)
        {
            return false;
        }

        Extension = extension;
        Values.Clear();
        foreach (var definition in extension.Settings)
        {
            Values[definition.Name] = await settings.GetAsync(extension.Name, definition.Name);
        }

        return true;
    }

    private ExtensionSettingsDefinition? Definition(string group) =>
        Extension.Settings.FirstOrDefault(s => !s.Hidden && string.Equals(s.Name, group, StringComparison.OrdinalIgnoreCase));

    /// <summary>Legge e controlla i campi inviati (obbligatori, lunghezza, numeri, opzioni ammesse).</summary>
    private Dictionary<string, string> ReadRow(ExtensionSettingsDefinition definition, string group)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in definition.Parameters)
        {
            var field = $"{group}.{parameter.Name}";
            var value = parameter.Type switch
            {
                ExtensionParameterType.Boolean => Request.Form.ContainsKey(field) ? "true" : "false",
                ExtensionParameterType.ListBox => string.Join(',', Request.Form[field].Where(v => !string.IsNullOrEmpty(v))),
                _ => Request.Form[field].ToString().Trim(),
            };

            if (parameter.Required && parameter.Type != ExtensionParameterType.Boolean && value.Length == 0)
            {
                AddError(group, $"{parameter.Label}: obbligatorio.");
            }
            else if (value.Length > parameter.MaxLength && parameter.Type is ExtensionParameterType.String)
            {
                AddError(group, $"{parameter.Label}: massimo {parameter.MaxLength} caratteri.");
            }
            else if (value.Length > 0 && !IsValidNumber(parameter.Type, value))
            {
                AddError(group, $"{parameter.Label}: valore numerico non valido.");
            }
            else if (parameter.Type is ExtensionParameterType.DropDown or ExtensionParameterType.RadioGroup
                     && parameter.Options.Count > 0 && value.Length > 0 && parameter.Options.All(o => Option(o).Value != value))
            {
                AddError(group, $"{parameter.Label}: valore non ammesso.");
            }

            row[parameter.Name] = value;
        }

        return row;
    }

    private static bool IsValidNumber(ExtensionParameterType type, string value) => type switch
    {
        ExtensionParameterType.Integer => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        ExtensionParameterType.Long => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        ExtensionParameterType.Float or ExtensionParameterType.Double or ExtensionParameterType.Decimal =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        _ => true,
    };

    private void AddError(string group, string message)
    {
        if (!Errors.TryGetValue(group, out var list))
        {
            Errors[group] = list = [];
        }

        list.Add(message);
    }
}
