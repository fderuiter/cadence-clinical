using Cadence.Apps.Execution.Domain.Coding;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="SynonymRule"/> table.
/// @req:PRD-SYS-001
/// </summary>
public sealed class SynonymRuleConfiguration : IEntityTypeConfiguration<SynonymRule>
{
    public void Configure(EntityTypeBuilder<SynonymRule> builder)
    {
        builder.ToTable("synonym_rules");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Scope)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.StudyId)
            .HasMaxLength(255);

        builder.Property(x => x.VerbatimText)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.DictionaryType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.DictionaryVersion)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.TargetCode)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.TargetTerm)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.TargetLevel)
            .HasMaxLength(50);

        builder.Property(x => x.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.ReasonForChange)
            .HasMaxLength(2000)
            .IsRequired();

        builder.HasIndex(x => new { x.Scope, x.StudyId, x.Status });
        builder.HasIndex(x => new { x.VerbatimText, x.DictionaryType });
    }
}

/// <summary>
/// EF Core entity configuration for <see cref="BulkSynonymImportJob"/> table.
/// @req:PRD-SYS-001
/// </summary>
public sealed class BulkSynonymImportJobConfiguration : IEntityTypeConfiguration<BulkSynonymImportJob>
{
    public void Configure(EntityTypeBuilder<BulkSynonymImportJob> builder)
    {
        builder.ToTable("bulk_synonym_import_jobs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Scope)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.StudyId)
            .HasMaxLength(255);

        builder.Property(x => x.FileName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.FileHash)
            .HasMaxLength(128)
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

        builder.HasIndex(x => x.CreatedAt);
    }
}
