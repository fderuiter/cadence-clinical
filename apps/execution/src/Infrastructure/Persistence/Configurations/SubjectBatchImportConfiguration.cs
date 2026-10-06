using System;

using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 10 Fluent API mapping for <see cref="SubjectBatchImport"/> and <see cref="SubjectBatchRow"/>.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectBatchImportConfiguration : IEntityTypeConfiguration<SubjectBatchImport>
{
    public void Configure(EntityTypeBuilder<SubjectBatchImport> builder)
    {
        builder.ToTable("subject_batch_imports");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BatchId)
            .HasMaxLength(255)
            .IsRequired();
        builder.HasIndex(x => x.BatchId).IsUnique();

        builder.Property(x => x.StudyCode)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.FileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasMany(x => x.Rows)
            .WithOne()
            .HasForeignKey(x => x.BatchImportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(SubjectBatchImport.Rows))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SubjectBatchRowConfiguration : IEntityTypeConfiguration<SubjectBatchRow>
{
    public void Configure(EntityTypeBuilder<SubjectBatchRow> builder)
    {
        builder.ToTable("subject_batch_rows");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubjectId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.SiteCode)
            .HasConversion(c => c.HasValue ? c.Value.Value : null, v => v != null ? new SiteCode(v) : null)
            .HasColumnName("SiteId")
            .HasMaxLength(255);

        builder.Property(x => x.ArmId)
            .HasMaxLength(255);

        builder.Property(x => x.Name)
            .HasMaxLength(255);

        builder.Property(x => x.Birthdate)
            .HasMaxLength(64);

        builder.Property(x => x.Gender)
            .HasMaxLength(64);

        builder.Property(x => x.Race)
            .HasMaxLength(64);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.HasIndex(x => new { x.BatchImportId, x.RowNumber }).IsUnique();
    }
}
