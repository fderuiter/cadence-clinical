using Cadence.Apps.Execution.Domain.Supply;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

public sealed class IpKitInventoryItemConfiguration : IEntityTypeConfiguration<IpKitInventoryItem>
{
    public void Configure(EntityTypeBuilder<IpKitInventoryItem> builder)
    {
        builder.ToTable("ip_kit_inventory_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.KitNumber).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.LotNumber).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DispositionReason).HasMaxLength(255);
        builder.Property(x => x.LastReconciledBatchId).HasMaxLength(255);
        builder.Property(x => x.LastReconciledBy).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => new { x.StudyId, x.SiteId, x.KitNumber }).IsUnique();
    }
}

public sealed class IpKitReconciliationBatchConfiguration : IEntityTypeConfiguration<IpKitReconciliationBatch>
{
    public void Configure(EntityTypeBuilder<IpKitReconciliationBatch> builder)
    {
        builder.ToTable("ip_kit_reconciliation_batches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BatchNumber).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProcessedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CertificateId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.ItemsJson).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => x.BatchNumber).IsUnique();
    }
}

public sealed class IpKitReconciliationCertificateConfiguration : IEntityTypeConfiguration<IpKitReconciliationCertificate>
{
    public void Configure(EntityTypeBuilder<IpKitReconciliationCertificate> builder)
    {
        builder.ToTable("ip_kit_reconciliation_certificates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CertificateId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.BatchNumber).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.IssuedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.KitNumbersJson).IsRequired();
        builder.Property(x => x.SummaryJson).IsRequired();
        builder.Property(x => x.DigitalSignature).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => x.CertificateId).IsUnique();
    }
}
