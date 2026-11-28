using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Database.Tenancy;

/// <summary>
/// Holds a single PostgreSQL connection lease per scoped <see cref="DbContext"/> across reads, audit chain-tip
/// prefetches, signature token consumption, and saves, releasing the lease before <see cref="DbContext"/> disposal
/// so <see cref="StandaloneTenantConnectionInterceptor"/> resets <c>cadence.tenant_id</c> before returning the
/// connection to <c>NpgsqlDataSource</c>.
/// </summary>
public sealed class StandaloneDbContextLeaseManager : IDisposable, IAsyncDisposable
{
    private readonly List<DbContext> _leasedContexts = [];
    private bool _disposed;

    public void AcquireLease(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_disposed || !context.Database.IsRelational())
        {
            return;
        }

        foreach (DbContext existing in _leasedContexts)
        {
            if (ReferenceEquals(existing, context))
            {
                return;
            }
        }

        context.Database.OpenConnection();
        _leasedContexts.Add(context);
    }

    public async ValueTask AcquireLeaseAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_disposed || !context.Database.IsRelational())
        {
            return;
        }

        foreach (DbContext existing in _leasedContexts)
        {
            if (ReferenceEquals(existing, context))
            {
                return;
            }
        }

        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        _leasedContexts.Add(context);
    }

    public void ReleaseBrokenLease(DbContext? context)
    {
        if (context is null || _disposed)
        {
            return;
        }

        for (int i = _leasedContexts.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_leasedContexts[i], context))
            {
                _leasedContexts.RemoveAt(i);
                try
                {
                    context.Database.CloseConnection();
                }
                catch (ObjectDisposedException)
                {
                    // Context was already disposed explicitly before broken-lease cleanup.
                }

                return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (int i = _leasedContexts.Count - 1; i >= 0; i--)
        {
            try
            {
                DbContext leased = _leasedContexts[i];
                leased.Database.CloseConnection();
                DbConnection conn = leased.Database.GetDbConnection();
                if (conn.State == ConnectionState.Open)
                {
                    StandaloneTenantConnectionInterceptor.ForceResetAndClose(conn);
                }
            }
            catch (ObjectDisposedException)
            {
                // Context was already disposed explicitly by caller before scope teardown.
            }
        }

        _leasedContexts.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (int i = _leasedContexts.Count - 1; i >= 0; i--)
        {
            try
            {
                DbContext leased = _leasedContexts[i];
                await leased.Database.CloseConnectionAsync().ConfigureAwait(false);
                DbConnection conn = leased.Database.GetDbConnection();
                if (conn.State == ConnectionState.Open)
                {
                    await StandaloneTenantConnectionInterceptor.ForceResetAndCloseAsync(conn).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException)
            {
                // Context was already disposed explicitly by caller before scope teardown.
            }
        }

        _leasedContexts.Clear();
    }
}

/// <summary>
/// Carries the scoped <see cref="ICurrentTenantAccessor"/> and lazy <see cref="StandaloneDbContextLeaseManager"/>
/// resolver on <see cref="DbContextOptions"/> without fragmenting EF Core's internal service-provider cache.
/// </summary>
public sealed class StandaloneTenantOptionsExtension : IDbContextOptionsExtension
{
    private readonly Func<StandaloneDbContextLeaseManager?>? _leaseManagerResolver;
    private DbContextOptionsExtensionInfo? _info;

    public StandaloneTenantOptionsExtension(
        ICurrentTenantAccessor? tenantAccessor,
        bool isRelational,
        Func<StandaloneDbContextLeaseManager?>? leaseManagerResolver = null)
    {
        TenantAccessor = tenantAccessor;
        IsRelational = isRelational;
        _leaseManagerResolver = leaseManagerResolver;
    }

    public ICurrentTenantAccessor? TenantAccessor { get; }

    public bool IsRelational { get; }

    internal StandaloneDbContextLeaseManager? ResolveLeaseManager() => _leaseManagerResolver?.Invoke();

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
        bool isRelational,
        Func<StandaloneDbContextLeaseManager?>? leaseManagerResolver = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new StandaloneTenantOptionsExtension(tenantAccessor, isRelational, leaseManagerResolver));
        return optionsBuilder;
    }
}
