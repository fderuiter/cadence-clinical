using System;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Interop.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Interop microservice persistence gateway.
/// </summary>
public sealed class InteropDbContext(DbContextOptions<InteropDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);
    public DbSet<EproSubmission> EproSubmissions => Set<EproSubmission>();
    public DbSet<InteropAuditLog> InteropAuditLogs => Set<InteropAuditLog>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<SubjectAssignment> SubjectAssignments => Set<SubjectAssignment>();
    public DbSet<SubjectNotification> SubjectNotifications => Set<SubjectNotification>();
    public DbSet<EproSubmissionDefeated> EproSubmissionDefeateds => Set<EproSubmissionDefeated>();
    public DbSet<ClinicalQuery> ClinicalQueries => Set<ClinicalQuery>();
    public DbSet<EproSubmissionQuarantine> EproSubmissionQuarantines => Set<EproSubmissionQuarantine>();
    public DbSet<EhrEndpointRecord> EhrEndpointRecords => Set<EhrEndpointRecord>();
    public DbSet<EhrReconciliationCandidateRecord> EhrReconciliationCandidateRecords => Set<EhrReconciliationCandidateRecord>();
    public DbSet<DeviceRegistrationRecord> DeviceRegistrationRecords => Set<DeviceRegistrationRecord>();
    public DbSet<SensorTelemetryStreamRecord> SensorTelemetryStreamRecords => Set<SensorTelemetryStreamRecord>();
    public DbSet<SensorDailySummaryRecord> SensorDailySummaryRecords => Set<SensorDailySummaryRecord>();
    public DbSet<SensorSafetyAlertRecord> SensorSafetyAlertRecords => Set<SensorSafetyAlertRecord>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<StatisticalProgram> StatisticalPrograms => Set<StatisticalProgram>();
    public DbSet<DatabaseLockAuthToken> DatabaseLockAuthTokens => Set<DatabaseLockAuthToken>();
    public DbSet<ExecutionRunRecord> ExecutionRunRecords => Set<ExecutionRunRecord>();
    public DbSet<BiostatExportAuditLog> BiostatExportAuditLogs => Set<BiostatExportAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new EproSubmissionConfiguration());
        modelBuilder.ApplyConfiguration(new InteropAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new InstrumentConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectNotificationConfiguration());
        modelBuilder.ApplyConfiguration(new EproSubmissionDefeatedConfiguration());
        modelBuilder.ApplyConfiguration(new ClinicalQueryConfiguration());
        modelBuilder.ApplyConfiguration(new EproSubmissionQuarantineConfiguration());
        modelBuilder.ApplyConfiguration(new EhrEndpointRecordConfiguration());
        modelBuilder.ApplyConfiguration(new EhrReconciliationCandidateRecordConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceRegistrationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SensorTelemetryStreamRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SensorDailySummaryRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SensorSafetyAlertRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new StatisticalProgramConfiguration());
        modelBuilder.ApplyConfiguration(new DatabaseLockAuthTokenConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutionRunRecordConfiguration());
        modelBuilder.ApplyConfiguration(new BiostatExportAuditLogConfiguration());
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
