using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Database;

public static class CadenceDatabaseExtensions
{
    public static WebApplicationBuilder AddCadencePostgresDbContext<TDbContext>(
        this WebApplicationBuilder builder,
        string defaultDatabaseName = "cadence_edc")
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = builder.Configuration["POSTGRES_CONNECTION_STRING"]
            is { Length: > 0 } configuredConnectionString
                ? configuredConnectionString
                : $"Host=localhost;Port=5432;Database={defaultDatabaseName};Username=cadence;Password=cadence_password";

        builder.Services.AddDbContext<TDbContext>(options => options.UseNpgsql(connectionString));

        return builder;
    }
}
