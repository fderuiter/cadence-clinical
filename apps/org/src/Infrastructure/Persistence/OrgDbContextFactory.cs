using Cadence.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Org.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct an <see cref="OrgDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class OrgDbContextFactory : IDesignTimeDbContextFactory<OrgDbContext>
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "org";

    public OrgDbContext CreateDbContext(string[] args)
    {
        var connectionString = CadenceDatabaseExtensions.ResolveDesignTimeConnectionString(args);

        var optionsBuilder = new DbContextOptionsBuilder<OrgDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, Schema));

        return new OrgDbContext(optionsBuilder.Options);
    }
}
