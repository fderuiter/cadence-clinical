using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.artifacts</c> (spec §8.3). An artifact's versions are always loaded with it; the
/// optional study is a composite tenant foreign key (ADR-2250).
/// </summary>
internal sealed class ArtifactConfiguration : IEntityTypeConfiguration<Artifact>
{
    public void Configure(EntityTypeBuilder<Artifact> builder)
    {
        builder.ToTable("artifacts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(Artifact.MaxTitleLength).IsRequired();
        builder.Ignore(x => x.LatestVersion);
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.Kind });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Versions).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.ArtifactId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.artifact_versions</c>, insert-only in the database (<see cref="Commands.InsertOnlySql"/>).
/// Numbers and content hashes are unique per artifact.
/// </summary>
internal sealed class ArtifactVersionConfiguration : IEntityTypeConfiguration<ArtifactVersion>
{
    public void Configure(EntityTypeBuilder<ArtifactVersion> builder)
    {
        builder.ToTable("artifact_versions", t =>
        {
            t.HasCheckConstraint("ck_artifact_versions_number", "number > 0");
            t.HasCheckConstraint("ck_artifact_versions_size", "size_bytes > 0");
            t.HasCheckConstraint("ck_artifact_versions_content_hash", $"content_hash ~ '{ContentHash.Pattern}'");
        });
        builder.HasKey(x => x.Id);

        // Versions are created with their id; without this EF would treat a version added to a loaded artifact as an
        // existing row to update.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ContentHash).HasMaxLength(ContentHash.Length).IsRequired();
        builder.Property(x => x.MediaType).HasMaxLength(ArtifactContent.MaxMediaTypeLength).IsRequired();
        builder.Property(x => x.StorageRef).HasMaxLength(ArtifactContent.MaxStorageRefLength).IsRequired();
        builder.Ignore(x => x.SignableTarget);
        builder.HasIndex(x => new { x.TenantId, x.ArtifactId, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ArtifactId, x.ContentHash }).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
    }
}
