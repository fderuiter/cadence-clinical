using System;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 10 entity mapping for the <c>clinical_queries</c> table.
/// Configures strongly-typed value objects, target coordinates index, and status escalation tracking.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public sealed class ClinicalQueryConfiguration : IEntityTypeConfiguration<ClinicalQuery>
{
    public void Configure(EntityTypeBuilder<ClinicalQuery> builder)
    {
        builder.ToTable("clinical_queries");
        builder.HasKey(q => q.Id);

        builder.Property(q => q.Id)
            .HasConversion(id => id.Value, v => new QueryId(v));

        builder.Property(q => q.StudyId)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(q => q.SiteId)
            .HasConversion(
                c => c.HasValue ? c.Value.Value : null,
                v => v == null ? null : new SiteCode(v))
            .HasMaxLength(255);

        builder.Property(q => q.SubjectId)
            .HasConversion(c => c.Value, v => new SubjectCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(q => q.VisitId)
            .HasMaxLength(255);

        builder.Property(q => q.Domain)
            .HasConversion(
                c => c.HasValue ? c.Value.Value : null,
                v => v == null ? null : new DomainCode(v))
            .HasMaxLength(50);

        builder.Property(q => q.TestCode)
            .HasConversion(c => c.Value, v => new TestCode(v))
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(q => q.Status)
            .HasConversion(s => QueryStatusCodec.ToCode(s), v => ParseStatus(v))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(q => q.Explanation)
            .HasMaxLength(1000);

        builder.Property(q => q.Response)
            .HasMaxLength(1000);

        builder.Property(q => q.ObservationId)
            .HasConversion(
                c => c.HasValue ? c.Value.Value.ToString() : null,
                v => v == null ? null : ObservationId.Parse(v))
            .HasMaxLength(255);

        builder.Property(q => q.FieldLink)
            .HasMaxLength(255);

        builder.Property(q => q.Message)
            .HasMaxLength(1000);

        builder.Property(q => q.Origin)
            .HasMaxLength(50);

        builder.Property(q => q.Priority)
            .HasMaxLength(50);

        builder.Property(q => q.RuleId)
            .HasMaxLength(255);

        builder.Property(q => q.Responder)
            .HasMaxLength(255);

        builder.Property(q => q.Resolver)
            .HasMaxLength(255);

        builder.Property(q => q.CancellationReason)
            .HasMaxLength(1000);

        builder.Property(q => q.FormId)
            .HasMaxLength(255);

        builder.Property(q => q.FieldId)
            .HasMaxLength(255);

        builder.Property(q => q.QueryType)
            .HasMaxLength(255);

        builder.Property(q => q.ActionRequired)
            .HasMaxLength(255);

        // Concurrency token matching ClinicalSubject pattern
        builder.Property<long>("_version")
            .HasColumnName("_version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken();

        // Indexes
        builder.HasIndex(q => new { q.StudyId, q.SubjectId, q.VisitId, q.Domain, q.TestCode })
            .HasDatabaseName("idx_query_target");

        builder.HasIndex(q => new { q.SubjectId, q.Status })
            .HasDatabaseName("idx_query_subject_status");

        builder.HasIndex(q => q.ObservationId)
            .HasDatabaseName("idx_query_observation_id");

        builder.HasIndex(q => q.SiteId)
            .HasDatabaseName("ix_clinical_queries_site_id");
    }

    private static QueryStatus ParseStatus(string raw) =>
        QueryStatusCodec.TryParse(raw, out var status)
            ? status
            : throw new InvalidOperationException($"Corrupted status string '{raw}' stored in clinical_queries.status");
}
