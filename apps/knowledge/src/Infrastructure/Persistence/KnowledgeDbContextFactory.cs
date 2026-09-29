using Cadence.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="KnowledgeDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class KnowledgeDbContextFactory : IDesignTimeDbContextFactory<KnowledgeDbContext>
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "knowledge";

    public KnowledgeDbContext CreateDbContext(string[] args)
    {
        var connectionString = CadenceDatabaseExtensions.ResolveDesignTimeConnectionString(args);

        var optionsBuilder = new DbContextOptionsBuilder<KnowledgeDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, Schema));

        return new KnowledgeDbContext(optionsBuilder.Options);
    }
}
