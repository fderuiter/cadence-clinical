using Cadence.Apps.Org.Domain;
using Cadence.Apps.Org.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Org.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Org microservice persistence gateway.
/// @req:PRD-SYS-053
/// </summary>
public sealed class OrgDbContext(DbContextOptions<OrgDbContext> options) : DbContext(options)
{
    public DbSet<PersonnelAssignment> PersonnelAssignments => Set<PersonnelAssignment>();
    public DbSet<DelegationOfAuthority> DelegationsOfAuthority => Set<DelegationOfAuthority>();
    public DbSet<TrainingLog> TrainingLogs => Set<TrainingLog>();
    public DbSet<OrgAuditLog> OrgAuditLogs => Set<OrgAuditLog>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new PersonnelAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new DelegationOfAuthorityConfiguration());
        modelBuilder.ApplyConfiguration(new TrainingLogConfiguration());
        modelBuilder.ApplyConfiguration(new OrgAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
    }
}
