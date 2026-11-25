using System.Text.Json;
using System.Text.Json.Serialization;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.protocol_versions</c> (spec §6.2). Numbers are unique per study; the parent and the
/// study are composite tenant foreign keys (ADR-2250). The content is stored as the exact text that was hashed, and
/// the <c>ProtocolVersions</c> migration adds the lifecycle trigger in <see cref="Protocol.ProtocolVersionSql"/>.
/// </summary>
internal sealed class ProtocolVersionConfiguration : IEntityTypeConfiguration<ProtocolVersion>
{
    public void Configure(EntityTypeBuilder<ProtocolVersion> builder)
    {
        builder.ToTable("protocol_versions", t =>
        {
            t.HasCheckConstraint("ck_protocol_versions_number", "number > 0");
            t.HasCheckConstraint("ck_protocol_versions_content_hash", $"content_hash IS NULL OR content_hash ~ '{ContentHash.Pattern}'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Content).HasColumnType("text").IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(ContentHash.Length);
        builder.Property(x => x.Amendment)
            .HasColumnType("jsonb")
            .HasConversion(AmendmentJson.Converter, AmendmentJson.Comparer);
        builder.Property(x => x.WithdrawalReason).HasMaxLength(1000);
        builder.Ignore(x => x.SignableTarget);
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.Number }).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProtocolVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParentVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attestation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ApprovalAttestationId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attestation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.WithdrawalAttestationId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }

    /// <summary>Stores an amendment as JSON with enums by name.</summary>
    internal static class AmendmentJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        public static readonly ValueConverter<ProtocolAmendment?, string> Converter = new(
            amendment => JsonSerializer.Serialize(amendment, Options),
            json => JsonSerializer.Deserialize<ProtocolAmendment>(json, Options));

        public static readonly ValueComparer<ProtocolAmendment?> Comparer = new(
            (left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
            amendment => JsonSerializer.Serialize(amendment, Options).GetHashCode(System.StringComparison.Ordinal),
            amendment => JsonSerializer.Deserialize<ProtocolAmendment>(JsonSerializer.Serialize(amendment, Options), Options));
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.attestations</c> (spec §9.5), insert-only in the database and indexed by the record
/// each attestation signs.
/// </summary>
internal sealed class AttestationConfiguration : IEntityTypeConfiguration<Attestation>
{
    public void Configure(EntityTypeBuilder<Attestation> builder)
    {
        builder.ToTable("attestations", t =>
            t.HasCheckConstraint("ck_attestations_target_content_hash", $"target_content_hash ~ '{ContentHash.Pattern}'"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Meaning).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetKind).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetId).IsRequired();
        builder.Property(x => x.TargetContentHash).HasMaxLength(ContentHash.Length).IsRequired();
        builder.Ignore(x => x.Target);
        builder.Property(x => x.AuthenticatedAt).IsRequired();
        builder.Property(x => x.Method).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RecordedAt).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.TargetKind, x.TargetId });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}
