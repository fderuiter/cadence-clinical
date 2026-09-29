using System;

using Cadence.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Payments.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct a <see cref="PaymentsDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    /// <summary>This module's PostgreSQL schema in the shared database (ADR-2247).</summary>
    public const string Schema = "payments";

    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=cadence_password";

    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } configured
            ? configured
            : DefaultDesignTimeConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, Schema));

        return new PaymentsDbContext(optionsBuilder.Options);
    }
}
