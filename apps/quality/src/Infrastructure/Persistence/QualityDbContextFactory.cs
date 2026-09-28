using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Quality.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct a <see cref="QualityDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class QualityDbContextFactory : IDesignTimeDbContextFactory<QualityDbContext>
{
    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=cadence_password";

    public QualityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } configured
            ? configured
            : DefaultDesignTimeConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<QualityDbContext>().UseNpgsql(connectionString);

        return new QualityDbContext(optionsBuilder.Options);
    }
}
