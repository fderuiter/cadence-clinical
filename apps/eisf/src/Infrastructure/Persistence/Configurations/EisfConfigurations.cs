using Cadence.Apps.Eisf.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Eisf.Infrastructure.Persistence.Configurations;

public class IsfDocumentConfiguration : IEntityTypeConfiguration<IsfDocument>
{
    public void Configure(EntityTypeBuilder<IsfDocument> builder)
    {
        builder.ToTable("isf_documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.BinderClassification).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.BinderClassification);
        builder.Property(x => x.Filename).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ObjectKey).HasMaxLength(500);
        builder.HasIndex(x => x.ObjectKey);
        builder.Property(x => x.MimeType).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.IssueDate);
        builder.HasIndex(x => x.ExpirationDate);
        builder.Property(x => x.DocumentOwnerId).HasMaxLength(255);
        builder.HasIndex(x => x.DocumentOwnerId);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
        builder.Property(x => x.CorrelationKey).HasMaxLength(255);
        builder.HasIndex(x => x.CorrelationKey);
        builder.Property(x => x.ContentChecksum).HasMaxLength(64);
        builder.Property(x => x.SyncStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
    }
}

public class IsfAuditLogConfiguration : IEntityTypeConfiguration<IsfAuditLog>
{
    public void Configure(EntityTypeBuilder<IsfAuditLog> builder)
    {
        builder.ToTable("isf_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.HasIndex(x => x.Timestamp);
        builder.Property(x => x.ActorId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ActorId);
        builder.Property(x => x.ActorRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DocumentId).HasMaxLength(36);
        builder.HasIndex(x => x.DocumentId);
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PreviousDigest).HasMaxLength(64);
        builder.Property(x => x.Sha256Digest).HasMaxLength(64);
    }
}

public class EisfSectionTaxonomyConfiguration : IEntityTypeConfiguration<EisfSectionTaxonomy>
{
    public void Configure(EntityTypeBuilder<EisfSectionTaxonomy> builder)
    {
        builder.ToTable("eisf_section_taxonomies");
        builder.HasKey(x => x.SectionCode);
        builder.Property(x => x.SectionCode).HasMaxLength(50);
        builder.Property(x => x.SectionNumber).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.SectionNumber);
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000).IsRequired();
    }
}

public class EisfDocumentRecordConfiguration : IEntityTypeConfiguration<EisfDocumentRecord>
{
    public void Configure(EntityTypeBuilder<EisfDocumentRecord> builder)
    {
        builder.ToTable("eisf_document_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SectionCode).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.SectionCode);
        builder.Property(x => x.Filename).HasMaxLength(255).IsRequired();
        builder.Property(x => x.FilePath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Sha256Checksum).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}
