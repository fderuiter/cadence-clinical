using System;
using System.Text.RegularExpressions;

using Cadence.Database.Tenancy;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    private const string UnitTestPlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=cadence_test;Username=cadence_test";

    /// <summary>
    /// Registers <typeparamref name="TDbContext"/> against PostgreSQL with transient-failure retries,
    /// <c>cadence.tenant_id</c> session binding + SaveChanges tenant stamping (ADR-2250, K-ID-2),
    /// a readiness health check, and migrations when <see cref="CadenceMigrationOptions"/> asks for it.
    /// The connection string is resolved by <see cref="ResolvePostgresConnectionString"/>.
    /// </summary>
    public static WebApplicationBuilder AddCadencePostgresDbContext<TDbContext>(
        this WebApplicationBuilder builder,
        string? schema = null,
        string connectionName = DefaultConnectionName,
        Action<IServiceProvider, DbContextOptionsBuilder>? configureOptions = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        string baseConnectionString = ResolvePostgresConnectionString(builder.Configuration, builder.Environment, connectionName);
        string connectionString = WithModuleSchema(
            baseConnectionString,
            schema ?? ModuleSchemaFor<TDbContext>());

        if (string.Equals(baseConnectionString.Trim(), UnitTestPlaceholderConnectionString, StringComparison.Ordinal))
        {
            builder.Services.AddCadenceInMemoryTenancy();
        }
        else
        {
            builder.Services.AddCadencePostgresTenancy(baseConnectionString);
        }

        builder.Services.AddCadenceDbContext<TDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure());
            options.UseCadenceStandaloneTenancy(sp.GetService<ICurrentTenantAccessor>(), isRelational: true);
            configureOptions?.Invoke(sp, options);
            options.AddInterceptors(
                StandaloneTenantConnectionInterceptor.Instance,
                StandaloneTenantSaveChangesInterceptor.Instance);
        });

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<TDbContext>(name: typeof(TDbContext).Name, tags: [ReadinessTag]);

        builder.Services.AddCadenceMigrations<TDbContext>(builder.Configuration);

        return builder;
    }

    /// <summary>
    /// Registers PostgreSQL tenant code resolution (<c>kernel.tenant_id_by_code</c>) and scoped tenant accessors.
    /// </summary>
    public static IServiceCollection AddCadencePostgresTenancy(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddHttpContextAccessor();
        services.TryAddSingleton<ITenantCodeResolver>(_ => new PostgresTenantCodeResolver(connectionString));
        RegisterScopedTenantAccessors(services);
        return services;
    }

    /// <summary>
    /// Registers deterministic tenant code resolution and scoped tenant accessors for in-memory test hosts
    /// when PostgreSQL Row-Level Security is absent.
    /// </summary>
    public static IServiceCollection AddCadenceInMemoryTenancy(
        this IServiceCollection services,
        ITenantCodeResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.RemoveAll<ITenantCodeResolver>();
        services.AddSingleton<ITenantCodeResolver>(resolver ?? new DeterministicTenantCodeResolver());
        RegisterScopedTenantAccessors(services);
        return services;
    }

    private static void RegisterScopedTenantAccessors(IServiceCollection services)
    {
        services.TryAddSingleton<CurrentTenantAccessor>();
        services.TryAddSingleton<ICurrentTenantAccessor>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.TryAddSingleton<ITenantScope>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
        services.TryAddSingleton<ITenantAccessVerifier>(sp => sp.GetRequiredService<CurrentTenantAccessor>());
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

    /// <summary>Credential-free placeholder connection string for offline EF Core design-time model building (ADR-2260, ADR-2498).</summary>
    public const string DesignTimePlaceholderConnectionString = "Host=localhost;Database=cadence_design";

    /// <summary>
    /// Resolves the connection string for <c>IDesignTimeDbContextFactory</c> implementations (the
    /// <c>dotnet ef</c> tooling process, which has no host configuration): the value after
    /// <see cref="DesignTimeConnectionArgument"/> in <paramref name="args"/>, then
    /// <c>ConnectionStrings__cadence</c>, then <c>POSTGRES_CONNECTION_STRING</c>, otherwise the credential-free
    /// placeholder <see cref="DesignTimePlaceholderConnectionString"/> so offline model commands succeed without
    /// embedding credentials in source control (ADR-2260, ADR-2498).
    /// </summary>
    public static string ResolveDesignTimeConnectionString(string[]? args = null) =>
        ResolveDesignTimeConnectionStringCore(args, Environment.GetEnvironmentVariable);

    internal static string ResolveDesignTimeConnectionStringCore(string[]? args, Func<string, string?> getEnvironmentVariable)
    {
        if (args is not null)
        {
            int index = Array.IndexOf(args, DesignTimeConnectionArgument);
            if (index >= 0 && index + 1 < args.Length && args[index + 1] is { Length: > 0 } fromArgs)
            {
                return fromArgs;
            }
        }

        // Literal names preserved for EnvVarExtractorService:
        // Environment.GetEnvironmentVariable("ConnectionStrings__cadence")
        // Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
        if (getEnvironmentVariable("ConnectionStrings__cadence") is { Length: > 0 } named)
        {
            return named;
        }

        return getEnvironmentVariable(LegacyConnectionStringKey) is { Length: > 0 } configured
            ? configured
            : DesignTimePlaceholderConnectionString;
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
