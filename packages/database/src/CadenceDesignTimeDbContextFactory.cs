using System;
using System.Collections.Generic;

using Cadence.Security.FieldProtection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using NpgsqlTypes;

namespace Cadence.Database;

/// <summary>
/// Shared design-time <see cref="DbContext"/> factory for Cadence services (<c>dotnet ef</c> and
/// <c>Cadence.SchemaDocExporter</c>). Resolves the PostgreSQL connection string fail-closed via
/// <see cref="CadenceDatabaseExtensions.ResolveDesignTimeConnectionString"/> (ADR-2260), scopes the connection
/// to the module's schema via <see cref="CadenceDatabaseExtensions.WithModuleSchema"/> (ADR-2447), configures
/// Npgsql, and invokes a typed constructor delegate.
/// </summary>
/// <typeparam name="TDbContext">The service <see cref="DbContext"/> type.</typeparam>
public abstract class CadenceDesignTimeDbContextFactory<TDbContext> : IDesignTimeDbContextFactory<TDbContext>
    where TDbContext : DbContext
{
    private readonly Func<DbContextOptions<TDbContext>, TDbContext> _createContext;
    private readonly string _schema;

    /// <summary>
    /// Initializes a design-time factory for a context constructed from <see cref="DbContextOptions{TContext}"/>.
    /// </summary>
    /// <param name="createContext">Typed constructor delegate for <typeparamref name="TDbContext"/>.</param>
    /// <param name="schema">
    /// Optional PostgreSQL module schema; defaults to <see cref="CadenceDatabaseExtensions.ModuleSchemaFor{TDbContext}"/>.
    /// </param>
    protected CadenceDesignTimeDbContextFactory(
        Func<DbContextOptions<TDbContext>, TDbContext> createContext,
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(createContext);

        _createContext = createContext;
        _schema = schema ?? CadenceDatabaseExtensions.ModuleSchemaFor<TDbContext>();
    }

    /// <summary>
    /// Initializes a design-time factory for a field-encrypting context, supplying
    /// <see cref="DesignTimeFieldEncryptor.Instance"/> so EF Core can build the model without encryption keys while
    /// rejecting runtime value conversion (ADR-2494).
    /// </summary>
    /// <param name="createContext">Typed constructor delegate accepting options and <see cref="IFieldEncryptor"/>.</param>
    /// <param name="schema">
    /// Optional PostgreSQL module schema; defaults to <see cref="CadenceDatabaseExtensions.ModuleSchemaFor{TDbContext}"/>.
    /// </param>
    protected CadenceDesignTimeDbContextFactory(
        Func<DbContextOptions<TDbContext>, IFieldEncryptor, TDbContext> createContext,
        string? schema = null)
        : this(BindDesignTimeEncryptor(createContext), schema)
    {
    }

    /// <inheritdoc />
    public TDbContext CreateDbContext(string[] args)
    {
        string connectionString = CadenceDatabaseExtensions.ResolveDesignTimeConnectionString(args);

        var optionsBuilder = new DbContextOptionsBuilder<TDbContext>()
            .UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, _schema));
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(DesignTimeTstzRangeOptionsExtension.Instance);

        return _createContext(optionsBuilder.Options);
    }

    private static Func<DbContextOptions<TDbContext>, TDbContext> BindDesignTimeEncryptor(
        Func<DbContextOptions<TDbContext>, IFieldEncryptor, TDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        return options => createContext(options, DesignTimeFieldEncryptor.Instance);
    }
}

internal sealed class DesignTimeTstzRangeOptionsExtension : IDbContextOptionsExtension
{
    public static readonly DesignTimeTstzRangeOptionsExtension Instance = new();
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.AddSingleton<IRelationalTypeMappingSourcePlugin, TstzRangeOffsetMappingPlugin>();
        services.AddSingleton<Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure.IConventionSetPlugin, StandaloneSentinelConventionPlugin>();
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "DesignTimeTstzRange ";
        public override int GetServiceProviderHashCode() => 1;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Cadence:DesignTimeTstzRange"] = "1";
    }

    private sealed class TstzRangeOffsetMappingPlugin(IServiceProvider serviceProvider) : IRelationalTypeMappingSourcePlugin
    {
        private bool _resolving;

        public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
        {
            if (_resolving || mappingInfo.ClrType != typeof(NpgsqlRange<DateTimeOffset>) || mappingInfo.StoreTypeName is not null)
            {
                return null;
            }

            _resolving = true;
            try
            {
                return serviceProvider.GetRequiredService<IRelationalTypeMappingSource>()
                    .FindMapping(typeof(NpgsqlRange<DateTimeOffset>), "tstzrange");
            }
            finally
            {
                _resolving = false;
            }
        }
    }

    private sealed class StandaloneSentinelConventionPlugin
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
            foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            {
                AssignMissingStringStructSentinels(entityType.GetDeclaredProperties());
                foreach (var complexProperty in entityType.GetDeclaredComplexProperties())
                {
                    AssignMissingStringStructSentinels(complexProperty.ComplexType.GetDeclaredProperties());
                }
            }
        }

        private static void AssignMissingStringStructSentinels(
            IEnumerable<Microsoft.EntityFrameworkCore.Metadata.IConventionProperty> properties)
        {
            foreach (var property in properties)
            {
                if (property.ClrType is not { IsValueType: true, IsPrimitive: false, IsEnum: false } clrType ||
                    Nullable.GetUnderlyingType(clrType) is not null ||
                    property.GetValueConverter() is not { ProviderClrType: var providerType } converter ||
                    providerType != typeof(string))
                {
                    continue;
                }

                object defaultSentinel = property.Sentinel ?? Activator.CreateInstance(clrType)!;
                if (converter.ConvertToProvider(defaultSentinel) is null)
                {
                    property.SetSentinel(converter.ConvertFromProvider("__sentinel__"));
                }
            }
        }
    }
}
