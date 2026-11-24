using System;

using Cadence.Apps.Quality.Domain;
using Cadence.Apps.Quality.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Quality.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Quality microservice persistence gateway.
/// </summary>
public sealed class QualityDbContext(DbContextOptions<QualityDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);

    public DbSet<QualityDeviation> QualityDeviations => Set<QualityDeviation>();
    public DbSet<QualityRootCauseAnalysis> QualityRootCauseAnalyses => Set<QualityRootCauseAnalysis>();
    public DbSet<QualityCapaRecord> QualityCapaRecords => Set<QualityCapaRecord>();
    public DbSet<QualityAuditLog> QualityAuditLogs => Set<QualityAuditLog>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<TransactionalOutboxMessage> TransactionalOutbox => Set<TransactionalOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new QualityDeviationConfiguration());
        modelBuilder.ApplyConfiguration(new QualityRootCauseAnalysisConfiguration());
        modelBuilder.ApplyConfiguration(new QualityCapaRecordConfiguration());
        modelBuilder.ApplyConfiguration(new QualityAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionalOutboxMessageConfiguration());
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
