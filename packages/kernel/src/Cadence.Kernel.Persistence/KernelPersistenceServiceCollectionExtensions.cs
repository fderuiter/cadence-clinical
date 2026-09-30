using System;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Persistence.Audit;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Outbox;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Registers kernel persistence. Called by the kernel API host only; application modules must not call it (K-XS-2).
/// @req:PRD-SYS-053
/// </summary>
public static class KernelPersistenceServiceCollectionExtensions
{
    private static readonly KernelTenantConnectionInterceptor TenantInterceptor = new();

    /// <summary>
    /// Registers the kernel <see cref="DbContext"/> against PostgreSQL and the unit of work the command pipeline
    /// commits through (<see cref="IKernelUnitOfWorkFactory"/>). Each command gets its own context and transaction;
    /// audit fields, provenance, events and the audit chain are written by that transaction, not by an interceptor.
    /// </summary>
    /// <remarks>
    /// No automatic retry strategy is configured: a command's transaction is not safely re-runnable by EF Core. Callers
    /// retry the whole command with the same idempotency key instead (K-TX-3).
    /// <para>
    /// Every connection is scoped to one tenant by PostgreSQL Row-Level Security (K-ID-2, ADR-2250): the pipeline
    /// binds each command's context to the command's tenant, and a context without a tenant cannot connect. Connect
    /// as the application role (<see cref="KernelDatabase.GrantAppRoleSql"/>), not as the schema owner, and apply
    /// migrations separately with <see cref="KernelDatabase.MigrateAsync"/>.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The <c>cadence</c> database connection string for the application role.</param>
    public static IServiceCollection AddKernelPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<KernelDbContext>(options =>
        {
            KernelDatabase.UseKernelNpgsql(options, connectionString);
            options.AddInterceptors(TenantInterceptor);
        });
        services.TryAddSingleton<KernelTenantDbContextFactory>();
        services.TryAddSingleton<KernelUnitOfWorkFactory>();
        services.TryAddSingleton<IKernelUnitOfWorkFactory>(sp => sp.GetRequiredService<KernelUnitOfWorkFactory>());
        services.TryAddSingleton<KernelAuditChainVerifier>();
        services.TryAddSingleton<KernelAuditChainSealer>();

        return services;
    }

    /// <summary>
    /// Runs the kernel's background jobs in the host: the outbox relay (delivers events to the host's
    /// <see cref="IKernelEventPublisher"/>) and the hourly verify-and-seal pass over every tenant's audit chain. Call
    /// after <see cref="AddKernelPersistence"/> in exactly one host per deployment.
    /// </summary>
    public static IServiceCollection AddKernelBackgroundJobs(
        this IServiceCollection services, Action<KernelBackgroundOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new KernelBackgroundOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.RelayBatchSize);

        services.AddSingleton(options);
        services.TryAddSingleton<KernelOutboxRelay>();
        services.AddHostedService<KernelOutboxRelayService>();
        services.AddHostedService<KernelAuditSealingService>();
        return services;
    }
}
