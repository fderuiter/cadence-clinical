using Cadence.Apps.Execution.Domain.Forms;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="PublishedFormVersionRegistry"/>.
/// @req:PRD-SYS-053
/// </summary>
public sealed class PublishedFormVersionRegistryConfiguration : IEntityTypeConfiguration<PublishedFormVersionRegistry>
{
    public void Configure(EntityTypeBuilder<PublishedFormVersionRegistry> builder)
    {
        builder.ToTable("published_form_version_registries");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.StudyId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.ProtocolVersionId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.ProtocolVersion)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.FormId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.FormHash)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.OpenRosaXml)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(r => r.DdfIr)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(r => r.PublishedAt)
            .IsRequired();

        builder.HasIndex(r => new { r.StudyId, r.ProtocolVersion, r.FormId })
            .IsUnique();
    }
}
