using Cadence.Apps.Etmf.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Etmf.Infrastructure.Persistence.Configurations;

public sealed class TmfDocumentConfiguration : IEntityTypeConfiguration<TmfDocument>
{
    public void Configure(EntityTypeBuilder<TmfDocument> builder)
    {
        builder.ToTable("tmf_documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(255);
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.Property(x => x.Zone).IsRequired();
        builder.Property(x => x.Section).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ArtifactType).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Filename).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TaxonomyVersion).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ArtifactCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
        builder.Property(x => x.DocumentOwnerId).HasMaxLength(255);
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
        builder.Property(x => x.ProtocolVersionTag).HasMaxLength(50);
        builder.Property(x => x.ProtocolVersionStatus).HasMaxLength(50);
        builder.Property(x => x.DocumentType).HasMaxLength(50);
        builder.Property(x => x.ApprovalStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SignatureManifestation).HasColumnType("jsonb");
        builder.Property(x => x.Signer).HasMaxLength(255);
        builder.Property(x => x.IsRedacted).IsRequired();
        builder.Property(x => x.RedactionSourceId).HasMaxLength(36);
        builder.Property(x => x.RedactionManifestJson).HasColumnType("jsonb");
        builder.Property(x => x.CorrelationKey).HasMaxLength(255);
        builder.Property(x => x.ContentChecksum).HasMaxLength(64);
        builder.Property(x => x.SourceSystem).HasMaxLength(255);
        builder.Property(x => x.SyncStatus).HasMaxLength(50);
        builder.Property(x => x.ObjectKey).HasMaxLength(1000);

        builder.HasIndex(x => x.ArtifactCode);
        builder.HasIndex(x => x.ArtifactType);
        builder.HasIndex(x => x.CorrelationKey);
        builder.HasIndex(x => x.DocumentOwnerId);
        builder.HasIndex(x => x.DocumentType);
        builder.HasIndex(x => x.ExpirationDate);
        builder.HasIndex(x => x.IdempotencyKey);
        builder.HasIndex(x => x.IssueDate);
        builder.HasIndex(x => x.RedactionSourceId);
        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
        builder.HasIndex(x => x.Zone);
        builder.HasIndex(x => x.ObjectKey);
    }
}

public sealed class TmfExpectedDocumentConfiguration : IEntityTypeConfiguration<TmfExpectedDocument>
{
    public void Configure(EntityTypeBuilder<TmfExpectedDocument> builder)
    {
        builder.ToTable("tmf_expected_documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.Property(x => x.Milestone).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ArtifactType).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Section).HasMaxLength(255);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.ArtifactType);
        builder.HasIndex(x => x.Milestone);
        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class TmfAuditLedgerSealConfiguration : IEntityTypeConfiguration<TmfAuditLedgerSeal>
{
    public void Configure(EntityTypeBuilder<TmfAuditLedgerSeal> builder)
    {
        builder.ToTable("tmf_audit_ledger_seals");
        builder.HasKey(x => x.BlockIndex);
        builder.Property(x => x.BlockIndex).ValueGeneratedOnAdd();
        builder.Property(x => x.PreviousBlockHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CurrentBlockHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.SealedRecordCount).IsRequired();
        builder.Property(x => x.MerkleRootHash).HasMaxLength(64).IsRequired();
    }
}

public sealed class TmfAuditLogConfiguration : IEntityTypeConfiguration<TmfAuditLog>
{
    public void Configure(EntityTypeBuilder<TmfAuditLog> builder)
    {
        builder.ToTable("tmf_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.UserRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DocumentId).HasMaxLength(36);
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CryptographicSeal).HasMaxLength(64);
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);

        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.UserId);
    }
}

public sealed class TmfDocumentExpirationAlertStateConfiguration : IEntityTypeConfiguration<TmfDocumentExpirationAlertState>
{
    public void Configure(EntityTypeBuilder<TmfDocumentExpirationAlertState> builder)
    {
        builder.ToTable("tmf_document_expiration_alert_states");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.DocumentId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.WarningWindow).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AlertedAt).IsRequired();
        builder.Property(x => x.Dispatched).IsRequired();
        builder.Property(x => x.NotificationId).HasMaxLength(255);
        builder.Property(x => x.Attempts).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasOne<TmfDocument>()
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.DocumentId, x.WarningWindow }).IsUnique();
        builder.HasIndex(x => x.Dispatched);
        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => x.NotificationId);
        builder.HasIndex(x => x.WarningWindow);
    }
}

public sealed class TmfDocumentQcTransitionConfiguration : IEntityTypeConfiguration<TmfDocumentQcTransition>
{
    public void Configure(EntityTypeBuilder<TmfDocumentQcTransition> builder)
    {
        builder.ToTable("tmf_document_qc_transitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.DocumentId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.TransitionSequence).IsRequired();
        builder.Property(x => x.FromStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ToStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ActorRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Timestamp).IsRequired();

        builder.HasOne<TmfDocument>()
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.DocumentId, x.TransitionSequence }).IsUnique();
        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => x.Timestamp);
    }
}
