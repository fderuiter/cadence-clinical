using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Cgt;
using Cadence.Apps.Execution.Domain.Coding;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Devices;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Signatures;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 persistence gateway for the Execution bounded context.
/// Exposes aggregates for subjects, observations, form submissions, clinical queries, CEC dossiers, CGT chain of custody records, devices, staged adverse event reviews, and IP kit batch reconciliations.
/// @req:PRD-SUB-001
/// @req:PRD-DEV-001
/// @req:PRD-DEV-002
/// @req:PRD-DEV-003
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-007
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-SCM-005
/// @req:PRD-SCM-006
/// @req:SRS-EDC-001
/// @req:SRS-EDC-002
/// @req:SRS-EDC-004
/// </summary>
public sealed class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : DbContext(options)
{
    public DbSet<ClinicalSubject> ClinicalSubjects => Set<ClinicalSubject>();

    public DbSet<SubjectConsent> SubjectConsents => Set<SubjectConsent>();

    public DbSet<SiteComplianceCache> SiteComplianceCaches => Set<SiteComplianceCache>();

    public DbSet<ClinicalObservation> ClinicalObservations => Set<ClinicalObservation>();

    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();

    public DbSet<PublishedFormVersionRegistry> PublishedFormVersionRegistries => Set<PublishedFormVersionRegistry>();

    public DbSet<ClinicalQuery> ClinicalQueries => Set<ClinicalQuery>();

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

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    public DbSet<LabReferenceRange> LabReferenceRanges => Set<LabReferenceRange>();

    public DbSet<BulkLabRangeImportJob> BulkLabRangeImportJobs => Set<BulkLabRangeImportJob>();

    /// <summary>Used e-signature step-up tokens, so each token signs once across instances and restarts (ADR-2515).</summary>
    public DbSet<ConsumedSignatureToken> ConsumedSignatureTokens => Set<ConsumedSignatureToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ClinicalSubjectConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectConsentConfiguration());
        modelBuilder.ApplyConfiguration(new SiteComplianceCacheConfiguration());
        modelBuilder.ApplyConfiguration(new ClinicalObservationConfiguration());
        modelBuilder.ApplyConfiguration(new FormSubmissionConfiguration());
        modelBuilder.ApplyConfiguration(new PublishedFormVersionRegistryConfiguration());
        modelBuilder.ApplyConfiguration(new ClinicalQueryConfiguration());
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
    }
}
