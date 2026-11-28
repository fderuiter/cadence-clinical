using System;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;

using Cadence.Database.Audit;
using Cadence.Database.Concurrency;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    /// <summary>Primary environment variable binding <c>ConnectionStrings:cadence</c> across runtime hosts and design-time tools.</summary>
    public const string PrimaryConnectionStringEnvVar = "ConnectionStrings__cadence";

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
    /// shared <see cref="NpgsqlDataSource"/> pooling, scoped <see cref="DbContext"/> connection leasing,
    /// <c>cadence.tenant_id</c> session binding + SaveChanges tenant stamping (ADR-2250, K-ID-2),
    /// default 21 CFR Part 11 <see cref="GxPAuditInterceptor"/> hash-chained audit logging + outbox flushing,
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

        bool isPlaceholderUnitTestConnection = string.Equals(
            baseConnectionString.Trim(),
            UnitTestPlaceholderConnectionString,
            StringComparison.Ordinal);

        if (isPlaceholderUnitTestConnection)
        {
            builder.Services.AddCadenceInMemoryTenancy();
        }
        else
        {
            builder.Services.TryAddKeyedSingleton<NpgsqlDataSource>(
                typeof(TDbContext),
                (_, _) => NpgsqlDataSource.Create(connectionString));
            builder.Services.AddHttpContextAccessor();
            builder.Services.TryAddSingleton<ITenantCodeResolver>(sp =>
                new PostgresTenantCodeResolver(
                    sp.GetRequiredKeyedService<NpgsqlDataSource>(typeof(TDbContext)),
                    sp.GetService<IClock>(),
                    ownsDataSource: false));
            RegisterScopedTenantAccessors(builder.Services);
        }

        builder.Services.AddProblemDetails();
        builder.Services.TryAddSingleton<IClock>(SystemClock.Instance);
        builder.Services.TryAddScoped<IAuditActorScope, AmbientAuditActorScope>();
        builder.Services.TryAddScoped<StandaloneDbContextLeaseManager>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IExceptionHandler, StandaloneConcurrencyExceptionHandler>());

        builder.Services.AddCadenceDbContext<TDbContext>((sp, options) =>
        {
            var dataSource = sp.GetKeyedService<NpgsqlDataSource>(typeof(TDbContext));
            if (dataSource is not null)
            {
                options.UseNpgsql(dataSource, npgsql => npgsql.EnableRetryOnFailure());
            }
            else
            {
                options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure());
            }

            options.UseCadenceStandaloneTenancy(
                sp.GetService<ICurrentTenantAccessor>(),
                isRelational: true,
                leaseManagerResolver: () => sp.GetService<StandaloneDbContextLeaseManager>());
            configureOptions?.Invoke(sp, options);
            options.AddInterceptors(
                StandaloneTenantConnectionInterceptor.Instance,
                StandaloneTenantSaveChangesInterceptor.Instance,
                StandaloneConcurrencyTokenInterceptor.Instance);
            options.AddCadenceGxPAuditInterceptor(sp);
        });

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<TDbContext>(name: typeof(TDbContext).Name, tags: [ReadinessTag]);

        builder.Services.AddCadenceMigrations<TDbContext>(builder.Configuration);

        return builder;
    }

    /// <summary>
    /// Attaches a single <see cref="GxPAuditInterceptor"/> to <paramref name="options"/> if one has not already been
    /// configured, resolving actors fail-closed via <see cref="ResolveAuditActor"/>.
    /// </summary>
    public static DbContextOptionsBuilder AddCadenceGxPAuditInterceptor(
        this DbContextOptionsBuilder options,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        bool alreadyRegistered = options.Options
            .FindExtension<CoreOptionsExtension>()?
            .Interceptors?
            .OfType<GxPAuditInterceptor>()
            .Any() is true;

        if (!alreadyRegistered)
        {
            options.AddInterceptors(new GxPAuditInterceptor(
                () => ResolveAuditActor(serviceProvider),
                serviceProvider.GetService<IClock>() ?? SystemClock.Instance,
                () => ResolveAuditChangeReason(serviceProvider)));
        }

        return options;
    }

    /// <summary>
    /// Resolves the active change reason for a GxP database save from <see cref="IAuditActorScope"/> or the decoded
    /// RFC 8187 <c>X-Change-Reason</c> HTTP request header when an audited entity did not supply one directly.
    /// </summary>
    public static string? ResolveAuditChangeReason(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        string? scopedReason = serviceProvider.GetService<IAuditActorScope>()?.CurrentChangeReason
                               ?? AmbientAuditActorScope.Current?.ChangeReason;
        if (!string.IsNullOrWhiteSpace(scopedReason))
        {
            return scopedReason;
        }

        HttpContext? httpContext = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
        return httpContext is not null ? ChangeReasonHeader.Read(httpContext.Request) : null;
    }

    /// <summary>
    /// Resolves the attributable actor for a GxP database save from <see cref="IAuditActorScope"/> (<c>svc:*</c>)
    /// first, then the verified HTTP caller claims, failing closed with <see cref="InvalidOperationException"/>
    /// when neither is present.
    /// </summary>
    public static string ResolveAuditActor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        string? scopedActor = serviceProvider.GetService<IAuditActorScope>()?.CurrentActor
                              ?? AmbientAuditActorScope.Current?.Actor;
        if (!string.IsNullOrWhiteSpace(scopedActor))
        {
            return scopedActor;
        }

        HttpContext? httpContext = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext;
        if (httpContext is not null)
        {
            string? principalUserId = CallerScope.Principal(httpContext)?.UserId;
            if (!string.IsNullOrWhiteSpace(principalUserId))
            {
                return principalUserId;
            }

            if (httpContext.User.Identity is { IsAuthenticated: true })
            {
                string? claimUserId = httpContext.User.FindFirst(CadenceClaimTypes.Subject)?.Value
                                      ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                      ?? httpContext.User.Identity.Name;
                if (!string.IsNullOrWhiteSpace(claimUserId))
                {
                    return claimUserId;
                }
            }
        }

        throw new InvalidOperationException(
            "GxP audit entries require a verified authenticated HTTP caller or an explicit 'svc:' IAuditActorScope.");
    }

    /// <summary>
    /// Registers PostgreSQL tenant code resolution (<c>kernel.tenant_id_by_code</c>) and scoped tenant accessors.
    /// </summary>
    public static IServiceCollection AddCadencePostgresTenancy(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddHttpContextAccessor();
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.TryAddSingleton<ITenantCodeResolver>(sp => new PostgresTenantCodeResolver(connectionString, sp.GetService<IClock>()));
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
        services.AddProblemDetails();
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.TryAddScoped<IAuditActorScope, AmbientAuditActorScope>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IExceptionHandler, StandaloneConcurrencyExceptionHandler>());
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

    /// <summary>
    /// Resolves the connection string for <c>IDesignTimeDbContextFactory</c> implementations (the
    /// <c>dotnet ef</c> tooling process, which has no host configuration): the value after
    /// <see cref="DesignTimeConnectionArgument"/> in <paramref name="args"/>, then
    /// <c>ConnectionStrings__cadence</c>, then <c>POSTGRES_CONNECTION_STRING</c>. Throws when none is set
    /// rather than falling back to a hardcoded credential (ADR-2260).
    /// </summary>
    public static string ResolveDesignTimeConnectionString(
        string[]? args = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var readEnv = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        if (args is not null)
        {
            int index = Array.IndexOf(args, DesignTimeConnectionArgument);
            if (index >= 0 && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return args[index + 1];
            }
        }

        if (readEnv(PrimaryConnectionStringEnvVar) is { Length: > 0 } primary &&
            !string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        if (readEnv(LegacyConnectionStringKey) is { Length: > 0 } legacy &&
            !string.IsNullOrWhiteSpace(legacy))
        {
            return legacy;
        }

        throw new InvalidOperationException(
            $"No PostgreSQL connection string configured. Set {PrimaryConnectionStringEnvVar} or {LegacyConnectionStringKey}, " +
            $"or pass {DesignTimeConnectionArgument} \"...\" after -- on the dotnet ef command line.");
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
