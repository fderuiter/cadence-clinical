using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using NpgsqlTypes;

namespace Cadence.Kernel.Modules;

/// <summary>
/// A module's own schema migrations, as the composition host applies them (ADR-2600, ADR-2601): after the kernel's, in
/// registration order, each with its migrations history in the module's schema. Like the kernel's, they run as the role
/// that owns the schemas, never as the application role, and only when the host is asked to migrate.
/// </summary>
/// <param name="Module">The module's name, lower snake case.</param>
/// <param name="Schema">The schema the module's migrations create and own.</param>
/// <param name="MigrateAsync">Applies the module's pending migrations on the given owner connection string.</param>
/// <param name="GrantAppRoleSql">
/// SQL granting an application role exactly what the module needs on its schema, for the deployment step that grants the
/// kernel's (<c>KernelDatabase.GrantAppRoleSql</c>).
/// </param>
public sealed record ModuleMigrationRegistration(
    string Module,
    string Schema,
    Func<string, CancellationToken, Task> MigrateAsync,
    Func<string, string> GrantAppRoleSql);

/// <summary>Registers a module's schema migrations with the composition host (ADR-2601).</summary>
public static partial class ModuleMigrationServiceCollectionExtensions
{
    /// <summary>The migrations history table in every module schema, named like the kernel's.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Design-time CLI argument supplying an explicit PostgreSQL connection string.</summary>
    public const string DesignTimeConnectionArgument = "--connection";

    /// <summary>Primary environment variable binding <c>ConnectionStrings:cadence</c> across runtime hosts and design-time tools.</summary>
    public const string PrimaryConnectionStringEnvVar = "ConnectionStrings__cadence";

    /// <summary>Legacy flat PostgreSQL connection string environment variable honoured after <c>ConnectionStrings__cadence</c>.</summary>
    public const string LegacyConnectionStringEnvVar = "POSTGRES_CONNECTION_STRING";

    /// <summary>
    /// Resolves the PostgreSQL connection string for kernel and module design-time <c>DbContext</c> factories:
    /// <c>--connection</c> first, <c>ConnectionStrings__cadence</c> second, and <c>POSTGRES_CONNECTION_STRING</c> third,
    /// failing closed with <see cref="InvalidOperationException"/> when all three are omitted (ADR-2260).
    /// </summary>
    /// <param name="args">Command-line arguments passed after <c>--</c> to <c>dotnet ef</c>.</param>
    /// <param name="getEnvironmentVariable">Optional environment variable lookup override for isolated testing.</param>
    public static string ResolveDesignTimeConnectionString(
        string[]? args = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var lookupEnv = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;

        if (args is not null)
        {
            int idx = Array.IndexOf(args, DesignTimeConnectionArgument);
            if (idx >= 0 && idx + 1 < args.Length && !string.IsNullOrWhiteSpace(args[idx + 1]))
            {
                return args[idx + 1];
            }
        }

        if (lookupEnv(PrimaryConnectionStringEnvVar) is { Length: > 0 } named &&
            !string.IsNullOrWhiteSpace(named))
        {
            return named;
        }

        if (lookupEnv(LegacyConnectionStringEnvVar) is { Length: > 0 } fallback &&
            !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        throw new InvalidOperationException(
            $"No PostgreSQL connection string configured. Set {PrimaryConnectionStringEnvVar} or {LegacyConnectionStringEnvVar}, " +
            $"or pass {DesignTimeConnectionArgument} \"...\" after -- on the dotnet ef command line.");
    }

    /// <summary>
    /// Registers design-time Npgsql range mapping support on <paramref name="optionsBuilder"/> so <c>dotnet ef dbcontext optimize</c>
    /// resolves <see cref="NpgsqlRange{T}"/> of <see cref="DateTimeOffset"/> sentinel literals cleanly offline.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/> type.</typeparam>
    /// <param name="optionsBuilder">The options builder to configure.</param>
    public static DbContextOptionsBuilder<TContext> UseModuleDesignTimeMappings<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(ModuleDesignTimeRangeExtension.Instance);
        return optionsBuilder;
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/>'s migrations as <paramref name="module"/>'s. The host applies them after
    /// the kernel's when <c>Database:MigrateOnStartup</c> or <c>Database:MigrateOnly</c> is set; a deployment that
    /// connects as the application role applies them and <paramref name="grantAppRoleSql"/> in its migration job.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="module">The module's name, lower snake case.</param>
    /// <param name="schema">The module's schema, lower snake case; its migrations history lives there.</param>
    /// <param name="create">Creates the context from options, usually <c>options =&gt; new TContext(options)</c>.</param>
    /// <param name="grantAppRoleSql">SQL that grants a named application role what the module needs.</param>
    /// <exception cref="InvalidOperationException">The module or the schema already has migrations registered.</exception>
    public static IServiceCollection AddModuleMigrations<TContext>(
        this IServiceCollection services,
        string module,
        string schema,
        Func<DbContextOptions<TContext>, TContext> create,
        Func<string, string> grantAppRoleSql)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(grantAppRoleSql);
        if (module is null || !SnakeCase().IsMatch(module))
        {
            throw new ArgumentException($"'{module}' is not a module name: lower snake case, starting with a letter.", nameof(module));
        }

        if (schema is null || !SnakeCase().IsMatch(schema) || schema == "kernel")
        {
            throw new ArgumentException($"'{schema}' is not a module schema: lower snake case, and not the kernel's.", nameof(schema));
        }

        if (services.Any(d => d.ImplementationInstance is ModuleMigrationRegistration r && (r.Module == module || r.Schema == schema)))
        {
            throw new InvalidOperationException($"Module '{module}' or schema '{schema}' already has migrations registered.");
        }

        services.AddSingleton(new ModuleMigrationRegistration(
            module,
            schema,
            async (connectionString, cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
                var options = new DbContextOptionsBuilder<TContext>()
                    .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
                    .Options;
                await using var db = create(options);
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            },
            grantAppRoleSql));
        return services;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,47}$", RegexOptions.CultureInvariant)]
    private static partial Regex SnakeCase();

    private sealed class ModuleDesignTimeRangeExtension : IDbContextOptionsExtension
    {
        public static readonly ModuleDesignTimeRangeExtension Instance = new();
        private DbContextOptionsExtensionInfo? _examInfo;

        public DbContextOptionsExtensionInfo Info => _examInfo ??= new ModuleRangeInfo(this);

        public void ApplyServices(IServiceCollection services)
        {
            services.AddSingleton<IRelationalTypeMappingSourcePlugin, ModuleRangeMappingPlugin>();
            services.AddSingleton<Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure.IConventionSetPlugin, ModuleSentinelConventionPlugin>();
        }

        public void Validate(IDbContextOptions options)
        {
        }

        private sealed class ModuleRangeInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            public override bool IsDatabaseProvider => false;
            public override string LogFragment => "ModuleDesignTimeRange ";
            public override int GetServiceProviderHashCode() => 2;
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ModuleRangeInfo;
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
                debugInfo["Cadence:ModuleDesignTimeRange"] = "1";
        }

        private sealed class ModuleRangeMappingPlugin(IServiceProvider provider) : IRelationalTypeMappingSourcePlugin
        {
            private bool _active;

            public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
            {
                if (_active || mappingInfo.ClrType != typeof(NpgsqlRange<DateTimeOffset>) || mappingInfo.StoreTypeName is not null)
                {
                    return null;
                }

                _active = true;
                try
                {
                    return provider.GetRequiredService<IRelationalTypeMappingSource>()
                        .FindMapping(typeof(NpgsqlRange<DateTimeOffset>), "tstzrange");
                }
                finally
                {
                    _active = false;
                }
            }
        }

        private sealed class ModuleSentinelConventionPlugin
            : Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure.IConventionSetPlugin,
              Microsoft.EntityFrameworkCore.Metadata.Conventions.IModelFinalizingConvention
        {
            public Microsoft.EntityFrameworkCore.Metadata.Conventions.ConventionSet ModifyConventions(
                Microsoft.EntityFrameworkCore.Metadata.Conventions.ConventionSet conventionSet)
            {
                conventionSet.ModelFinalizingConventions.Add(this);
                return conventionSet;
            }

            public void ProcessModelFinalizing(
                Microsoft.EntityFrameworkCore.Metadata.Builders.IConventionModelBuilder modelBuilder,
                Microsoft.EntityFrameworkCore.Metadata.Conventions.IConventionContext<Microsoft.EntityFrameworkCore.Metadata.Builders.IConventionModelBuilder> context)
            {
                var allProps = modelBuilder.Metadata.GetEntityTypes()
                    .SelectMany(static e => e.GetDeclaredProperties()
                        .Concat(e.GetDeclaredComplexProperties().SelectMany(static cp => cp.ComplexType.GetDeclaredProperties())));

                foreach (var prop in allProps)
                {
                    if (prop.ClrType is { IsValueType: true, IsPrimitive: false, IsEnum: false } valueType &&
                        Nullable.GetUnderlyingType(valueType) is null &&
                        prop.GetValueConverter() is { } conv &&
                        conv.ProviderClrType == typeof(string) &&
                        conv.ConvertToProvider(prop.Sentinel ?? Activator.CreateInstance(valueType)!) is null)
                    {
                        prop.SetSentinel(conv.ConvertFromProvider("__sentinel__"));
                    }
                }
            }
        }
    }
}
