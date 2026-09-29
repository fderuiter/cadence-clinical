using Cadence.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Fileshare.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct a <see cref="FileshareDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class FileshareDbContextFactory : IDesignTimeDbContextFactory<FileshareDbContext>
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "fileshare";

    public FileshareDbContext CreateDbContext(string[] args)
    {
        var connectionString = CadenceDatabaseExtensions.ResolveDesignTimeConnectionString(args);

        var optionsBuilder = new DbContextOptionsBuilder<FileshareDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, Schema));

        return new FileshareDbContext(optionsBuilder.Options);
    }
}
