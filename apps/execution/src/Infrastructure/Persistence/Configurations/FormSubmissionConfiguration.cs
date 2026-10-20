using System;
using System.Text.Json;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 10 entity mapping for the <c>form_submissions</c> table.
/// Configures strongly-typed value objects, JSONB payload, signature manifest, and coordinates index.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:SRS-EDC-002
/// @req:SRS-EDC-003
/// </summary>
public sealed class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    public void Configure(EntityTypeBuilder<FormSubmission> builder)
    {
        builder.ToTable("form_submissions");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasConversion(id => id.Value, v => new FormSubmissionId(v));

        builder.Property(f => f.StudyId)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(f => f.SiteId)
            .HasConversion(
                c => c.HasValue ? c.Value.Value : null,
                v => v == null ? null : new SiteCode(v))
            .HasMaxLength(255);

        builder.Property(f => f.SubjectId)
            .HasConversion(c => c.Value, v => new SubjectCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(f => f.VisitId)
            .HasMaxLength(255);

        builder.Property(f => f.FormId)
            .HasConversion(c => c.Value, v => new FormId(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(f => f.Status)
            .HasConversion(s => FormSubmissionStatusCodec.ToCode(s), v => ParseStatus(v))
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(f => f.ProtocolVersion)
            .HasMaxLength(50);

        builder.Property(f => f.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb");

        // Signature manifest mapped to jsonb
        var signatureComparer = new ValueComparer<SignatureManifest?>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => v == null ? null : JsonSerializer.Deserialize<SignatureManifest>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default));

        builder.Property(f => f.SignatureManifest)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<SignatureManifest>(v, JsonSerializerOptions.Default))
            .HasColumnName("signature_manifest")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(signatureComparer);

        builder.Property(f => f.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(f => f.IsReadonly)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(f => f.ClonedFromId)
            .HasMaxLength(36);

        // Concurrency token matching ClinicalSubject pattern
        builder.Property<long>("_version")
            .HasColumnName("_version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken();

        // Indexes
        builder.HasIndex(f => new { f.StudyId, f.SubjectId, f.VisitId, f.FormId })
            .HasDatabaseName("idx_form_submissions_coords");

        builder.HasIndex(f => f.SiteId)
            .HasDatabaseName("ix_form_submissions_site_id");
    }

    private static FormSubmissionStatus ParseStatus(string raw) =>
        FormSubmissionStatusCodec.TryParse(raw, out var status)
            ? status
            : throw new InvalidOperationException($"Corrupted status string '{raw}' stored in form_submissions.status");
}
