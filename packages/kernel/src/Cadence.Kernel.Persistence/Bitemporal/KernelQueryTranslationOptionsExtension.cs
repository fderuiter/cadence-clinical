using System.Collections.Generic;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// Adds the kernel's query translators (<see cref="TimeRangeTranslator"/>) to EF Core's internal service provider.
/// Applied by <see cref="KernelDbContext"/> itself, so every way of building the context gets it.
/// @req:PRD-SYS-053
/// </summary>
internal sealed class KernelQueryTranslationOptionsExtension : IDbContextOptionsExtension
{
    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) =>
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, TimeRangeMethodCallTranslatorPlugin>()
            .TryAdd<IMemberTranslatorPlugin, TimeRangeMemberTranslatorPlugin>();

    public void Validate(IDbContextOptions options)
    {
        // Nothing to validate: the extension carries no settings.
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
