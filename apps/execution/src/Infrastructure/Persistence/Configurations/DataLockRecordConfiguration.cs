using Cadence.Apps.Execution.Domain.Locks;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 10 entity mapping for <c>data_lock_records</c> table.
/// Configures strongly typed value objects and indexes across 6-tier hierarchy.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// </summary>
public sealed class DataLockRecordConfiguration : IEntityTypeConfiguration<DataLockRecord>
{
    public void Configure(EntityTypeBuilder<DataLockRecord> builder)
    {
        builder.ToTable("data_lock_records");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasMaxLength(100);

        builder.Property(l => l.StudyId)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(l => l.SiteId)
            .HasConversion(
                c => c.HasValue ? c.Value.Value : null,
                v => v == null ? null : new SiteCode(v))
            .HasMaxLength(255);

        builder.Property(l => l.SubjectId)
            .HasConversion(
                c => c.HasValue ? c.Value.Value : null,
                v => v == null ? null : new SubjectCode(v))
            .HasMaxLength(255);

        builder.Property(l => l.VisitId)
            .HasMaxLength(255);

        builder.Property(l => l.FormId)
            .HasMaxLength(255);

        builder.Property(l => l.FieldName)
            .HasMaxLength(255);

        builder.Property(l => l.ScopeType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.ScopeId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(l => l.LockType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.ReasonForChange)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(l => l.SigToken)
            .HasMaxLength(2000);

        builder.Property(l => l.PreflightSummary)
            .HasMaxLength(2000);

        builder.Property(l => l.OverrideJustification)
            .HasMaxLength(2000);

        builder.Property(l => l.UnlockJustification)
            .HasMaxLength(2000);

        builder.Property(l => l.UnlockedBy)
            .HasMaxLength(255);

        builder.HasIndex(l => new { l.StudyId, l.ScopeType, l.ScopeId, l.IsActive })
            .HasDatabaseName("idx_data_locks_scope");
    }
}
