using Cadence.Apps.Fileshare.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Fileshare.Infrastructure.Persistence.Configurations;

public sealed class FileRecordConfiguration : IEntityTypeConfiguration<FileRecord>
{
    public void Configure(EntityTypeBuilder<FileRecord> builder)
    {
        builder.ToTable("file_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.Property(x => x.Filename).HasMaxLength(500).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.ObjectKey).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ChecksumSha256).HasMaxLength(64);
        builder.Property(x => x.VersionIndex).IsRequired();
        builder.Property(x => x.UploadedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.UploadedAt).IsRequired();
        builder.Property(x => x.IsOnHold).IsRequired();
        builder.Property(x => x.HoldReason).HasMaxLength(1000);
        builder.Property(x => x.IsDeleted).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ScanStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ScanEngine).HasMaxLength(100);
        builder.Property(x => x.ScanThreatName).HasMaxLength(255);
        builder.Property(x => x.TranscodeStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.HlsManifestKey).HasMaxLength(1000);
        builder.Property(x => x.TranscodeError).HasMaxLength(2000);
        builder.Property(x => x.PdfRenditionKey).HasMaxLength(1000);
        builder.Property(x => x.ConversionStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ConversionError).HasMaxLength(2000);
        builder.Property(x => x.ApprovalStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SignatureManifestation).HasColumnType("jsonb");
        builder.Property(x => x.ApprovedBy).HasMaxLength(255);
        builder.Property(x => x.ApprovalReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => x.ApprovedBy);
        builder.HasIndex(x => x.ChecksumSha256);
        builder.HasIndex(x => x.ConversionStatus);
        builder.HasIndex(x => x.ObjectKey).IsUnique();
        builder.HasIndex(x => x.ScanStatus);
        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.StudyId);
        builder.HasIndex(x => x.TranscodeStatus);
        builder.HasIndex(x => x.UploadedBy);
    }
}

public sealed class ShareGrantConfiguration : IEntityTypeConfiguration<ShareGrant>
{
    public void Configure(EntityTypeBuilder<ShareGrant> builder)
    {
        builder.ToTable("share_grants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.FileRecordId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.GrantedToUserId).HasMaxLength(255);
        builder.Property(x => x.GrantedByUserId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PermissionLevel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IsDeleted).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(x => x.FileRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.FileRecordId);
        builder.HasIndex(x => x.GrantedToUserId);
    }
}

public sealed class GuestLinkConfiguration : IEntityTypeConfiguration<GuestLink>
{
    public void Configure(EntityTypeBuilder<GuestLink> builder)
    {
        builder.ToTable("guest_links");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.FileRecordId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.TokenHmac).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AccessCount).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(x => x.FileRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.FileRecordId);
        builder.HasIndex(x => x.TokenHmac).IsUnique();
    }
}

public sealed class PlaybackAuditLogConfiguration : IEntityTypeConfiguration<PlaybackAuditLog>
{
    public void Configure(EntityTypeBuilder<PlaybackAuditLog> builder)
    {
        builder.ToTable("playback_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.FileRecordId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.Event).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SessionId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(x => x.FileRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Event);
        builder.HasIndex(x => x.FileRecordId);
        builder.HasIndex(x => x.SessionId);
        builder.HasIndex(x => x.UserId);
    }
}

public sealed class FileAuditLogConfiguration : IEntityTypeConfiguration<FileAuditLog>
{
    public void Configure(EntityTypeBuilder<FileAuditLog> builder)
    {
        builder.ToTable("file_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.FileRecordId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Detail).HasColumnType("jsonb");

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(x => x.FileRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.Action);
        builder.HasIndex(x => x.FileRecordId);
        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.UserId);
    }
}
