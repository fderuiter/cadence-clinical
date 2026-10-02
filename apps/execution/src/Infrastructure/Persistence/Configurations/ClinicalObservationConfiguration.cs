using System;
using System.Text.Json;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 10 entity mapping for the <c>clinical_observations</c> table.
/// Configures strongly-typed value objects, ADR-2332 JSONB additional properties with GIN index,
/// and lookup coordinates index on (study_id, subject_id, visit_id, domain, test_code).
/// @req:PRD-SYS-001
/// @req:PRD-SYS-042
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public sealed class ClinicalObservationConfiguration : IEntityTypeConfiguration<ClinicalObservation>
{
    public void Configure(EntityTypeBuilder<ClinicalObservation> builder)
    {
        builder.ToTable("clinical_observations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, v => new ObservationId(v));

        builder.Property(o => o.SubjectId)
            .HasConversion(c => c.Value, v => new SubjectCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(o => o.StudyId)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(o => o.SiteId)
            .HasConversion(
                c => c.HasValue ? c.Value.Value : null,
                v => v == null ? null : new SiteCode(v))
            .HasMaxLength(255);

        builder.Property(o => o.VisitId)
            .HasMaxLength(255);

        builder.Property(o => o.Domain)
            .HasConversion(c => c.Value, v => new DomainCode(v))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(o => o.TestCode)
            .HasConversion(c => c.Value, v => new TestCode(v))
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(o => o.TestName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(o => o.Unit)
            .HasMaxLength(50);

        builder.Property(o => o.NormalizedUnit)
            .HasMaxLength(50);

        builder.Property(o => o.SdvVerifiedBy)
            .HasMaxLength(255);

        builder.Property(o => o.PageId)
            .HasMaxLength(255);

        builder.Property(o => o.SdvFlagReason)
            .HasMaxLength(1000);

        builder.Property(o => o.ProtocolVersionTag)
            .HasMaxLength(50);

        builder.Property(o => o.FormSubmissionId)
            .HasConversion(
                c => c.HasValue ? c.Value.Value : (Guid?)null,
                v => v == null ? null : new FormSubmissionId(v.Value))
            .HasColumnName("form_submission_id");

        builder.Property(o => o.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        // ADR-2332 dynamic property bag mapped to PostgreSQL jsonb
        var additionalPropertiesComparer = new ValueComparer<ObservationAdditionalProperties>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<ObservationAdditionalProperties>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default) ?? new());

        builder.Property(o => o.AdditionalProperties)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<ObservationAdditionalProperties>(v, JsonSerializerOptions.Default) ?? new())
            .HasColumnName("additional_properties")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(additionalPropertiesComparer);

        // Concurrency token matching ClinicalSubject pattern
        builder.Property<long>("_version")
            .HasColumnName("_version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken();

        // Indexes
        builder.HasIndex(o => new { o.StudyId, o.SubjectId, o.VisitId, o.Domain, o.TestCode })
            .HasDatabaseName("idx_observation_coords");

        builder.HasIndex(o => o.SiteId)
            .HasDatabaseName("ix_clinical_observations_site_id");

        builder.HasIndex(o => new { o.SubjectId, o.ObservationDate })
            .HasDatabaseName("idx_observation_subject_date");

        builder.HasIndex(o => o.FormSubmissionId)
            .HasDatabaseName("ix_clinical_observations_form_submission_id");

        builder.HasIndex(o => o.IsActive)
            .HasDatabaseName("ix_clinical_observations_is_active");

        builder.HasIndex(o => new { o.FormSubmissionId, o.IsActive })
            .HasDatabaseName("idx_observation_form_submission_active");
    }
}
