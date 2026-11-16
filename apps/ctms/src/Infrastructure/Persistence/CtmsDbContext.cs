using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for CTMS microservice persistence gateway.
/// </summary>
public sealed class CtmsDbContext : DbContext
{
    private readonly IDomainEventDispatcher? _eventDispatcher;
    private readonly List<object> _dispatchedEvents = [];

    public IReadOnlyList<object> DispatchedEvents => _dispatchedEvents.AsReadOnly();

    public CtmsDbContext(DbContextOptions<CtmsDbContext> options)
        : this(options, null)
    {
    }

    public CtmsDbContext(DbContextOptions<CtmsDbContext> options, IDomainEventDispatcher? eventDispatcher)
        : base(options)
    {
        _eventDispatcher = eventDispatcher;
    }

    public DbSet<CtmsAuditLog> CtmsAuditLogs => Set<CtmsAuditLog>();
    public DbSet<CtmsClinicalQuery> CtmsClinicalQueries => Set<CtmsClinicalQuery>();
    public DbSet<CtmsCraAllocation> CtmsCraAllocations => Set<CtmsCraAllocation>();
    public DbSet<CtmsDefeatedMonitoringVisit> CtmsDefeatedMonitoringVisits => Set<CtmsDefeatedMonitoringVisit>();
    public DbSet<CtmsDelegation> CtmsDelegations => Set<CtmsDelegation>();
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
    public DbSet<CtmsInvestigatorPayable> CtmsInvestigatorPayables => Set<CtmsInvestigatorPayable>();
    public DbSet<CtmsSiteGreenlightGate> CtmsSiteGreenlightGates => Set<CtmsSiteGreenlightGate>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<TransactionalOutboxMessage> TransactionalOutbox => Set<TransactionalOutboxMessage>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var gatesWithEvents = ChangeTracker.Entries<CtmsSiteGreenlightGate>()
            .Where(e => e.Entity.UncommittedEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var eventsToDispatch = gatesWithEvents.SelectMany(g => g.UncommittedEvents).ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        if (eventsToDispatch.Count > 0)
        {
            _dispatchedEvents.AddRange(eventsToDispatch);
            if (_eventDispatcher != null)
            {
                await _eventDispatcher.DispatchAsync(eventsToDispatch, cancellationToken);
            }
            foreach (var gate in gatesWithEvents)
            {
                gate.FlushUncommittedEvents();
            }
        }

        return result;
    }

    public override int SaveChanges()
    {
        var gatesWithEvents = ChangeTracker.Entries<CtmsSiteGreenlightGate>()
            .Where(e => e.Entity.UncommittedEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var eventsToDispatch = gatesWithEvents.SelectMany(g => g.UncommittedEvents).ToList();

        var result = base.SaveChanges();

        if (eventsToDispatch.Count > 0)
        {
            _dispatchedEvents.AddRange(eventsToDispatch);
            if (_eventDispatcher != null)
            {
                _eventDispatcher.DispatchAsync(eventsToDispatch).GetAwaiter().GetResult();
            }
            foreach (var gate in gatesWithEvents)
            {
                gate.FlushUncommittedEvents();
            }
        }

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new CtmsAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsClinicalQueryConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsCraAllocationConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsDefeatedMonitoringVisitConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsDelegationConfiguration());
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
        modelBuilder.ApplyConfiguration(new CtmsInvestigatorPayableConfiguration());
        modelBuilder.ApplyConfiguration(new CtmsSiteGreenlightGateConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionalOutboxMessageConfiguration());
    }
}
