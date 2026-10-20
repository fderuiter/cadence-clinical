using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Fileshare.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Fileshare microservice persistence gateway.
/// </summary>
public sealed class FileshareDbContext(DbContextOptions<FileshareDbContext> options) : DbContext(options)
{
    public DbSet<FileRecord> FileRecords => Set<FileRecord>();
    public DbSet<ShareGrant> ShareGrants => Set<ShareGrant>();
    public DbSet<GuestLink> GuestLinks => Set<GuestLink>();
    public DbSet<PlaybackAuditLog> PlaybackAuditLogs => Set<PlaybackAuditLog>();
    public DbSet<FileAuditLog> FileAuditLogs => Set<FileAuditLog>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new FileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ShareGrantConfiguration());
        modelBuilder.ApplyConfiguration(new GuestLinkConfiguration());
        modelBuilder.ApplyConfiguration(new PlaybackAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new FileAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
    }
}
