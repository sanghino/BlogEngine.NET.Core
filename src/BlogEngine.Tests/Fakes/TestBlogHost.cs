using BlogEngine.Core;
using BlogEngine.Core.Data;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Fakes;

/// <summary>Contesto modificabile dai test: blog, utente e ruoli correnti.</summary>
public sealed class TestBlogContext : IBlogContext
{
    public required Blog Blog { get; set; }

    public string? UserName { get; set; }

    public IReadOnlyCollection<string> Roles { get; set; } = [SystemRoles.Anonymous];

    public Uri? RequestBaseUri { get; set; } = new("https://localhost:5001/");
}

/// <summary>Orologio fermo, spostabile dai test.</summary>
public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Contenitore dei servizi del Core con store in memoria. Ogni istanza è un'installazione nuova
/// con il solo blog principale.
/// </summary>
public sealed class TestBlogHost : IDisposable
{
    public static readonly DateTimeOffset StartTime = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    /// <param name="configure">Registrazioni aggiuntive (gestori di eventi, filtri antispam...).</param>
    /// <param name="storage">
    /// Provider dati; se omesso si usa uno store in memoria. Passando la stessa istanza a un nuovo host
    /// si simula il riavvio dell'applicazione sugli stessi dati.
    /// </param>
    public TestBlogHost(Action<IServiceCollection>? configure = null, TestStorage? storage = null)
    {
        Stores = storage?.Memory ?? new InMemoryStores();
        Clock = new ManualTimeProvider(StartTime);
        Context = new TestBlogContext { Blog = new Blog() };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddBlogEngineCore();
        if (storage is null)
        {
            TestStorage.RegisterMemory(services, Stores);

            // Percorsi fittizi: con lo store in memoria nessuna cartella viene creata finché un test non scrive file.
            var dataRoot = Path.Combine(Path.GetTempPath(), "be-tests", Guid.NewGuid().ToString("N"));
            services.AddSingleton(new BlogEngine.Core.Configuration.BlogEnginePaths(dataRoot, new BlogEngine.Core.Configuration.BlogEngineOptions { DataPath = dataRoot }));
        }
        else
        {
            storage.Register(services);
        }

        configure?.Invoke(services);
        services.AddSingleton<IBlogContext>(Context);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _provider.GetRequiredService<IDataStoreInitializer>().InitializeAsync().GetAwaiter().GetResult();

        // Il blog principale viene creato alla prima apertura dei dati e ritrovato alle successive.
        var blogStore = _provider.GetRequiredService<IBlogStore>();
        PrimaryBlog = blogStore.GetAllAsync().GetAwaiter().GetResult().FirstOrDefault(b => b.IsPrimary)
            ?? new Blog { Name = "Primary", IsPrimary = true, VirtualPath = "~/" };
        blogStore.SaveAsync(PrimaryBlog).GetAwaiter().GetResult();
        Context.Blog = PrimaryBlog;

        _scope = _provider.CreateScope();
    }

    /// <summary>Store in memoria (vuoto e inutilizzato se si usa un provider XML o SQLite).</summary>
    public InMemoryStores Stores { get; }

    public ManualTimeProvider Clock { get; }

    public Blog PrimaryBlog { get; }

    public TestBlogContext Context { get; }

    public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public IPostService Posts => Get<IPostService>();

    public ICommentService Comments => Get<ICommentService>();

    public IPageService Pages => Get<IPageService>();

    public ICategoryService Categories => Get<ICategoryService>();

    public ISettingsService Settings => Get<ISettingsService>();

    public TestBlogHost AsAdmin(string userName = "admin") => As(userName, SystemRoles.Administrators);

    public TestBlogHost AsEditor(string userName = "editor") => As(userName, SystemRoles.Editors);

    public TestBlogHost AsAnonymous()
    {
        Context.UserName = null;
        Context.Roles = [SystemRoles.Anonymous];
        return this;
    }

    public TestBlogHost As(string userName, params string[] roles)
    {
        Context.UserName = userName;
        Context.Roles = roles;
        return this;
    }

    /// <summary>Crea e salva un post pubblicato come amministratore, poi ripristina l'utente precedente.</summary>
    public async Task<Post> CreatePostAsync(string title, Action<Post>? configure = null)
    {
        var (user, roles) = (Context.UserName, Context.Roles);
        AsAdmin();
        try
        {
            var post = new Post { Title = title, Content = $"<p>{title}</p>" };
            configure?.Invoke(post);
            await Posts.SaveAsync(post);
            return post;
        }
        finally
        {
            Context.UserName = user;
            Context.Roles = roles;
        }
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
