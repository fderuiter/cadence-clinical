using System;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

namespace Cadence.Database;

public static partial class CadenceDatabaseExtensions
{
    /// <summary>Connection string name used when a caller does not pass one (matches the Aspire AppHost's database resource).</summary>
    public const string DefaultConnectionName = "cadence";

    /// <summary>Legacy flat configuration key still honoured after <c>ConnectionStrings:{name}</c>.</summary>
    public const string LegacyConnectionStringKey = "POSTGRES_CONNECTION_STRING";

    /// <summary>Health check tag for dependencies that must be reachable before a service takes traffic.</summary>
    public const string ReadinessTag = "ready";

    /// <summary>The single shared database every module uses (ADR-2247); each module owns one schema in it.</summary>
    public const string DefaultDatabaseName = "cadence";

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaNameRegex();

    /// <summary>
    /// Registers <typeparamref name="TDbContext"/> against PostgreSQL with transient-failure retries and a
    /// readiness health check, and applies migrations when <see cref="CadenceMigrationOptions"/> asks for it.
    /// The connection string is resolved by <see cref="ResolvePostgresConnectionString"/>.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="schema">The module's PostgreSQL schema; defaults to the context name without "DbContext", lower-cased.</param>
    /// <param name="connectionName">The <c>ConnectionStrings</c> entry to read.</param>
    public static WebApplicationBuilder AddCadencePostgresDbContext<TDbContext>(
        this WebApplicationBuilder builder,
        string? schema = null,
        string connectionName = DefaultConnectionName)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        string connectionString = WithModuleSchema(
            ResolvePostgresConnectionString(builder.Configuration, builder.Environment, DefaultDatabaseName, connectionName),
            schema ?? ModuleSchemaFor<TDbContext>());

        builder.Services.AddCadenceDbContext<TDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<TDbContext>(name: typeof(TDbContext).Name, tags: [ReadinessTag]);

        builder.Services.AddCadenceMigrations<TDbContext>(builder.Configuration);

        return builder;
    }

    /// <summary>
    /// Resolves the PostgreSQL connection string in this order: <c>ConnectionStrings:{connectionName}</c>,
    /// then <c>POSTGRES_CONNECTION_STRING</c>. In the Development environment only, falls back to the local
    /// docker-compose database; in any other environment a missing value fails startup instead of silently
    /// connecting to localhost with a well-known password.
    /// </summary>
    public static string ResolvePostgresConnectionString(
        IConfiguration configuration,
        IHostEnvironment environment,
        string defaultDatabaseName = DefaultDatabaseName,
        string connectionName = DefaultConnectionName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (configuration.GetConnectionString(connectionName) is { Length: > 0 } named)
        {
            return named;
        }

        if (configuration[LegacyConnectionStringKey] is { Length: > 0 } legacy)
        {
            return legacy;
        }

        if (environment.IsDevelopment())
        {
            return $"Host=localhost;Port=5432;Database={defaultDatabaseName};Username=cadence;Password=cadence_password";
        }

        throw new InvalidOperationException(
            $"No PostgreSQL connection string configured for '{environment.ApplicationName}'. " +
            $"Set ConnectionStrings__{connectionName} or {LegacyConnectionStringKey}.");
    }

    /// <summary>
    /// Scopes a connection to one module's schema by setting the Npgsql search path to <c>{schema},public</c>.
    /// Unqualified tables, including the migrations history table, are created and resolved in the module's
    /// schema, so modules sharing one database cannot collide on table names (ADR-2247). <c>public</c> stays on
    /// the path only so extension types and functions installed there still resolve.
    /// </summary>
    public static string WithModuleSchema(string connectionString, string schema)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(schema);

        if (!SchemaNameRegex().IsMatch(schema))
        {
            throw new ArgumentException($"'{schema}' is not a valid module schema name (lower-case letters, digits, underscore).", nameof(schema));
        }

        return new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = $"{schema},public" }.ConnectionString;
    }

    /// <summary>The default module schema for a context: <c>CtmsDbContext</c> becomes <c>ctms</c>.</summary>
    public static string ModuleSchemaFor<TDbContext>()
        where TDbContext : DbContext
    {
        string name = typeof(TDbContext).Name;
        if (name.EndsWith("DbContext", StringComparison.Ordinal))
        {
            name = name[..^"DbContext".Length];
        }

        return name.ToLowerInvariant();
    }
}
