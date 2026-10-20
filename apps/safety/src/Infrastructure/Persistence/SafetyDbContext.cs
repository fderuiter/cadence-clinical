using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Signatures;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Safety.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Safety microservice persistence gateway.
/// @req:PRD-SYS-053
/// </summary>
public sealed class SafetyDbContext(DbContextOptions<SafetyDbContext> options) : DbContext(options)
{
    public DbSet<IcsrReportingEvaluation> SafetyReportingEvaluations => Set<IcsrReportingEvaluation>();
    public DbSet<SafetyCaseIcsr> SafetyCases => Set<SafetyCaseIcsr>();
    public DbSet<ExportJob> SafetyExportJobs => Set<ExportJob>();
    public DbSet<SaeReconciliationRun> SaeReconciliationRuns => Set<SaeReconciliationRun>();
    public DbSet<SaeDiscrepancy> SaeDiscrepancies => Set<SaeDiscrepancy>();
    public DbSet<SaeReconciliationJob> SaeReconciliationJobs => Set<SaeReconciliationJob>();
    public DbSet<SafetyNarrative> SafetyNarratives => Set<SafetyNarrative>();
    public DbSet<SafetyAuditLog> SafetyAuditLogs => Set<SafetyAuditLog>();
    public DbSet<As2Transmission> SafetyAs2Transmissions => Set<As2Transmission>();
    public DbSet<As2TransmissionAuditLog> SafetyAs2TransmissionAuditLogs => Set<As2TransmissionAuditLog>();
    public DbSet<SafetySignalDetectionRun> SafetySignalDetectionRuns => Set<SafetySignalDetectionRun>();
    public DbSet<SafetySignalAlert> SafetySignalAlerts => Set<SafetySignalAlert>();
    public DbSet<PeriodicSafetyReportRecord> PeriodicSafetyReports => Set<PeriodicSafetyReportRecord>();
    public DbSet<DeviceSafetyInvestigation> DeviceSafetyInvestigations => Set<DeviceSafetyInvestigation>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Used e-signature tokens, single use across instances and restarts (ADR-2515, #5434, #5462).
        modelBuilder.ApplyConfiguration(new ConsumedSignatureTokenConfiguration());

        modelBuilder.ApplyConfiguration(new IcsrReportingEvaluationConfiguration());
        modelBuilder.ApplyConfiguration(new SafetyCaseIcsrConfiguration());
        modelBuilder.ApplyConfiguration(new ExportJobConfiguration());
        modelBuilder.ApplyConfiguration(new SaeReconciliationRunConfiguration());
        modelBuilder.ApplyConfiguration(new SaeDiscrepancyConfiguration());
        modelBuilder.ApplyConfiguration(new SaeReconciliationJobConfiguration());
        modelBuilder.ApplyConfiguration(new SafetyNarrativeConfiguration());
        modelBuilder.ApplyConfiguration(new SafetyAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new As2TransmissionConfiguration());
        modelBuilder.ApplyConfiguration(new As2TransmissionAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new SafetySignalDetectionRunConfiguration());
        modelBuilder.ApplyConfiguration(new SafetySignalAlertConfiguration());
        modelBuilder.ApplyConfiguration(new PeriodicSafetyReportRecordConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceSafetyInvestigationConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
    }
}
