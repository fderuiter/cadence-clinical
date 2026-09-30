using Cadence.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Eisf.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct an <see cref="EisfDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class EisfDbContextFactory : IDesignTimeDbContextFactory<EisfDbContext>
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "eisf";

    public EisfDbContext CreateDbContext(string[] args)
    {
        var connectionString = CadenceDatabaseExtensions.ResolveDesignTimeConnectionString(args);

        var optionsBuilder = new DbContextOptionsBuilder<EisfDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, Schema));

        return new EisfDbContext(optionsBuilder.Options);
    }
}
