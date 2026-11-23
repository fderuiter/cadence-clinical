using System;

using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Cgt;
using Cadence.Apps.Execution.Domain.Coding;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Devices;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Signatures;
using Cadence.Database.Tenancy;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 persistence gateway for the Execution bounded context.
/// Exposes aggregates for subjects, consent, site compliance, clinical queries, lab reference ranges, CEC dossiers, CGT chain of custody records, devices, staged adverse event reviews, and IP kit batch reconciliations.
/// @req:PRD-SUB-001
/// @req:PRD-DEV-001
/// @req:PRD-DEV-002
/// @req:PRD-DEV-003
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-007
/// @req:PRD-SYS-192
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-SCM-005
/// @req:PRD-SCM-006
/// @req:SRS-EDC-001
/// @req:SRS-EDC-002
/// @req:SRS-EDC-004
/// </summary>
public sealed class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);

    public Guid? ActiveRelationalTenantFilter => ResolveRelationalTenantFilter(this);

    public Guid? ActiveBoundTenantId => ResolveActiveBoundTenantId(this);

    public bool HasUnverifiedRequestTenant => ResolveHasUnverifiedRequestTenant(this);

    public DbSet<ClinicalSubject> ClinicalSubjects => Set<ClinicalSubject>();

    public DbSet<SubjectConsent> SubjectConsents => Set<SubjectConsent>();

    public DbSet<SiteComplianceCache> SiteComplianceCaches => Set<SiteComplianceCache>();

    public DbSet<SiteActivationPeriod> SiteActivationPeriods => Set<SiteActivationPeriod>();


    public DbSet<StagedAdverseEventReview> StagedAdverseEventReviews => Set<StagedAdverseEventReview>();

    public DbSet<CecDossier> CecDossiers => Set<CecDossier>();

    public DbSet<CecChairAdjudication> CecChairAdjudications => Set<CecChairAdjudication>();

    public DbSet<CgtChainOfCustody> CGTChainsOfCustody => Set<CgtChainOfCustody>();

    public DbSet<CgtMilestoneEvent> CGTMilestoneEvents => Set<CgtMilestoneEvent>();

    public DbSet<CgtBedsideVerification> CGTBedsideVerifications => Set<CgtBedsideVerification>();

    public DbSet<CgtDestructionCertificate> CGTDestructionCertificates => Set<CgtDestructionCertificate>();

    public DbSet<CgtVialReturnReconciliation> CGTVialReturnReconciliations => Set<CgtVialReturnReconciliation>();

    public DbSet<SynonymRule> SynonymRules => Set<SynonymRule>();

    public DbSet<BulkSynonymImportJob> BulkSynonymImportJobs => Set<BulkSynonymImportJob>();

    public DbSet<DeviceInventoryItem> DeviceInventoryItems => Set<DeviceInventoryItem>();

    public DbSet<DeviceDeficiency> DeviceDeficiencies => Set<DeviceDeficiency>();

    public DbSet<DeviceQuarantineRecord> DeviceQuarantineRecords => Set<DeviceQuarantineRecord>();

    public DbSet<IpKitInventoryItem> IpKitInventoryItems => Set<IpKitInventoryItem>();

    public DbSet<IpKitReconciliationBatch> IpKitReconciliationBatches => Set<IpKitReconciliationBatch>();

    public DbSet<IpKitReconciliationCertificate> IpKitReconciliationCertificates => Set<IpKitReconciliationCertificate>();

    public DbSet<RandomizationScheme> RandomizationSchemes => Set<RandomizationScheme>();

    public DbSet<RandomizationListSlot> RandomizationListSlots => Set<RandomizationListSlot>();

    public DbSet<RandomizationAllocation> RandomizationAllocations => Set<RandomizationAllocation>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<LabReferenceRange> LabReferenceRanges => Set<LabReferenceRange>();

    public DbSet<BulkLabRangeImportJob> BulkLabRangeImportJobs => Set<BulkLabRangeImportJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ClinicalSubjectConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectConsentConfiguration());
        modelBuilder.ApplyConfiguration(new RandomizationSchemeConfiguration());
        modelBuilder.ApplyConfiguration(new RandomizationListSlotConfiguration());
        modelBuilder.ApplyConfiguration(new RandomizationAllocationConfiguration());
        modelBuilder.ApplyConfiguration(new SiteComplianceCacheConfiguration());
        modelBuilder.ApplyConfiguration(new SiteActivationPeriodConfiguration(this));
        modelBuilder.ApplyConfiguration(new StagedAdverseEventReviewConfiguration());
        modelBuilder.ApplyConfiguration(new CecDossierConfiguration());
        modelBuilder.ApplyConfiguration(new CecChairAdjudicationConfiguration());
        modelBuilder.ApplyConfiguration(new CgtChainOfCustodyConfiguration());
        modelBuilder.ApplyConfiguration(new CgtMilestoneEventConfiguration());
        modelBuilder.ApplyConfiguration(new CgtBedsideVerificationConfiguration());
        modelBuilder.ApplyConfiguration(new CgtDestructionCertificateConfiguration());
        modelBuilder.ApplyConfiguration(new CgtVialReturnReconciliationConfiguration());
        modelBuilder.ApplyConfiguration(new SynonymRuleConfiguration());
        modelBuilder.ApplyConfiguration(new BulkSynonymImportJobConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceInventoryItemConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceDeficiencyConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceQuarantineRecordConfiguration());
        modelBuilder.ApplyConfiguration(new IpKitInventoryItemConfiguration());
        modelBuilder.ApplyConfiguration(new IpKitReconciliationBatchConfiguration());
        modelBuilder.ApplyConfiguration(new IpKitReconciliationCertificateConfiguration());
        modelBuilder.ApplyConfiguration(new LabReferenceRangeConfiguration());
        modelBuilder.ApplyConfiguration(new BulkLabRangeImportJobConfiguration());
        modelBuilder.ApplyConfiguration(new ConsumedSignatureTokenConfiguration());

        modelBuilder.AddTransactionalOutboxEntities();

        modelBuilder.Entity<ClinicalSubject>()
            .HasQueryFilter(e => ActiveRelationalTenantFilter == null || e.TenantId == ActiveRelationalTenantFilter);
        modelBuilder.Entity<SubjectConsent>()
            .HasQueryFilter(e => ActiveRelationalTenantFilter == null || e.TenantId == ActiveRelationalTenantFilter);
        modelBuilder.Entity<SiteComplianceCache>()
            .HasQueryFilter(e => ActiveRelationalTenantFilter == null || e.TenantId == ActiveRelationalTenantFilter);

        modelBuilder.ConfigureStandaloneTenancy(this);
    }

    private static Guid? ResolveRelationalTenantFilter(DbContext context)
    {
        var ext = context.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        if (ext is null || !ext.IsRelational || ext.TenantAccessor is null || ext.TenantAccessor.IsSystemSuppressed)
        {
            return null;
        }

        if (ext.TenantAccessor.GetCurrentTenantId() is { } activeTenant && activeTenant != Guid.Empty)
        {
            return activeTenant;
        }

        return StandaloneTenantModelExtensions.UnboundTenantSentinel;
    }

    private static Guid? ResolveActiveBoundTenantId(DbContext context)
    {
        var ext = context.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        if (ext?.TenantAccessor is null || ext.TenantAccessor.IsSystemSuppressed)
        {
            return null;
        }

        return ext.TenantAccessor.GetCurrentTenantId() is { } activeTenant && activeTenant != Guid.Empty
            ? activeTenant
            : null;
    }

    private static bool ResolveHasUnverifiedRequestTenant(DbContext context)
    {
        var ext = context.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        if (ext?.TenantAccessor is null || ext.TenantAccessor.IsSystemSuppressed)
        {
            return false;
        }

        return ext.TenantAccessor.HasHttpRequestWithoutTenant;
    }
}
