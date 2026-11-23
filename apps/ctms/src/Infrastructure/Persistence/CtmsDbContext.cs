using System;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for CTMS microservice persistence gateway.
/// </summary>
public sealed class CtmsDbContext(DbContextOptions<CtmsDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);
    public DbSet<CtmsAuditLog> CtmsAuditLogs => Set<CtmsAuditLog>();
    public DbSet<CtmsClinicalQuery> CtmsClinicalQueries => Set<CtmsClinicalQuery>();
    public DbSet<CtmsCraAllocation> CtmsCraAllocations => Set<CtmsCraAllocation>();
    public DbSet<CtmsDefeatedMonitoringVisit> CtmsDefeatedMonitoringVisits => Set<CtmsDefeatedMonitoringVisit>();
    public DbSet<CtmsDelegation> CtmsDelegations => Set<CtmsDelegation>();
    public DbSet<CtmsDelegationPeriod> CtmsDelegationPeriods => Set<CtmsDelegationPeriod>();
    public DbSet<CtmsGeneratedLetter> CtmsGeneratedLetters => Set<CtmsGeneratedLetter>();
    public DbSet<CtmsInvestigatorGrant> CtmsInvestigatorGrants => Set<CtmsInvestigatorGrant>();
    public DbSet<CtmsMonitoringVisitFinding> CtmsMonitoringVisitFindings => Set<CtmsMonitoringVisitFinding>();
    public DbSet<CtmsMonitoringVisit> CtmsMonitoringVisits => Set<CtmsMonitoringVisit>();
    public DbSet<CtmsRecruitmentRecord> CtmsRecruitmentRecords => Set<CtmsRecruitmentRecord>();
    public DbSet<CtmsRegulatoryForm> CtmsRegulatoryForms => Set<CtmsRegulatoryForm>();
    public DbSet<CtmsSiteMilestone> CtmsSiteMilestones => Set<CtmsSiteMilestone>();
    public DbSet<CtmsBudgetLineItem> CtmsBudgetLineItems => Set<CtmsBudgetLineItem>();
    public DbSet<CtmsPaymentMilestone> CtmsPaymentMilestones => Set<CtmsPaymentMilestone>();
    public DbSet<DoaDelegationRecord> DoaDelegationRecords => Set<DoaDelegationRecord>();
    public DbSet<DoaDelegationPeriod> DoaDelegationPeriods => Set<DoaDelegationPeriod>();
    public DbSet<CtmsInvestigatorPayable> CtmsInvestigatorPayables => Set<CtmsInvestigatorPayable>();
    public DbSet<CtmsSiteGreenlightGate> CtmsSiteGreenlightGates => Set<CtmsSiteGreenlightGate>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<TransactionalOutboxMessage> TransactionalOutbox => Set<TransactionalOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new CtmsAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsClinicalQueryConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsCraAllocationConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsDefeatedMonitoringVisitConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsDelegationConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsDelegationPeriodConfiguration(this));
        modelBuilder.ApplyConfiguration(new CtmsGeneratedLetterConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsInvestigatorGrantConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsMonitoringVisitFindingConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsMonitoringVisitConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsRecruitmentRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsRegulatoryFormConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsSiteMilestoneConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsBudgetLineItemConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsPaymentMilestoneConfiguration());
        modelBuilder.ApplyConfiguration(new DoaDelegationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new DoaDelegationPeriodConfiguration(this));
        modelBuilder.ApplyConfiguration(new CtmsInvestigatorPayableConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsSiteGreenlightGateConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionalOutboxMessageConfiguration());
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
