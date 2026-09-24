using Cadence.Apps.Notifications.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Notifications.Infrastructure.Persistence.Configurations;

public class NotificationRecordConfiguration : IEntityTypeConfiguration<NotificationRecord>
{
    public void Configure(EntityTypeBuilder<NotificationRecord> builder)
    {
        builder.ToTable("notification_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.RecipientUserId).HasMaxLength(255);
        builder.HasIndex(x => x.RecipientUserId);
        builder.Property(x => x.RecipientRoleId).HasMaxLength(255);
        builder.HasIndex(x => x.RecipientRoleId);
        builder.Property(x => x.Category).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Channels).HasMaxLength(255).IsRequired();
        builder.Property(x => x.RelatedEntityId).HasMaxLength(255);
        builder.Property(x => x.RelatedEntityType).HasMaxLength(255);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DeliveryState).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class NotificationAuditLogConfiguration : IEntityTypeConfiguration<NotificationAuditLog>
{
    public void Configure(EntityTypeBuilder<NotificationAuditLog> builder)
    {
        builder.ToTable("notification_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.HasIndex(x => x.Timestamp);
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.UserId);
        builder.Property(x => x.UserRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();
    }
}

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.NotificationId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.NotificationId);
        builder.Property(x => x.Channel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(1000);
    }
}
