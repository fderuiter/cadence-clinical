using System;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Tenancy;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Etmf.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for eTMF microservice persistence gateway.
/// </summary>
public sealed class EtmfDbContext(DbContextOptions<EtmfDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);

    public DbSet<TmfDocument> TmfDocuments => Set<TmfDocument>();
    public DbSet<TmfDocumentRevision> TmfDocumentRevisions => Set<TmfDocumentRevision>();
    public DbSet<TmfExpectedDocument> TmfExpectedDocuments => Set<TmfExpectedDocument>();
    public DbSet<TmfAuditLedgerSeal> TmfAuditLedgerSeals => Set<TmfAuditLedgerSeal>();
    public DbSet<TmfAuditLog> TmfAuditLogs => Set<TmfAuditLog>();
    public DbSet<TmfDocumentExpirationAlertState> TmfDocumentExpirationAlertStates => Set<TmfDocumentExpirationAlertState>();
    public DbSet<TmfDocumentQcTransition> TmfDocumentQcTransitions => Set<TmfDocumentQcTransition>();
    public DbSet<TmfExportJob> TmfExportJobs => Set<TmfExportJob>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<TransactionalOutboxMessage> TransactionalOutbox => Set<TransactionalOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new TmfDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new TmfDocumentRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new TmfExpectedDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new TmfAuditLedgerSealConfiguration());
        modelBuilder.ApplyConfiguration(new TmfAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new TmfDocumentExpirationAlertStateConfiguration());
        modelBuilder.ApplyConfiguration(new TmfDocumentQcTransitionConfiguration());
        modelBuilder.ApplyConfiguration(new TmfExportJobConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionalOutboxMessageConfiguration());

        modelBuilder.AddTransactionalOutboxEntities();
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
