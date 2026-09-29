using System;

using Cadence.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Ctms.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct a <see cref="CtmsDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class CtmsDbContextFactory : IDesignTimeDbContextFactory<CtmsDbContext>
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "ctms";

    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=cadence_password";

    public CtmsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } configured
            ? configured
            : DefaultDesignTimeConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, Schema));

        return new CtmsDbContext(optionsBuilder.Options);
    }
}
