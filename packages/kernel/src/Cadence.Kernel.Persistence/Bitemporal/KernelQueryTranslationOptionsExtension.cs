using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// Adds the kernel's query translators (<see cref="TimeRangeTranslator"/>) and default-schema migration SQL
/// decoration (preserving <see cref="KernelDbContext.Schema"/> when compacted <c>*.Designer.cs</c> files omit
/// <c>BuildTargetModel</c>) to EF Core's internal service provider. Applied by <see cref="KernelDbContext"/> itself,
/// so every way of building the context gets it.
/// </summary>
internal sealed class KernelQueryTranslationOptionsExtension : IDbContextOptionsExtension
{
    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, TimeRangeMethodCallTranslatorPlugin>()
            .TryAdd<IMemberTranslatorPlugin, TimeRangeMemberTranslatorPlugin>();

        ServiceDescriptor? sqlGen = services.FirstOrDefault(static d =>
            d.ServiceType == typeof(IMigrationsSqlGenerator) && d.ImplementationType is not null);
        if (sqlGen?.ImplementationType is { } implType && implType != typeof(KernelDefaultSchemaMigrationsSqlGenerator))
        {
            services.TryAdd(ServiceDescriptor.Describe(implType, implType, sqlGen.Lifetime));
            services.Replace(ServiceDescriptor.Describe(
                typeof(IMigrationsSqlGenerator),
                sp => new KernelDefaultSchemaMigrationsSqlGenerator((IMigrationsSqlGenerator)sp.GetRequiredService(implType)),
                sqlGen.Lifetime));
        }
    }

    public void Validate(IDbContextOptions options)
    {
        // Nothing to validate: the extension carries no settings.
    }

    private sealed class KernelDefaultSchemaMigrationsSqlGenerator(IMigrationsSqlGenerator inner) : IMigrationsSqlGenerator
    {
        private static readonly IModel DefaultSchemaModel = CreateDefaultSchemaModel();

        public IReadOnlyList<MigrationCommand> Generate(
            IReadOnlyList<MigrationOperation> operations,
            IModel? model = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            IModel? effectiveModel = model is not null && model.GetDefaultSchema() is null && !model.GetEntityTypes().Any()
                ? DefaultSchemaModel
                : model;
            return inner.Generate(operations, effectiveModel, options);
        }

        private static IModel CreateDefaultSchemaModel()
        {
            var builder = new ModelBuilder();
            builder.HasDefaultSchema(KernelDbContext.Schema);
            return builder.FinalizeModel();
        }
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Cadence kernel query translations ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Cadence:KernelQueryTranslations"] = "1";
    }
}
