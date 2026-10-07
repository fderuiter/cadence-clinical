using System;

using Cadence.Apps.Execution.Domain.Devices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

public sealed class DeviceInventoryItemConfiguration : IEntityTypeConfiguration<DeviceInventoryItem>
{
    public void Configure(EntityTypeBuilder<DeviceInventoryItem> builder)
    {
        builder.ToTable("device_inventory_items");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StudyId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DeviceIdentifier).HasMaxLength(256).IsRequired();
        builder.Property(x => x.BrandName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DeviceModel).HasMaxLength(128);
        builder.Property(x => x.DeviceType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DeviceClassification).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UdiDi).HasMaxLength(128).IsRequired();
        builder.Property(x => x.UdiPi).HasMaxLength(256);
        builder.Property(x => x.UdiCarrierFormat).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LotNumber).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SerialNumber).HasMaxLength(128);
        builder.Property(x => x.SoftwareVersion).HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AllocatedSubjectId).HasMaxLength(128);
        builder.Property(x => x.DispensedBy).HasMaxLength(128);
        builder.Property(x => x.QuarantinedBy).HasMaxLength(128);

        builder.HasIndex(x => new { x.StudyId, x.SiteId });
        builder.HasIndex(x => new { x.StudyId, x.Status });
        builder.HasIndex(x => new { x.UdiDi, x.LotNumber });
        builder.HasIndex(x => x.SerialNumber);
    }
}

public sealed class DeviceDeficiencyConfiguration : IEntityTypeConfiguration<DeviceDeficiency>
{
    public void Configure(EntityTypeBuilder<DeviceDeficiency> builder)
    {
        builder.ToTable("device_deficiencies");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StudyId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SubjectId).HasMaxLength(128);
        builder.Property(x => x.DeviceInventoryId).HasMaxLength(128);
        builder.Property(x => x.UdiDi).HasMaxLength(128).IsRequired();
        builder.Property(x => x.LotNumber).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SerialNumber).HasMaxLength(128);
        builder.Property(x => x.DeviceName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DeficiencyType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DeficiencyDescription).HasMaxLength(4096).IsRequired();
        builder.Property(x => x.HealthImpact).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActionTakenDevice).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReturnTrackingNumber).HasMaxLength(128);
        builder.Property(x => x.QuarantineScope).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SafetyCaseId).HasMaxLength(128);
        builder.Property(x => x.SafetyClassification).HasMaxLength(128);
        builder.Property(x => x.InvestigationStatus).HasMaxLength(64).IsRequired();

        builder.HasIndex(x => new { x.StudyId, x.SiteId });
        builder.HasIndex(x => x.SafetyCaseId);
    }
}

public sealed class DeviceQuarantineRecordConfiguration : IEntityTypeConfiguration<DeviceQuarantineRecord>
{
    public void Configure(EntityTypeBuilder<DeviceQuarantineRecord> builder)
    {
        builder.ToTable("device_quarantine_records");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StudyId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DeficiencyId).HasMaxLength(128);
        builder.Property(x => x.DeviceInventoryId).HasMaxLength(128);
        builder.Property(x => x.Scope).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LotNumber).HasMaxLength(128);
        builder.Property(x => x.SerialNumber).HasMaxLength(128);
        builder.Property(x => x.Reason).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(64).IsRequired();
        builder.Property(x => x.QuarantinedBy).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ReleasedBy).HasMaxLength(128);
        builder.Property(x => x.ReleaseJustification).HasMaxLength(2048);

        builder.HasIndex(x => new { x.StudyId, x.Status });
        builder.HasIndex(x => x.DeviceInventoryId);
    }
}
