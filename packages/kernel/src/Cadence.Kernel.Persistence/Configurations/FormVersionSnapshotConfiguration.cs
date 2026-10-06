using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.form_version_snapshots</c> (Requirement 1).
/// Persists compiled USDM OpenRosa XForms schemas and DDF IRs for approved protocol versions.
/// </summary>
internal sealed class FormVersionSnapshotConfiguration : IEntityTypeConfiguration<FormVersionSnapshot>
{
    public void Configure(EntityTypeBuilder<FormVersionSnapshot> builder)
    {
        builder.ToTable("form_version_snapshots", t =>
        {
            t.HasCheckConstraint("ck_form_version_snapshots_form_hash", $"form_hash ~ '{ContentHash.Pattern}'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProtocolVersion).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FormId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.FormHash).HasMaxLength(ContentHash.Length).IsRequired();
        builder.Property(x => x.OpenRosaXml).HasColumnType("text").IsRequired();
        builder.Property(x => x.DdfIr).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.OdmXml).HasColumnType("text");
        builder.Property(x => x.RecordedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.ProtocolVersion, x.FormId }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProtocolVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProtocolVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}
