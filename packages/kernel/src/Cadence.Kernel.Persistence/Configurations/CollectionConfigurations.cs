using Cadence.Kernel.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.collection_instances</c> (spec §8.1, K-FACT-6, ADR-2622). Its members are always loaded
/// with it. The composite tenant foreign keys prove the enrollment belongs to the participant and study site the row
/// names, and the study site to its study (ADR-2250).
/// </summary>
internal sealed class CollectionInstanceConfiguration : IEntityTypeConfiguration<CollectionInstance>
{
    public void Configure(EntityTypeBuilder<CollectionInstance> builder)
    {
        builder.ToTable("collection_instances");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CollectionSpecId).HasMaxLength(CollectionInstance.ElementIdMaxLength).IsRequired();
        builder.Property(x => x.PlannedActivityId).HasMaxLength(CollectionInstance.ElementIdMaxLength);
        builder.Property(x => x.OpenedBy).HasMaxLength(255).IsRequired();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Enrollment>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EnrollmentId, x.ParticipantId, x.StudySiteId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id, x.ParticipantId, x.StudySiteId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudySite>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudySiteId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id, x.StudyId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProtocolVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProtocolVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Members).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.CollectionInstanceId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Members).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.collection_instance_members</c>, insert-only in the database
/// (<see cref="Commands.InsertOnlySql"/>): an item has one observation per instance, and an observation belongs to one
/// instance.
/// </summary>
internal sealed class CollectionInstanceMemberConfiguration : IEntityTypeConfiguration<CollectionInstanceMember>
{
    public void Configure(EntityTypeBuilder<CollectionInstanceMember> builder)
    {
        builder.ToTable("collection_instance_members");
        builder.HasKey(x => x.Id);

        // Members are created with their id; without this EF would treat one added to a loaded instance as an existing
        // row to update.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ItemId).HasMaxLength(CollectionInstance.ElementIdMaxLength).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.CollectionInstanceId, x.ItemId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ObservationId }).IsUnique();
        builder.HasOne<Observation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ObservationId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
