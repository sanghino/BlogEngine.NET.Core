// Captcha semplice per i commenti: una domanda (es. "5+5 =") e la risposta attesa, scelte dall'amministratore.
// Porting dell'estensione SimpleCaptcha di Aaron Stannard.

using System.Net;

[Extension("Settings for the SimpleCaptcha control", "4.0", "<a href=\"http://www.aaronstannard.com\">Aaron Stannard</a>", Priority = 2)]
public sealed class SimpleCaptcha : IBlogExtension
{
    public const string AnswerField = "simpleCaptchaValue";
    public const int MaxCaptchaLength = 30;

    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<ICaptchaProvider, SimpleCaptchaProvider>();

    public IEnumerable<ExtensionSettingsDefinition> Settings =>
    [
        new ExtensionSettingsDefinition(nameof(SimpleCaptcha))
        {
            IsScalar = true,
            Help = "To get started with SimpleCaptcha, just provide some captcha instructions for your users in the <b>CaptchaLabel</b> field and the value you require from your users in order to post a comment in the <b>CaptchaAnswer</b> field.",
        }
            .AddParameter("CaptchaLabel", "Your captcha's label", 30, required: true)
            .AddParameter("CaptchaAnswer", "Your captcha's expected value", MaxCaptchaLength, required: true)
            .AddParameter("ShowForAuthenticatedUsers", "Show Captcha For Authenticated Users", 1, type: ExtensionParameterType.Boolean)
            .SetDefault("CaptchaLabel", "5+5 = ")
            .SetDefault("CaptchaAnswer", "10")
            .SetDefault("ShowForAuthenticatedUsers", false),
    ];
}

public sealed class SimpleCaptchaProvider(IExtensionSettingsService settings, ILabels labels) : ICaptchaProvider
{
    public string Name => nameof(SimpleCaptcha);

    public async Task<string?> RenderAsync(CaptchaContext context, CancellationToken cancellationToken)
    {
        var values = await RequiredSettingsAsync(context, cancellationToken);
        if (values is null)
        {
            return null;
        }

        return $"""
            <p class="simple-captcha">
                <label for="{SimpleCaptcha.AnswerField}" style="margin-right: 5px;">{WebUtility.HtmlEncode(values.Get("CaptchaLabel"))}</label>
                <input type="text" id="{SimpleCaptcha.AnswerField}" name="{SimpleCaptcha.AnswerField}" maxlength="{SimpleCaptcha.MaxCaptchaLength}" value="" autocomplete="off" />
                <span id="spnSimpleCaptchaIncorrect" style="display:none; color:red">{WebUtility.HtmlEncode(labels["incorrectSimpleCaptcha"])}</span>
            </p>
            """;
    }

    public async Task<bool> ValidateAsync(CaptchaContext context, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var values = await RequiredSettingsAsync(context, cancellationToken);
        return values is null
            || string.Equals(fields.GetValueOrDefault(SimpleCaptcha.AnswerField)?.Trim(), values.Get("CaptchaAnswer").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Impostazioni, oppure null se il captcha non va chiesto (contatti, utenti autenticati, non configurato).</summary>
    private async Task<ExtensionSettingsValues?> RequiredSettingsAsync(CaptchaContext context, CancellationToken cancellationToken)
    {
        if (context.Form != CaptchaForm.Comment)
        {
            return null;
        }

        var values = await settings.GetAsync(nameof(SimpleCaptcha), cancellationToken: cancellationToken);
        if ((context.IsAuthenticated && !values.GetBoolean("ShowForAuthenticatedUsers")) || string.IsNullOrWhiteSpace(values.Get("CaptchaAnswer")))
        {
            return null;
        }

        return values;
    }
}
