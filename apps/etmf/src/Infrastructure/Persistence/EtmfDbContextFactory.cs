using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Etmf.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI (<c>migrations add</c>,
/// <c>database update</c>, <c>migrations script</c>, ...) construct an <see cref="EtmfDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class EtmfDbContextFactory : IDesignTimeDbContextFactory<EtmfDbContext>
{
    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=cadence_password";

    public EtmfDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } configured
            ? configured
            : DefaultDesignTimeConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<EtmfDbContext>().UseNpgsql(connectionString);

        return new EtmfDbContext(optionsBuilder.Options);
    }
}
