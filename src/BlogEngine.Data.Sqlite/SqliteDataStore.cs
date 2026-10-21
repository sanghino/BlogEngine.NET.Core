using BlogEngine.Core.Data;
using BlogEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Data.Sqlite;

/// <summary>
/// Provider dati su database SQLite. Ogni operazione usa un proprio DbContext di breve durata:
/// gli oggetti del dominio non restano tracciati da EF dopo il salvataggio.
/// </summary>
public sealed class SqliteDataStore(IDbContextFactory<BlogDbContext> factory) :
    IDataStoreInitializer,
    IBlogStore,
    IEntityStore<Post>,
    IEntityStore<Page>,
    IEntityStore<Category>,
    IEntityStore<BlogRollItem>,
    IEntityStore<Referrer>,
    IProfileStore,
    ISettingsStore,
    IRightsStore,
    ICustomFieldStore,
    IStringListStore,
    IExtensionDataStore,
    IUserStore,
    IRoleStore,
    ICommentFilterStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        // EF Core attiva il journal WAL quando crea il file: si torna al journal classico, che non
        // richiede memoria condivisa e funziona anche sulle cartelle di rete degli hosting condivisi.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE;", cancellationToken).ConfigureAwait(false);
    }

    // ---- Blog ----

    async Task<IReadOnlyList<Blog>> IBlogStore.GetAllAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Blogs.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    Task IBlogStore.SaveAsync(Blog blog, CancellationToken cancellationToken) => UpsertAsync(blog, [blog.Id], cancellationToken);

    async Task IBlogStore.DeleteAsync(Guid blogId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var postIds = db.Posts.Where(p => p.BlogId == blogId).Select(p => p.Id);
        await db.Comments.Where(c => postIds.Contains(c.PostId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Posts.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Pages.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Categories.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.BlogRoll.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Referrers.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Profiles.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.CustomFields.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Users.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Roles.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Settings.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Rights.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.StringLists.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.ExtensionData.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.CommentFilters.Where(x => x.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Blogs.Where(x => x.Id == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Post (con i commenti) ----

    async Task<IReadOnlyList<Post>> IEntityStore<Post>.GetAllAsync(Guid blogId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Posts.AsNoTracking().AsSplitQuery()
            .Include(p => p.AllComments)
            .Where(p => p.BlogId == blogId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task IEntityStore<Post>.SaveAsync(Post entity, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.Posts.Include(p => p.AllComments).FirstOrDefaultAsync(p => p.Id == entity.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.Posts.Add(Clone(entity));
        }
        else
        {
            CopyNonKeyValues(db, existing, entity);

            // Allinea i commenti: aggiorna quelli esistenti, aggiunge i nuovi, elimina quelli rimossi.
            var incoming = entity.AllComments.ToDictionary(c => c.Id);
            foreach (var stored in existing.AllComments.ToList())
            {
                if (incoming.Remove(stored.Id, out var updated))
                {
                    CopyNonKeyValues(db, stored, updated);
                }
                else
                {
                    db.Comments.Remove(stored);
                }
            }

            // Add esplicito: un commento con l'id già valorizzato verrebbe altrimenti considerato esistente.
            foreach (var added in incoming.Values)
            {
                var copy = Clone(added);
                copy.PostId = existing.Id;
                db.Comments.Add(copy);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task IEntityStore<Post>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Comments.Where(c => c.PostId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await db.Posts.Where(p => p.Id == id && p.BlogId == blogId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Pagine, categorie, blogroll, referrer ----

    Task<IReadOnlyList<Page>> IEntityStore<Page>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => ListAsync<Page>(x => x.BlogId == blogId, cancellationToken);

    Task IEntityStore<Page>.SaveAsync(Page entity, CancellationToken cancellationToken) => UpsertAsync(entity, [entity.Id], cancellationToken);

    Task IEntityStore<Page>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) => DeleteAsync<Page>(x => x.BlogId == blogId && x.Id == id, cancellationToken);

    Task<IReadOnlyList<Category>> IEntityStore<Category>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => ListAsync<Category>(x => x.BlogId == blogId, cancellationToken);

    Task IEntityStore<Category>.SaveAsync(Category entity, CancellationToken cancellationToken) => UpsertAsync(entity, [entity.Id], cancellationToken);

    Task IEntityStore<Category>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) => DeleteAsync<Category>(x => x.BlogId == blogId && x.Id == id, cancellationToken);

    Task<IReadOnlyList<BlogRollItem>> IEntityStore<BlogRollItem>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => ListAsync<BlogRollItem>(x => x.BlogId == blogId, cancellationToken);

    Task IEntityStore<BlogRollItem>.SaveAsync(BlogRollItem entity, CancellationToken cancellationToken) => UpsertAsync(entity, [entity.Id], cancellationToken);

    Task IEntityStore<BlogRollItem>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) => DeleteAsync<BlogRollItem>(x => x.BlogId == blogId && x.Id == id, cancellationToken);

    Task<IReadOnlyList<Referrer>> IEntityStore<Referrer>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => ListAsync<Referrer>(x => x.BlogId == blogId, cancellationToken);

    Task IEntityStore<Referrer>.SaveAsync(Referrer entity, CancellationToken cancellationToken) => UpsertAsync(entity, [entity.Id], cancellationToken);

    Task IEntityStore<Referrer>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) => DeleteAsync<Referrer>(x => x.BlogId == blogId && x.Id == id, cancellationToken);

    // ---- Profili, campi personalizzati, utenti e ruoli ----

    Task<IReadOnlyList<AuthorProfile>> IProfileStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => ListAsync<AuthorProfile>(x => x.BlogId == blogId, cancellationToken);

    Task IProfileStore.SaveAsync(AuthorProfile profile, CancellationToken cancellationToken) => UpsertAsync(profile, [profile.BlogId, profile.UserName], cancellationToken);

    Task IProfileStore.DeleteAsync(Guid blogId, string userName, CancellationToken cancellationToken) =>
        DeleteAsync<AuthorProfile>(x => x.BlogId == blogId && x.UserName == userName, cancellationToken);

    Task<IReadOnlyList<CustomField>> ICustomFieldStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => ListAsync<CustomField>(x => x.BlogId == blogId, cancellationToken);

    Task ICustomFieldStore.SaveAsync(CustomField field, CancellationToken cancellationToken) =>
        UpsertAsync(field, [field.BlogId, field.CustomType, field.ObjectId, field.Key], cancellationToken);

    Task ICustomFieldStore.DeleteAsync(CustomField field, CancellationToken cancellationToken) =>
        DeleteAsync<CustomField>(x => x.BlogId == field.BlogId && x.CustomType == field.CustomType && x.ObjectId == field.ObjectId && x.Key == field.Key, cancellationToken);

    Task<IReadOnlyList<BlogUser>> IUserStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => ListAsync<BlogUser>(x => x.BlogId == blogId, cancellationToken);

    Task IUserStore.SaveAsync(BlogUser user, CancellationToken cancellationToken) => UpsertAsync(user, [user.BlogId, user.UserName], cancellationToken);

    Task IUserStore.DeleteAsync(Guid blogId, string userName, CancellationToken cancellationToken) =>
        DeleteAsync<BlogUser>(x => x.BlogId == blogId && x.UserName == userName, cancellationToken);

    Task<IReadOnlyList<BlogRole>> IRoleStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => ListAsync<BlogRole>(x => x.BlogId == blogId, cancellationToken);

    Task IRoleStore.SaveAsync(BlogRole role, CancellationToken cancellationToken) => UpsertAsync(role, [role.BlogId, role.Name], cancellationToken);

    Task IRoleStore.DeleteAsync(Guid blogId, string roleName, CancellationToken cancellationToken) =>
        DeleteAsync<BlogRole>(x => x.BlogId == blogId && x.Name == roleName, cancellationToken);

    // ---- Impostazioni, permessi, elenchi, regole: l'insieme del blog viene sostituito ----

    async Task<IReadOnlyDictionary<string, string>> ISettingsStore.LoadAsync(Guid blogId, CancellationToken cancellationToken) =>
        (await ListAsync<SettingRow>(x => x.BlogId == blogId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);

    Task ISettingsStore.SaveAsync(Guid blogId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
        ReplaceAsync(x => x.BlogId == blogId, settings.Select(s => new SettingRow { BlogId = blogId, Name = s.Key, Value = s.Value }), cancellationToken);

    async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> IRightsStore.LoadAsync(Guid blogId, CancellationToken cancellationToken) =>
        (await ListAsync<RightRow>(x => x.BlogId == blogId, cancellationToken).ConfigureAwait(false))
            .GroupBy(x => x.Right)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(x => x.Role).ToList());

    Task IRightsStore.SaveAsync(Guid blogId, IReadOnlyDictionary<string, IReadOnlyCollection<string>> rights, CancellationToken cancellationToken) =>
        ReplaceAsync(x => x.BlogId == blogId,
            rights.SelectMany(r => r.Value.Distinct(StringComparer.OrdinalIgnoreCase).Select(role => new RightRow { BlogId = blogId, Right = r.Key, Role = role })),
            cancellationToken);

    async Task<IReadOnlyList<string>> IStringListStore.LoadAsync(Guid blogId, StringListKind kind, CancellationToken cancellationToken) =>
        (await ListAsync<StringListRow>(x => x.BlogId == blogId && x.Kind == kind, cancellationToken).ConfigureAwait(false))
            .OrderBy(x => x.Position)
            .Select(x => x.Value)
            .ToList();

    Task IStringListStore.SaveAsync(Guid blogId, StringListKind kind, IReadOnlyList<string> values, CancellationToken cancellationToken) =>
        ReplaceAsync(x => x.BlogId == blogId && x.Kind == kind,
            values.Select((v, i) => new StringListRow { BlogId = blogId, Kind = kind, Position = i, Value = v }),
            cancellationToken);

    async Task<IReadOnlyList<CommentFilterRule>> ICommentFilterStore.GetRulesAsync(Guid blogId, CancellationToken cancellationToken) =>
        (await ListAsync<CommentFilterRow>(x => x.BlogId == blogId, cancellationToken).ConfigureAwait(false))
            .OrderBy(x => x.Position)
            .Select(x => new CommentFilterRule { Id = x.Id, Action = x.Action, Subject = x.Subject, Operator = x.Operator, Filter = x.Filter })
            .ToList();

    Task ICommentFilterStore.SaveRulesAsync(Guid blogId, IReadOnlyList<CommentFilterRule> rules, CancellationToken cancellationToken) =>
        ReplaceAsync(x => x.BlogId == blogId,
            rules.Select((r, i) => new CommentFilterRow { Id = r.Id, BlogId = blogId, Position = i, Action = r.Action, Subject = r.Subject, Operator = r.Operator, Filter = r.Filter }),
            cancellationToken);

    // ---- Dati delle estensioni ----

    async Task<string?> IExtensionDataStore.LoadAsync(Guid blogId, ExtensionDataKind kind, string id, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return (await db.ExtensionData.FindAsync([blogId, kind, id], cancellationToken).ConfigureAwait(false))?.Data;
    }

    Task IExtensionDataStore.SaveAsync(Guid blogId, ExtensionDataKind kind, string id, string data, CancellationToken cancellationToken) =>
        UpsertAsync(new ExtensionDataRow { BlogId = blogId, Kind = kind, Id = id, Data = data }, [blogId, kind, id], cancellationToken);

    Task IExtensionDataStore.RemoveAsync(Guid blogId, ExtensionDataKind kind, string id, CancellationToken cancellationToken) =>
        DeleteAsync<ExtensionDataRow>(x => x.BlogId == blogId && x.Kind == kind && x.Id == id, cancellationToken);

    // ---- Operazioni comuni ----

    private async Task<IReadOnlyList<T>> ListAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
        where T : class
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<T>().AsNoTracking().Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Inserisce l'entità o ne copia i valori su quella memorizzata con la stessa chiave.</summary>
    private async Task UpsertAsync<T>(T entity, object[] key, CancellationToken cancellationToken)
        where T : class
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.Set<T>().FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.Add(entity);
        }
        else
        {
            CopyNonKeyValues(db, existing, entity);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copia sull'entità tracciata i valori di quella ricevuta, escluse le chiavi: le chiavi testuali
    /// non distinguono le maiuscole, e "mario" deve aggiornare la riga di "Mario" senza tentare di cambiarne la chiave.
    /// </summary>
    private static void CopyNonKeyValues<T>(BlogDbContext db, T tracked, T source)
        where T : class
    {
        var entry = db.Entry(tracked);
        var values = entry.CurrentValues.Clone();
        values.SetValues(source);
        foreach (var property in entry.Metadata.GetProperties().Where(p => !p.IsPrimaryKey()))
        {
            entry.Property(property.Name).CurrentValue = values[property];
        }
    }

    private async Task DeleteAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
        where T : class
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<T>().Where(predicate).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaceAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> scope, IEnumerable<T> rows, CancellationToken cancellationToken)
        where T : class
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<T>().Where(scope).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        db.Set<T>().AddRange(rows);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copia superficiale usata per gli inserimenti: EF non deve agganciare al proprio contesto gli
    /// oggetti condivisi della cache del Core.
    /// </summary>
    private static Post Clone(Post post)
    {
        var copy = new Post();
        CopyValues(post, copy);
        copy.Categories = [.. post.Categories];
        copy.Tags = [.. post.Tags];
        copy.NotificationEmails = [.. post.NotificationEmails];
        copy.AllComments = post.AllComments.Select(Clone).ToList();
        return copy;
    }

    private static Comment Clone(Comment comment)
    {
        var copy = new Comment();
        CopyValues(comment, copy);
        return copy;
    }

    private static void CopyValues<T>(T source, T target)
    {
        foreach (var property in typeof(T).GetProperties().Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
        {
            property.SetValue(target, property.GetValue(source));
        }
    }
}
