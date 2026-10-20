using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>randomization_schemes</c> (#5607): one row per version, unique per study and version; at most one approved
/// version per study (partial unique index).
/// </summary>
public sealed class RandomizationSchemeConfiguration : IEntityTypeConfiguration<RandomizationScheme>
{
    public void Configure(EntityTypeBuilder<RandomizationScheme> builder)
    {
        builder.ToTable("randomization_schemes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StudyCode)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();
        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ArmsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.StratificationFactorsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.BlockSizesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ConfigurationHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DraftedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ApprovedBy).HasMaxLength(255);
        builder.Property(x => x.ApprovalReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => new { x.StudyCode, x.Version }).IsUnique();
        builder.HasIndex(x => x.StudyCode)
            .IsUnique()
            .HasFilter("\"Status\" = 'Approved'")
            .HasDatabaseName("IX_randomization_schemes_StudyCode_Approved");
    }
}

/// <summary>
/// <c>randomization_list_slots</c> (#5607): a permuted-block list, unique per scheme, stratum and position; a slot
/// is consumed by at most one allocation.
/// </summary>
public sealed class RandomizationListSlotConfiguration : IEntityTypeConfiguration<RandomizationListSlot>
{
    public void Configure(EntityTypeBuilder<RandomizationListSlot> builder)
    {
        builder.ToTable("randomization_list_slots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StratumKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.ArmCiphertext).IsRequired();
        builder.HasIndex(x => new { x.SchemeId, x.StratumKey, x.Sequence }).IsUnique();
        builder.HasIndex(x => x.AllocationId).IsUnique().HasFilter("\"AllocationId\" IS NOT NULL");
        builder.HasOne<RandomizationScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// <c>randomization_allocations</c> (#5607): one per participant (unique), and one per scheme stratum position.
/// </summary>
public sealed class RandomizationAllocationConfiguration : IEntityTypeConfiguration<RandomizationAllocation>
{
    public void Configure(EntityTypeBuilder<RandomizationAllocation> builder)
    {
        builder.ToTable("randomization_allocations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StudyCode)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();
        builder.Property(x => x.SiteCode).HasConversion(
            c => c.HasValue ? c.Value.Value : null,
            v => v == null ? (SiteCode?)null : new SiteCode(v));
        builder.Property(x => x.SubjectId).HasConversion(id => id.Value, v => new SubjectId(v));
        builder.Property(x => x.StratumKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.StratificationFactorsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ArmCiphertext).IsRequired();
        builder.Property(x => x.KitNumber).HasMaxLength(255);
        builder.Property(x => x.AllocatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => x.SubjectId).IsUnique();
        builder.HasIndex(x => new { x.SchemeId, x.StratumKey, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.StudyCode, x.AllocatedAt });
        builder.HasOne<RandomizationScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClinicalSubject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
