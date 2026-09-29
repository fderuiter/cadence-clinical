using Cadence.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="KernelDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
internal sealed class KernelDbContextDesignTimeFactory : IDesignTimeDbContextFactory<KernelDbContext>
{
    public KernelDbContext CreateDbContext(string[] args)
    {
        var connectionString = CadenceDatabaseExtensions.ResolveDesignTimeConnectionString(args);

        var options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", KernelDbContext.Schema))
            .Options;

        return new KernelDbContext(options);
    }
}
