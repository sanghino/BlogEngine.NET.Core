using BlogEngine.Core.Caching;
using BlogEngine.Core.Events;
using BlogEngine.Core.Services;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogEngine.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra i servizi del Core. Vanno registrati a parte un provider dati (store) e un
    /// <see cref="IBlogContext"/> con durata scoped (nel sito web: uno per richiesta).
    /// </summary>
    public static IServiceCollection AddBlogEngineCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Cache condivise tra tutte le richieste. Sono registrate anche come IBlogScopedCache
        // così l'eliminazione di un blog le svuota tutte.
        AddCache<EntityRepository<Post>>(services);
        AddCache<EntityRepository<Page>>(services);
        AddCache<EntityRepository<Category>>(services);
        AddCache<EntityRepository<BlogRollItem>>(services);
        AddCache<EntityRepository<Referrer>>(services);
        AddCache<ProfileRepository>(services);
        AddCache<CustomFieldRepository>(services);
        AddCache<SettingsCache>(services);
        AddCache<RightsCache>(services);
        services.TryAddSingleton<BlogsCache>();
        services.TryAddSingleton<PostStateTracker>();

        // Servizi legati al contesto della richiesta (blog e utente correnti).
        services.TryAddScoped<IEventPublisher, EventPublisher>();
        services.TryAddScoped<IBlogClock, BlogClock>();
        services.TryAddScoped<ISettingsService, SettingsService>();
        services.TryAddScoped<IRightsService, RightsService>();
        services.TryAddScoped<IBlogService, BlogService>();
        services.TryAddScoped<IPostService, PostService>();
        services.TryAddScoped<ICommentService, CommentService>();
        services.TryAddScoped<IPageService, PageService>();
        services.TryAddScoped<ICategoryService, CategoryService>();
        services.TryAddScoped<IProfileService, ProfileService>();
        services.TryAddScoped<IBlogRollService, BlogRollService>();
        services.TryAddScoped<ICustomFieldService, CustomFieldService>();
        services.TryAddScoped<IReferrerService, ReferrerService>();
        services.TryAddScoped<ILinkGenerator, LinkGenerator>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler<CommentChangingEvent>, CommentModerationHandler>());

        return services;
    }

    private static void AddCache<TCache>(IServiceCollection services)
        where TCache : class, IBlogScopedCache
    {
        services.TryAddSingleton<TCache>();
        services.AddSingleton<IBlogScopedCache>(sp => sp.GetRequiredService<TCache>());
    }
}
