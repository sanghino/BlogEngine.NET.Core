using System.Text.Json;
using BlogEngine.Core.Data;
using BlogEngine.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BlogEngine.Data.Sqlite;

/// <summary>
/// Schema del database SQLite. Ogni modifica va accompagnata da una nuova migrazione
/// (<c>dotnet ef migrations add NomeModifica --project BlogEngine.Data.Sqlite</c>).
/// </summary>
public sealed class BlogDbContext(DbContextOptions<BlogDbContext> options) : DbContext(options)
{
    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<Post> Posts => Set<Post>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Page> Pages => Set<Page>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<BlogRollItem> BlogRoll => Set<BlogRollItem>();

    public DbSet<Referrer> Referrers => Set<Referrer>();

    public DbSet<AuthorProfile> Profiles => Set<AuthorProfile>();

    public DbSet<CustomField> CustomFields => Set<CustomField>();

    public DbSet<BlogUser> Users => Set<BlogUser>();

    public DbSet<BlogRole> Roles => Set<BlogRole>();

    public DbSet<SettingRow> Settings => Set<SettingRow>();

    public DbSet<RightRow> Rights => Set<RightRow>();

    public DbSet<StringListRow> StringLists => Set<StringListRow>();

    public DbSet<ExtensionDataRow> ExtensionData => Set<ExtensionDataRow>();

    public DbSet<CommentFilterRow> CommentFilters => Set<CommentFilterRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite non conserva il tipo della data: tutte le date sono in UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<Uri>().HaveConversion<UriConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>(b => b.HasKey(x => x.Id));

        modelBuilder.Entity<Post>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.BlogId);
            b.Ignore(x => x.Comments);
            b.Ignore(x => x.NotApprovedComments);
            b.Ignore(x => x.Pingbacks);
            b.Ignore(x => x.SpamComments);
            b.Ignore(x => x.DeletedComments);
            JsonList(b.Property(x => x.Categories));
            JsonList(b.Property(x => x.Tags));
            JsonList(b.Property(x => x.NotificationEmails));
            b.HasMany(x => x.AllComments).WithOne().HasForeignKey(c => c.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Comment>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.PostId);
        });

        modelBuilder.Entity<Page>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.BlogId);
        });

        modelBuilder.Entity<Category>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.BlogId);
        });

        modelBuilder.Entity<BlogRollItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.BlogId);
        });

        modelBuilder.Entity<Referrer>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.BlogId, x.Day });
        });

        modelBuilder.Entity<AuthorProfile>(b =>
        {
            b.HasKey(x => new { x.BlogId, x.UserName });
            b.Property(x => x.UserName).UseCollation("NOCASE");
        });

        modelBuilder.Entity<CustomField>(b =>
        {
            b.HasKey(x => new { x.BlogId, x.CustomType, x.ObjectId, x.Key });
            b.Property(x => x.CustomType).UseCollation("NOCASE");
            b.Property(x => x.ObjectId).UseCollation("NOCASE");
            b.Property(x => x.Key).UseCollation("NOCASE");
        });

        modelBuilder.Entity<BlogUser>(b =>
        {
            b.HasKey(x => new { x.BlogId, x.UserName });
            b.Property(x => x.UserName).UseCollation("NOCASE");
        });

        modelBuilder.Entity<BlogRole>(b =>
        {
            b.HasKey(x => new { x.BlogId, x.Name });
            b.Property(x => x.Name).UseCollation("NOCASE");
            JsonList(b.Property(x => x.Users));
        });

        modelBuilder.Entity<SettingRow>(b =>
        {
            b.HasKey(x => new { x.BlogId, x.Name });
            b.Property(x => x.Name).UseCollation("NOCASE");
        });

        modelBuilder.Entity<RightRow>(b => b.HasKey(x => new { x.BlogId, x.Right, x.Role }));
        modelBuilder.Entity<StringListRow>(b => b.HasKey(x => new { x.BlogId, x.Kind, x.Position }));
        modelBuilder.Entity<ExtensionDataRow>(b => b.HasKey(x => new { x.BlogId, x.Kind, x.Id }));

        modelBuilder.Entity<CommentFilterRow>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.BlogId);
        });

        // Gli id sono sempre generati dal codice: un'entità con l'id valorizzato può essere nuova.
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(e => e.FindPrimaryKey()?.Properties ?? [])
                     .Where(p => p.ClrType == typeof(Guid)))
        {
            property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }

    /// <summary>Liste di valori semplici salvate come array JSON in una colonna di testo.</summary>
    private static void JsonList<T>(PropertyBuilder<List<T>> property) =>
        property.HasConversion(
            list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
            json => string.IsNullOrEmpty(json) ? new List<T>() : JsonSerializer.Deserialize<List<T>>(json, (JsonSerializerOptions?)null) ?? new List<T>(),
            new ValueComparer<List<T>>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                list => list.ToList()));

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    private sealed class UriConverter() : ValueConverter<Uri, string>(
        v => v.ToString(),
        v => new Uri(v, UriKind.RelativeOrAbsolute));
}

// Righe delle tabelle che non corrispondono direttamente a un'entità del dominio.

public sealed class SettingRow
{
    public Guid BlogId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public sealed class RightRow
{
    public Guid BlogId { get; set; }

    public string Right { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}

public sealed class StringListRow
{
    public Guid BlogId { get; set; }

    public StringListKind Kind { get; set; }

    public int Position { get; set; }

    public string Value { get; set; } = string.Empty;
}

public sealed class ExtensionDataRow
{
    public Guid BlogId { get; set; }

    public ExtensionDataKind Kind { get; set; }

    public string Id { get; set; } = string.Empty;

    public string Data { get; set; } = string.Empty;
}

public sealed class CommentFilterRow
{
    public Guid Id { get; set; }

    public Guid BlogId { get; set; }

    public int Position { get; set; }

    public CommentFilterAction Action { get; set; }

    public CommentFilterSubject Subject { get; set; }

    public CommentFilterOperator Operator { get; set; }

    public string Filter { get; set; } = string.Empty;
}
