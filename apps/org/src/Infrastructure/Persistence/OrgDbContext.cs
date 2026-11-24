using System;

using Cadence.Apps.Org.Domain;
using Cadence.Apps.Org.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Org.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Org microservice persistence gateway.
/// </summary>
public sealed class OrgDbContext(DbContextOptions<OrgDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);

    public DbSet<PersonnelAssignment> PersonnelAssignments => Set<PersonnelAssignment>();
    public DbSet<DelegationOfAuthority> DelegationsOfAuthority => Set<DelegationOfAuthority>();
    public DbSet<DelegationOfAuthorityPeriod> DelegationOfAuthorityPeriods => Set<DelegationOfAuthorityPeriod>();
    public DbSet<TrainingLog> TrainingLogs => Set<TrainingLog>();
    public DbSet<OrgAuditLog> OrgAuditLogs => Set<OrgAuditLog>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<TransactionalOutboxMessage> TransactionalOutbox => Set<TransactionalOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new PersonnelAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new DelegationOfAuthorityConfiguration());
        modelBuilder.ApplyConfiguration(new DelegationOfAuthorityPeriodConfiguration(this));
        modelBuilder.ApplyConfiguration(new TrainingLogConfiguration());
        modelBuilder.ApplyConfiguration(new OrgAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionalOutboxMessageConfiguration());
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
