using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 persistence gateway for the Execution bounded context.
/// Exposes aggregates for subjects, observations, form submissions, clinical queries, and CEC dossiers.
/// @req:PRD-SUB-001
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
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

    public DbSet<ClinicalQuery> ClinicalQueries => Set<ClinicalQuery>();

    public DbSet<CecDossier> CecDossiers => Set<CecDossier>();

    public DbSet<CecChairAdjudication> CecChairAdjudications => Set<CecChairAdjudication>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ClinicalSubjectConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectConsentConfiguration());
        modelBuilder.ApplyConfiguration(new SiteComplianceCacheConfiguration());
        modelBuilder.ApplyConfiguration(new ClinicalObservationConfiguration());
        modelBuilder.ApplyConfiguration(new FormSubmissionConfiguration());
        modelBuilder.ApplyConfiguration(new ClinicalQueryConfiguration());
        modelBuilder.ApplyConfiguration(new CecDossierConfiguration());
        modelBuilder.ApplyConfiguration(new CecChairAdjudicationConfiguration());

        modelBuilder.AddTransactionalOutboxEntities();
    }
}
