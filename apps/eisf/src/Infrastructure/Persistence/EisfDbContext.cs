using System;

using Cadence.Apps.Eisf.Domain;
using Cadence.Apps.Eisf.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Eisf.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for eISF microservice persistence gateway.
/// </summary>
public sealed class EisfDbContext(DbContextOptions<EisfDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);

    public DbSet<IsfDocument> IsfDocuments => Set<IsfDocument>();
    public DbSet<IsfAuditLog> IsfAuditLogs => Set<IsfAuditLog>();
    public DbSet<EisfSectionTaxonomy> EisfSectionTaxonomies => Set<EisfSectionTaxonomy>();
    public DbSet<EisfDocumentRecord> EisfDocumentRecords => Set<EisfDocumentRecord>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new IsfDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new IsfAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new EisfSectionTaxonomyConfiguration());
        modelBuilder.ApplyConfiguration(new EisfDocumentRecordConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
