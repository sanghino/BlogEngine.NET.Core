using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlogEngine.Data.Sqlite;

/// <summary>
/// Usata solo dagli strumenti "dotnet ef" per generare le migrazioni; il database indicato
/// non viene creato né usato dall'applicazione.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BlogDbContext>
{
    public BlogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BlogDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
