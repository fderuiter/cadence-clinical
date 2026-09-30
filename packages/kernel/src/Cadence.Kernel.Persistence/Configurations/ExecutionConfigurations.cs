using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Bitemporal;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>EF Core mapping for <c>kernel.participants</c>: a pseudonymous identity per tenant (spec §7.4).</summary>
internal sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        builder.ToTable("participants");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.enrollments</c> (spec §7.4). Subject codes and enrollment numbers are unique per study.
/// The study site must belong to the study (composite key into <c>study_sites</c>), and the status history in
/// <c>enrollment_status_periods</c> is always loaded with the enrollment. The migration adds the lifecycle trigger and
/// the one-active-enrollment guard in <see cref="Execution.EnrollmentSql"/>.
/// </summary>
internal sealed class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("enrollments", t =>
        {
            t.HasCheckConstraint("ck_enrollments_number", "enrollment_number IS NULL OR enrollment_number > 0");
            t.HasCheckConstraint(
                "ck_enrollments_numbered",
                "(status IN ('Screening', 'ScreenFailed') AND enrollment_number IS NULL)"
                + " OR (status IN ('Enrolled', 'Randomized', 'Active', 'Completed') AND enrollment_number IS NOT NULL)"
                + " OR status = 'Withdrawn'");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubjectCode)
            .HasConversion(SubjectCodeConverter.Instance)
            .HasMaxLength(SubjectCode.MaxLength)
            .IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.WithdrawalKind).HasConversion<string>().HasMaxLength(32);
        builder.Ignore(x => x.ContentHash);
        builder.Ignore(x => x.SignableTarget);
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.SubjectCode }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.EnrollmentNumber }).IsUnique().HasFilter("enrollment_number IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.ParticipantId });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id, x.ParticipantId, x.StudySiteId });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Participant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParticipantId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudySite>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudySiteId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id, x.StudyId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attestation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.WithdrawalAttestationId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Periods).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.EnrollmentId, x.ParticipantId, x.StudySiteId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id, x.ParticipantId, x.StudySiteId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Periods).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        AuditColumns.Configure(builder);
    }

    /// <summary>Stores a <see cref="SubjectCode"/> as its text.</summary>
    private sealed class SubjectCodeConverter() : ValueConverter<SubjectCode, string>(code => code.Value, text => SubjectCode.From(text))
    {
        public static readonly SubjectCodeConverter Instance = new();
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.enrollment_status_periods</c>, the bitemporal status history of an enrollment
/// (ADR-2246). The migration adds the K-TIME-1 and K-TIME-2 guards keyed by enrollment.
/// </summary>
internal sealed class EnrollmentStatusPeriodConfiguration : IEntityTypeConfiguration<EnrollmentStatusPeriod>
{
    public void Configure(EntityTypeBuilder<EnrollmentStatusPeriod> builder)
    {
        builder.ToTable("enrollment_status_periods");
        builder.HasKey(x => x.Id);

        // Rows are created with their id; without this EF would treat a new row added to a loaded enrollment as an
        // existing one to update.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.HasBitemporalPeriod();
        builder.HasIndex(x => new { x.TenantId, x.EnrollmentId });
    }
}
