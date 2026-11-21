using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Database.Tenancy;

/// <summary>
/// Carries the scoped <see cref="ICurrentTenantAccessor"/> on <see cref="DbContextOptions"/> without fragmenting
/// EF Core's internal service-provider cache.
/// </summary>
public sealed class StandaloneTenantOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public StandaloneTenantOptionsExtension(ICurrentTenantAccessor? tenantAccessor, bool isRelational)
    {
        TenantAccessor = tenantAccessor;
        IsRelational = isRelational;
    }

    public ICurrentTenantAccessor? TenantAccessor { get; }

    public bool IsRelational { get; }

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private StandaloneTenantOptionsExtension TypedExtension => (StandaloneTenantOptionsExtension)Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => TypedExtension.IsRelational ? "CadenceStandaloneTenancy(Relational)" : "CadenceStandaloneTenancy(InMemory)";

        public override int GetServiceProviderHashCode() => HashCode.Combine(TypedExtension.IsRelational);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo otherInfo && TypedExtension.IsRelational == otherInfo.TypedExtension.IsRelational;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Cadence:StandaloneTenancy:IsRelational"] = TypedExtension.IsRelational ? "1" : "0";
    }
}

public static class StandaloneTenantDbContextOptionsExtensions
{
    public static DbContextOptionsBuilder UseCadenceStandaloneTenancy(
        this DbContextOptionsBuilder optionsBuilder,
        ICurrentTenantAccessor? tenantAccessor,
        bool isRelational)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new StandaloneTenantOptionsExtension(tenantAccessor, isRelational));
        return optionsBuilder;
    }
}
