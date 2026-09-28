using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Interop.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct an <see cref="InteropDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class InteropDbContextFactory : IDesignTimeDbContextFactory<InteropDbContext>
{
    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=cadence_password";

    public InteropDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } configured
            ? configured
            : DefaultDesignTimeConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<InteropDbContext>().UseNpgsql(connectionString);

        return new InteropDbContext(optionsBuilder.Options);
    }
}
