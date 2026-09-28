using System;

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
    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=cadence_edc;Username=cadence;Password=cadence_password";

    public FileshareDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") is { Length: > 0 } configured
            ? configured
            : DefaultDesignTimeConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<FileshareDbContext>().UseNpgsql(connectionString);

        return new FileshareDbContext(optionsBuilder.Options);
    }
}
