using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core Fluent API mapping for <c>subject_activation_tokens</c>.
/// Stores only keyed HMAC-SHA256 digests (<see cref="SubjectActivationToken.TokenHash"/> and
/// <see cref="SubjectActivationToken.RedeemedByPrincipalHash"/>) and pseudonymous clinical codes.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectActivationTokenConfiguration : IEntityTypeConfiguration<SubjectActivationToken>
{
    public void Configure(EntityTypeBuilder<SubjectActivationToken> builder)
    {
        builder.ToTable("subject_activation_tokens");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.SubjectCode)
            .HasConversion(c => c.Value, v => new SubjectCode(v))
            .HasMaxLength(255)
            .IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.SubjectCode })
            .HasDatabaseName("IX_subject_activation_tokens_tenant_id_SubjectCode");

        builder.Property(x => x.StudyCode)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.SiteCode)
            .HasConversion(
                c => c.HasValue ? c.Value.Value : null,
                v => v == null ? (SiteCode?)null : new SiteCode(v))
            .HasMaxLength(255);

        builder.Property(x => x.TokenHash)
            .HasMaxLength(64)
            .IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();

        builder.Property(x => x.IssuedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.ReasonForChange)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(x => x.RedeemedByPrincipalHash)
            .HasMaxLength(64);
        builder.HasIndex(x => new { x.TenantId, x.RedeemedByPrincipalHash })
            .HasDatabaseName("IX_subject_activation_tokens_tenant_id_RedeemedByPrincipalHash");

        builder.Property<long>("_version").IsConcurrencyToken().HasDefaultValue(0L);
    }
}
