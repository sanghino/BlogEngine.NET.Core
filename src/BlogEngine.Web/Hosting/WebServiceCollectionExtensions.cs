using System.Threading.RateLimiting;
using BlogEngine.Core;
using BlogEngine.Core.Configuration;
using BlogEngine.Services;
using BlogEngine.Web.Security;
using BlogEngine.Web.Setup;
using BlogEngine.Web.Extensions;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;

namespace BlogEngine.Web.Hosting;

public static class WebServiceCollectionExtensions
{
    /// <summary>Limite di tentativi per login, registrazione, recupero password e configurazione.</summary>
    public const string AccountRateLimit = "account";

    public static IServiceCollection AddBlogEngineWeb(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.Configure<BlogEngineOptions>(configuration.GetSection(BlogEngineOptions.SectionName));

        // Le estensioni vanno compilate prima di creare il contenitore dei servizi, in cui registrano i loro componenti.
        var options = configuration.GetSection(BlogEngineOptions.SectionName).Get<BlogEngineOptions>() ?? new BlogEngineOptions();
        services.AddBlogEngineExtensions(new BlogEnginePaths(environment.ContentRootPath, options), configuration["BlogEngine:ExtensionCachePath"]);
        services.AddSingleton(sp => new BlogEnginePaths(
            sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
            sp.GetRequiredService<IOptions<BlogEngineOptions>>().Value));

        // Su un hosting condiviso non c'è un profilo utente in cui salvare le chiavi: senza questa
        // configurazione resterebbero in memoria e ogni riavvio disconnetterebbe tutti gli utenti.
        services.AddDataProtection().SetApplicationName("BlogEngine");
        services.AddOptions<KeyManagementOptions>()
            .Configure<BlogEnginePaths, ILoggerFactory>((options, paths, loggerFactory) =>
                options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(paths.Keys), loggerFactory));

        services.AddBlogEngineCore();
        services.AddBlogEngineStorage();
        services.AddSingleton<InstallationState>();
        services.AddSingleton<SetupService>();
        services.AddSingleton<PasswordResetTokens>();
        services.AddScoped<RequestBlogContext>();
        services.AddScoped<IBlogContext>(sp => sp.GetRequiredService<RequestBlogContext>());

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = ".BlogEngine.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = AccountSignIn.PersistentLifetime;
                options.SlidingExpiration = true;
                options.LoginPath = "/account/login";
                options.LogoutPath = "/account/logout";
                options.AccessDeniedPath = "/account/login";
            });
        services.AddAuthorization();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AccountRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            // Commenti, voti, contatti e newsletter dalle pagine pubbliche.
            options.AddPolicy(Public.BlogActionsController.FormsRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en");
            options.SupportedCultures = BlogCultureProvider.SupportedCultures.ToList();
            options.SupportedUICultures = BlogCultureProvider.SupportedCultures.ToList();
            options.RequestCultureProviders.Insert(0, new BlogCultureProvider());
        });

        services.AddRazorPages();

        // Le pagine Razor scrivono le lettere accentate così come sono (non come &#xE0;), come i temi.
        services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
            options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

        // API dell'amministrazione AngularJS: JSON con Newtonsoft e nomi delle proprietà invariati (PascalCase),
        // come la Web API 2 originale, così il JavaScript esistente riceve e invia esattamente lo stesso formato.
        services.AddControllers().AddNewtonsoftJson(options =>
        {
            options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();

            // Come la Web API originale: "Id": "" e gli elenchi inviati come "" sono valori vuoti, non errori.
            options.SerializerSettings.Converters.Add(new Api.LenientGuidConverter());
            options.SerializerSettings.Converters.Add(new Api.LenientListConverter());
        });
        services.AddHttpClient();

        services.AddScoped<Api.AdminMapper>();
        services.AddScoped<Api.TrashService>();
        services.AddScoped<Admin.AdminViewContext>();
        services.AddSingleton<ThemeCatalog>();

        // Temi Liquid, widget e pagine pubbliche.
        services.AddHttpContextAccessor();
        services.AddSingleton<Themes.ThemeEngine>();
        services.AddSingleton<Themes.WidgetCatalog>();
        services.AddScoped<Themes.WidgetService>();
        services.AddScoped<Themes.SiteRenderer>();
        services.AddScoped<Themes.PublicContent>();
        services.AddBuiltInWidgets();

        // Servizi per le estensioni: risorse della pagina, email in background, riavvio quando cambiano.
        services.AddScoped<PageResources>();
        services.AddScoped<IPageResources>(sp => sp.GetRequiredService<PageResources>());
        services.AddScoped<CaptchaService>();
        services.AddScoped<ILabels, LabelsAdapter>();
        services.AddSingleton<EmailChannel>();
        services.AddScoped<IEmailQueue, BackgroundEmailQueue>();
        services.AddHostedService<EmailSenderService>();
        services.AddHostedService<ExtensionWatcher>();
        services.AddSingleton<FileLog>();
        services.AddSingleton<ILoggerProvider, FileLoggerProvider>();

        return services;
    }
}
