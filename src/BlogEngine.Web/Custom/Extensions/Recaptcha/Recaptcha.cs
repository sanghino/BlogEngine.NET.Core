// Google reCAPTCHA v2 ("Non sono un robot") per i commenti e, se attivato nelle impostazioni dei
// contatti, per il modulo di contatto. Le chiavi si creano su https://www.google.com/recaptcha/admin.
// Porting dell'estensione Recaptcha (Bloodforge.com): la versione 1 usata in origine non esiste più.

using System.Net;
using System.Net.Http;
using System.Text.Json;

[Extension("Settings for the Recaptcha control", "4.0", "<a href=\"http://www.bloodforge.com\">Bloodforge.com</a>", EnabledByDefault = false)]
public sealed class Recaptcha : IBlogExtension
{
    public const string ResponseField = "g-recaptcha-response";

    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<ICaptchaProvider, RecaptchaProvider>();

    public IEnumerable<ExtensionSettingsDefinition> Settings =>
    [
        new ExtensionSettingsDefinition(nameof(Recaptcha))
        {
            IsScalar = true,
            Help = "Create your site key and secret key (reCAPTCHA v2, checkbox) at <a href=\"https://www.google.com/recaptcha/admin\" target=\"_blank\">google.com/recaptcha</a>. Remember to <span style=\"color:red\">enable the extension</span> for reCAPTCHA to show up on the comments form.",
        }
            .AddParameter("PublicKey", "Site Key", 100, required: true)
            .AddParameter("PrivateKey", "Secret Key", 100, required: true)
            .AddParameter("ShowForAuthenticatedUsers", "Show Captcha For Authenticated Users", 1, type: ExtensionParameterType.Boolean)
            .AddParameter("Theme", "Theme", 20, type: ExtensionParameterType.DropDown, options: ["light", "dark"])
            .AddParameter("Language", "Language", 5, type: ExtensionParameterType.DropDown, options: ["en|English", "it|Italiano", "nl|Dutch", "fr|French", "de|German", "pt|Portuguese", "ru|Russian", "es|Spanish", "tr|Turkish"])
            .SetDefault("PublicKey", "YOURPUBLICKEY")
            .SetDefault("PrivateKey", "YOURPRIVATEKEY")
            .SetDefault("ShowForAuthenticatedUsers", false)
            .SetDefault("Theme", "light")
            .SetDefault("Language", "en"),
    ];
}

public sealed class RecaptchaProvider(
    IExtensionSettingsService settings,
    ISettingsService blogSettings,
    IHttpClientFactory httpClients,
    ILogger<RecaptchaProvider> logger) : ICaptchaProvider
{
    public string Name => nameof(Recaptcha);

    public async Task<string?> RenderAsync(CaptchaContext context, CancellationToken cancellationToken)
    {
        if (await RequiredSettingsAsync(context, cancellationToken) is not { } values)
        {
            return null;
        }

        static string E(string value) => WebUtility.HtmlEncode(value);
        return $"""
            <div class="g-recaptcha" data-sitekey="{E(values.Get("PublicKey"))}" data-theme="{E(values.Get("Theme", "light"))}"></div>
            <script src="https://www.google.com/recaptcha/api.js?hl={Uri.EscapeDataString(values.Get("Language", "en"))}" async defer></script>
            """;
    }

    public async Task<bool> ValidateAsync(CaptchaContext context, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        if (await RequiredSettingsAsync(context, cancellationToken) is not { } values)
        {
            return true;
        }

        var answer = fields.GetValueOrDefault(Recaptcha.ResponseField);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        try
        {
            var client = httpClients.CreateClient(nameof(Recaptcha));
            client.Timeout = TimeSpan.FromSeconds(15);
            using var response = await client.PostAsync("https://www.google.com/recaptcha/api/siteverify", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = values.Get("PrivateKey"),
                ["response"] = answer,
                ["remoteip"] = context.RemoteIp,
            }), cancellationToken);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("success", out var success) && success.GetBoolean();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Come in origine: se Google non risponde il commento non viene accettato.
            logger.LogWarning("reCAPTCHA non raggiungibile: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>Impostazioni, oppure null se il captcha non va chiesto.</summary>
    private async Task<ExtensionSettingsValues?> RequiredSettingsAsync(CaptchaContext context, CancellationToken cancellationToken)
    {
        var values = await settings.GetAsync(nameof(Recaptcha), cancellationToken: cancellationToken);
        if (values.Get("PublicKey") is "" or "YOURPUBLICKEY" || values.Get("PrivateKey") is "" or "YOURPRIVATEKEY")
        {
            return null;
        }

        if (context.IsAuthenticated && !values.GetBoolean("ShowForAuthenticatedUsers"))
        {
            return null;
        }

        if (context.Form == CaptchaForm.Contact && !(await blogSettings.GetAsync(cancellationToken: cancellationToken)).EnableRecaptchaOnContactForm)
        {
            return null;
        }

        return values;
    }
}
