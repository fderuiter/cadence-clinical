using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Evidence;

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
        builder.Property(x => x.Value).IsObservationValue();
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
        builder.Property(x => x.Value).IsObservationValue();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RecordedBy).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ObservationId, x.Number }).IsUnique();
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.assertions</c> (spec §8.2, §10.5, ADR-2621), insert-only in the database: a retraction is
/// a new row naming the one it retracts, and an assertion is retracted at most once. Locks are read by their scope.
/// </summary>
internal sealed class AssertionConfiguration : IEntityTypeConfiguration<Assertion>
{
    public void Configure(EntityTypeBuilder<Assertion> builder)
    {
        builder.ToTable("assertions", t =>
        {
            t.HasCheckConstraint("ck_assertions_target_revision", "target_revision IS NULL OR target_revision > 0");
            t.HasCheckConstraint("ck_assertions_lock_attested", "kind NOT IN ('Locked', 'Frozen') OR attestation_id IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetKind).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Text).HasMaxLength(AssertionText.MaxLength);
        builder.Property(x => x.AssertedBy).HasMaxLength(255).IsRequired();
        builder.Ignore(x => x.Target);
        builder.Ignore(x => x.IsLock);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.TargetId, x.Kind });
        builder.HasIndex(x => new { x.TenantId, x.EnrollmentId });
        builder.HasIndex(x => new { x.TenantId, x.RetractsId }).IsUnique().HasFilter("retracts_id IS NOT NULL");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Enrollment>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EnrollmentId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attestation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AttestationId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Assertion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RetractsId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}
