using System;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Indicators;
using Cadence.Kernel.Intelligence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.Audit;
using Cadence.Kernel.Persistence.Authorization;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Execution;
using Cadence.Kernel.Persistence.Indicators;
using Cadence.Kernel.Persistence.Intelligence;
using Cadence.Kernel.Persistence.Knowledge;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Outbox;
using Cadence.Kernel.Persistence.Protocol;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Trust;
using Cadence.Kernel.Persistence.WorkQueues;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// Registers kernel persistence. Called by the kernel API host only; application modules must not call it (K-XS-2).
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
        services.TryAddSingleton(new KernelConnectionString(connectionString));
        services.TryAddSingleton<KernelTenantDbContextFactory>();
        services.TryAddSingleton<KernelUnitOfWorkFactory>();
        services.TryAddSingleton<IKernelUnitOfWorkFactory>(sp => sp.GetRequiredService<KernelUnitOfWorkFactory>());
        services.TryAddSingleton<KernelAuditChainVerifier>();
        services.TryAddSingleton<IAttestationVerifier, KernelAttestationVerifier>();
        services.TryAddSingleton<IEnrollmentDirectory, KernelEnrollmentDirectory>();
        services.TryAddSingleton<IStudyDirectory, KernelStudyDirectory>();
        services.TryAddSingleton<IAuthorityStore, KernelAuthorityStore>();
        services.TryAddSingleton<ITeamDirectoryReader, KernelTeamDirectoryReader>();
        services.TryAddSingleton<IProtocolVersionReader, KernelProtocolVersionReader>();
        services.TryAddSingleton<IProposalReader, KernelProposalReader>();
        services.TryAddSingleton<IEffectivityReader, KernelEffectivityReader>();
        services.TryAddSingleton<IObservationRecords, KernelObservationRecords>();
        services.TryAddSingleton<ICollectionInstanceRecords, KernelCollectionInstanceRecords>();
        services.TryAddSingleton<ILockRecords, KernelLockRecords>();
        services.TryAddSingleton<IKernelExportReader, KernelExportReader>();
        services.TryAddSingleton<ITenantDirectory, KernelTenantDirectory>();
        services.TryAddSingleton<ISubjectTenantBindingProbe, KernelSubjectTenantBindingProbe>();
        services.TryAddSingleton<ITenantMembershipReader, KernelTenantMembershipReader>();
        services.TryAddSingleton<ITenantResidencyReader, KernelTenantResidencyReader>();
        services.TryAddSingleton<ITenantEntitlementReader, KernelTenantEntitlementReader>();
        services.TryAddSingleton<ISubscriptionScopingUsageReader, KernelSubscriptionScopingUsageReader>();
        services.TryAddSingleton<KernelAuditChainSealer>();
        services.TryAddSingleton<IConceptRegistry, KernelConceptRegistry>();
        services.TryAddSingleton<IWorkQueueDefinitions, KernelWorkQueueDefinitions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkItemSource, ProtocolVersionsInReviewSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndicatorPopulationSource, EnrollmentPopulationSource>());

        // Lineage reads need the host's INodeVisibility (ADR-2471); none is registered here, so resolving
        // IKernelLineage in a host that has not decided visibility fails instead of showing every object.
        services.TryAddSingleton<KernelLineageStore>();
        services.TryAddSingleton<IKernelLineage>(sp => sp.GetRequiredService<KernelLineageStore>());
        services.TryAddSingleton<IAmendmentImpact, KernelAmendmentImpact>();
        services.TryAddSingleton<ILineageOrphanSink, LoggingLineageOrphanSink>();
        services.TryAddSingleton<KernelLineageOrphanCheck>();

        return services;
    }

    /// <summary>
    /// Runs the kernel's background jobs in the host: the outbox relay (delivers events to the host's
    /// <see cref="IKernelEventPublisher"/>), the hourly verify-and-seal pass over every tenant's audit chain and the
    /// hourly K-LIN-1 lineage check (reported to <see cref="ILineageOrphanSink"/>). Call after
    /// <see cref="AddKernelPersistence"/>. Each pass takes a PostgreSQL leader lock, so with several replicas exactly one
    /// runs each job at a time (ADR-2601).
    /// </summary>
    public static IServiceCollection AddKernelBackgroundJobs(
        this IServiceCollection services, Action<KernelBackgroundOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new KernelBackgroundOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.RelayBatchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ActivityBatchSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ActivityLookBack, TimeSpan.Zero);

        services.RemoveAll<KernelBackgroundOptions>();
        services.AddSingleton(options);
        services.TryAddSingleton<KernelLeaderLock>();
        services.TryAddSingleton<KernelOutboxRelay>();
        services.AddHostedService<KernelOutboxRelayService>();
        services.AddHostedService<KernelAuditSealingService>();
        services.AddHostedService<KernelLineageCheckService>();
        return services;
    }
}
