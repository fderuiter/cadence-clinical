using Cadence.Apps.Execution.Domain.Observations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="LabReferenceRange"/> table.
/// @req:PRD-SYS-121
/// </summary>
public sealed class LabReferenceRangeConfiguration : IEntityTypeConfiguration<LabReferenceRange>
{
    public void Configure(EntityTypeBuilder<LabReferenceRange> builder)
    {
        builder.ToTable("lab_reference_ranges");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StudyId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.TestCode)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.TestName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Source)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.SiteId)
            .HasMaxLength(255);

        builder.Property(x => x.Unit)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.NormalizedUnit)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.SexApplicability)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.LoincCode)
            .HasMaxLength(100);

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.ReasonForChange)
            .HasMaxLength(2000)
            .IsRequired();

        builder.HasIndex(x => new { x.StudyId, x.TestCode, x.Source, x.SiteId, x.IsActive });
        builder.HasIndex(x => new { x.StudyId, x.IsActive });
    }
}

/// <summary>
/// EF Core entity configuration for <see cref="BulkLabRangeImportJob"/> table.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-121
/// </summary>
public sealed class BulkLabRangeImportJobConfiguration : IEntityTypeConfiguration<BulkLabRangeImportJob>
{
    public void Configure(EntityTypeBuilder<BulkLabRangeImportJob> builder)
    {
        builder.ToTable("bulk_lab_range_import_jobs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JobId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.StudyId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.SiteId)
            .HasMaxLength(255);

        builder.Property(x => x.FileName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.FileType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.ReasonForChange)
            .HasMaxLength(2000)
            .IsRequired();

        builder.HasIndex(x => x.JobId).IsUnique();
        builder.HasIndex(x => new { x.StudyId, x.SubmittedAt });
    }
}
