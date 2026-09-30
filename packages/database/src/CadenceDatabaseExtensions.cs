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
            ResolvePostgresConnectionString(builder.Configuration, builder.Environment, connectionName),
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
    /// then <c>POSTGRES_CONNECTION_STRING</c>. There is no built-in fallback in any environment (including
    /// Development): a missing value fails startup with an <see cref="InvalidOperationException"/> naming both
    /// keys, instead of silently connecting with a well-known password committed to source control (ADR-2260).
    /// </summary>
    public static string ResolvePostgresConnectionString(
        IConfiguration configuration,
        IHostEnvironment environment,
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

        throw new InvalidOperationException(
            $"No PostgreSQL connection string configured for '{environment.ApplicationName}'. " +
            $"Set ConnectionStrings__{connectionName} or {LegacyConnectionStringKey}.");
    }

    /// <summary>Design-time argument that supplies the connection string: <c>dotnet ef ... -- --connection "..."</c>.</summary>
    public const string DesignTimeConnectionArgument = "--connection";

    /// <summary>
    /// Resolves the connection string for <c>IDesignTimeDbContextFactory</c> implementations (the
    /// <c>dotnet ef</c> tooling process, which has no host configuration): the value after
    /// <see cref="DesignTimeConnectionArgument"/> in <paramref name="args"/>, otherwise the
    /// <c>POSTGRES_CONNECTION_STRING</c> environment variable. Throws when neither is set rather than falling
    /// back to a hardcoded credential (ADR-2260).
    /// </summary>
    public static string ResolveDesignTimeConnectionString(string[]? args = null)
    {
        if (args is not null)
        {
            int index = Array.IndexOf(args, DesignTimeConnectionArgument);
            if (index >= 0 && index + 1 < args.Length && args[index + 1] is { Length: > 0 } fromArgs)
            {
                return fromArgs;
            }
        }

        return Environment.GetEnvironmentVariable(LegacyConnectionStringKey) is { Length: > 0 } configured
            ? configured
            : throw new InvalidOperationException(
                $"{LegacyConnectionStringKey} is not set. Export it, or pass {DesignTimeConnectionArgument} \"...\" " +
                "after -- on the dotnet ef command line.");
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
