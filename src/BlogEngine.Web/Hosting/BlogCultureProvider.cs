using System.Globalization;
using BlogEngine.Services;
using Microsoft.AspNetCore.Localization;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Lingua dell'interfaccia secondo l'impostazione Culture del blog. Con "Auto" (predefinito) decide il
/// browser tramite l'intestazione Accept-Language, come nella versione .NET Framework.
/// </summary>
public sealed class BlogCultureProvider : RequestCultureProvider
{
    public const string AutoCulture = "Auto";

    /// <summary>Lingue di cui esistono le traduzioni (Resources/Labels.*.resx), più l'inglese predefinito.</summary>
    public static readonly IReadOnlyList<CultureInfo> SupportedCultures = new[]
    {
        "en", "af", "ar", "bg", "cs", "da", "de", "el-GR", "es", "et", "fa", "fi", "fr", "he", "hr", "hu", "is", "it",
        "ja", "ko", "mk", "nl", "nl-BE", "no", "pl", "pt-BR", "ru", "sk", "sl", "sq", "sv", "th", "tr", "uk", "vi-VN",
        "zh-CN", "zh-TW",
    }.Select(CultureInfo.GetCultureInfo).ToList();

    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        if (httpContext.RequestServices.GetRequiredService<RequestBlogContext>() is not { HasBlog: true })
        {
            return null;
        }

        var culture = (await httpContext.RequestServices.GetRequiredService<ISettingsService>().GetAsync()).Culture;
        return string.IsNullOrWhiteSpace(culture) || culture.Equals(AutoCulture, StringComparison.OrdinalIgnoreCase)
            ? null
            : new ProviderCultureResult(culture);
    }
}
