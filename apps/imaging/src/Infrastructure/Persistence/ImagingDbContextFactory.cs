using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Apps.Imaging.Infrastructure.Persistence;

/// <summary>
/// Design-time-only factory that lets the <c>dotnet ef</c> CLI construct an <see cref="ImagingDbContext"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class ImagingDbContextFactory : IDesignTimeDbContextFactory<ImagingDbContext>
{
    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=cadence_password";

    public ImagingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } configured
            ? configured
            : DefaultDesignTimeConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<ImagingDbContext>().UseNpgsql(connectionString);

        return new ImagingDbContext(optionsBuilder.Options);
    }
}
