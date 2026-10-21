using System.Threading.RateLimiting;
using BlogEngine.Core;
using BlogEngine.Core.Configuration;
using BlogEngine.Services;
using BlogEngine.Web.Security;
using BlogEngine.Web.Setup;
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

    public static IServiceCollection AddBlogEngineWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BlogEngineOptions>(configuration.GetSection(BlogEngineOptions.SectionName));
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

        // API dell'amministrazione AngularJS: JSON con Newtonsoft e nomi delle proprietà invariati (PascalCase),
        // come la Web API 2 originale, così il JavaScript esistente riceve e invia esattamente lo stesso formato.
        services.AddControllers().AddNewtonsoftJson(options =>
            options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());
        services.AddHttpClient();

        services.AddScoped<Api.AdminMapper>();
        services.AddScoped<Api.TrashService>();
        services.AddScoped<Admin.AdminViewContext>();
        services.AddSingleton<ThemeCatalog>();
        services.AddSingleton<FileLog>();
        services.AddSingleton<ILoggerProvider, FileLoggerProvider>();

        return services;
    }
}
