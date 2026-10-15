using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.observations</c> (spec §8.1, ADR-2620). An observation's revisions are always loaded
/// with it; the enrollment, participant and study are composite tenant foreign keys (ADR-2250).
/// </summary>
internal sealed class ObservationConfiguration : IEntityTypeConfiguration<Observation>
{
    public void Configure(EntityTypeBuilder<Observation> builder)
    {
        builder.ToTable("observations", t => t.HasCheckConstraint("ck_observations_revision", "revision > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Value).HasMaxLength(ObservationLimits.MaxValueLength).IsRequired();
        builder.Ignore(x => x.Concept);
        builder.HasIndex(x => new { x.TenantId, x.EnrollmentId });
        builder.HasIndex(x => new { x.TenantId, x.ParticipantId, x.ConceptId });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Enrollment>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EnrollmentId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Participant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Revisions).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.ObservationId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.observation_revisions</c>, insert-only in the database (<see cref="Commands.InsertOnlySql"/>).
/// Revision numbers are unique per observation.
/// </summary>
internal sealed class ObservationRevisionConfiguration : IEntityTypeConfiguration<ObservationRevision>
{
    public void Configure(EntityTypeBuilder<ObservationRevision> builder)
    {
        builder.ToTable("observation_revisions", t => t.HasCheckConstraint("ck_observation_revisions_number", "number > 0"));
        builder.HasKey(x => x.Id);

        // Revisions are created with their id; without this EF would treat one added to a loaded observation as an
        // existing row to update.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Value).HasMaxLength(ObservationLimits.MaxValueLength).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RecordedBy).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ObservationId, x.Number }).IsUnique();
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.observation_locks</c> (kernel Quality, ADR-2620): one row per locked observation,
/// keyed by the observation's UUID.
/// </summary>
internal sealed class ObservationLockConfiguration : IEntityTypeConfiguration<ObservationLock>
{
    public void Configure(EntityTypeBuilder<ObservationLock> builder)
    {
        builder.ToTable("observation_locks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.TenantId, x.ObservationId }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Observation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ObservationId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}
