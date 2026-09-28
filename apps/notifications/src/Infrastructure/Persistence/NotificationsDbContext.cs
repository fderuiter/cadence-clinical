using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Notifications.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Notifications microservice persistence gateway.
/// @req:PRD-SYS-053
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<NotificationRecord> NotificationRecords => Set<NotificationRecord>();
    public DbSet<NotificationAuditLog> NotificationAuditLogs => Set<NotificationAuditLog>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new NotificationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
    }
}
