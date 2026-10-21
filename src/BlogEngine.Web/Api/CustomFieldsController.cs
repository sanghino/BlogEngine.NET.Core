using BlogEngine.Core.Services;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/customfields")]
public sealed class CustomFieldsController(
    ICustomFieldService fields,
    CustomFieldRepository repository,
    IRightsService rights,
    IBlogContext context,
    ThemeCatalog themes) : AdminApiController
{
    /// <summary>Campi del blog; filtro tipico: CustomType == "THEME" &amp;&amp; ObjectId == "standard".</summary>
    [HttpGet("")]
    public async Task<IEnumerable<CustomFieldItem>> Get(string filter = "")
    {
        await DemandAsync();
        if (filter.Contains(CustomField.ThemeType, StringComparison.OrdinalIgnoreCase))
        {
            await RegisterThemeFieldsAsync();
        }

        var all = (await repository.GetAllAsync(context.Blog.Id, HttpContext.RequestAborted)).Select(ToItem).ToList();
        return ApiQuery.Filter(all, filter).ToList();
    }

    /// <summary>
    /// Crea, con il valore predefinito, i campi usati dai template dei temi e non ancora salvati
    /// (come faceva CustomFieldsParser leggendo i token [CUSTOMFIELD|THEME|...]).
    /// </summary>
    private async Task RegisterThemeFieldsAsync()
    {
        foreach (var theme in themes.GetThemes())
        {
            var existing = (await fields.GetFieldsAsync(CustomField.ThemeType, theme.Id)).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in WidgetService.FindThemeFields(theme).Where(f => !existing.Contains(f.Key)))
            {
                await fields.SaveAsync(new CustomField { CustomType = CustomField.ThemeType, ObjectId = theme.Id, Key = key, Value = value });
            }
        }
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] CustomFieldItem item)
    {
        var field = ToField(item);

        // I campi del profilo appartengono sempre all'utente corrente.
        if (string.Equals(field.CustomType, CustomField.ProfileType, StringComparison.OrdinalIgnoreCase))
        {
            field.ObjectId = context.UserName ?? string.Empty;
        }

        await fields.SaveAsync(field);
        return StatusCode(StatusCodes.Status201Created, ToItem(field));
    }

    /// <summary>Sostituisce tutti i campi dell'oggetto (tipo e id del primo elemento) con quelli ricevuti.</summary>
    [HttpPut("")]
    public async Task<IActionResult> Put([FromBody] List<CustomFieldItem> items)
    {
        if (items is { Count: > 0 })
        {
            await fields.ClearAsync(items[0].CustomType ?? string.Empty, items[0].ObjectId ?? string.Empty);
            foreach (var item in items)
            {
                await fields.SaveAsync(ToField(item));
            }
        }

        return Ok();
    }

    [HttpDelete("")]
    public async Task<IActionResult> Delete([FromBody] CustomFieldItem item)
    {
        await fields.DeleteAsync(ToField(item));
        return Ok();
    }

    private CustomField ToField(CustomFieldItem item) => new()
    {
        BlogId = context.Blog.Id,
        CustomType = item.CustomType ?? string.Empty,
        ObjectId = item.ObjectId ?? string.Empty,
        Key = item.Key ?? string.Empty,
        Value = item.Value ?? string.Empty,
        Attribute = item.Attribute ?? string.Empty,
    };

    private static CustomFieldItem ToItem(CustomField field) => new()
    {
        BlogId = field.BlogId,
        CustomType = field.CustomType,
        ObjectId = field.ObjectId,
        Key = field.Key,
        Value = field.Value,
        Attribute = field.Attribute,
    };

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminPages))
        {
            throw new UnauthorizedAccessException();
        }
    }
}
