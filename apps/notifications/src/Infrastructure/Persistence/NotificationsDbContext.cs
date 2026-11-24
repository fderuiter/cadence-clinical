using System;

using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Tenancy;

using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Notifications.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Notifications microservice persistence gateway.
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);

    public DbSet<NotificationRecord> NotificationRecords => Set<NotificationRecord>();
    public DbSet<NotificationAuditLog> NotificationAuditLogs => Set<NotificationAuditLog>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<TransactionalOutboxMessage> TransactionalOutbox => Set<TransactionalOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new NotificationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionalOutboxMessageConfiguration());

        modelBuilder.AddTransactionalOutboxEntities();
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
