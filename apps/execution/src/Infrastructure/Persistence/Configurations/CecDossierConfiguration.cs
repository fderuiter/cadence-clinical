using System;
using System.Collections.Generic;
using System.Text.Json;

using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Forms;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 10 entity mapping for the <c>cec_dossiers</c> table.
/// Configures JSONB properties, signature manifest, reader assessments, and optimistic concurrency tokens.
/// @req:PRD-CEC-001
/// @req:PRD-CEC-002
/// @req:PRD-SYS-053
/// </summary>
public sealed class CecDossierConfiguration : IEntityTypeConfiguration<CecDossier>
{
    public void Configure(EntityTypeBuilder<CecDossier> builder)
    {
        builder.ToTable("cec_dossiers");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasMaxLength(255);

        builder.Property(d => d.EventId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(d => d.StudyId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(d => d.SubjectId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(d => d.MaskedSubjectId)
            .HasMaxLength(255);

        builder.Property(d => d.SiteId)
            .HasMaxLength(255);

        builder.Property(d => d.MaskedSiteId)
            .HasMaxLength(255);

        builder.Property(d => d.CandidateEventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.ReportedTerm)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.ReportedOnsetDate)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(d => d.WorkflowStatus)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.IsDiscordant)
            .IsRequired();

        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => v == null ? new List<string>() : JsonSerializer.Deserialize<List<string>>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default) ?? new List<string>());

        builder.Property(d => d.DiscordanceReasons)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => string.IsNullOrWhiteSpace(v) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default) ?? new List<string>())
            .HasColumnName("discordance_reasons")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(stringListComparer);

        var signatureComparer = new ValueComparer<SignatureManifest?>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => v == null ? null : JsonSerializer.Deserialize<SignatureManifest>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default));

        builder.Property(d => d.SignatureManifest)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<SignatureManifest>(v, JsonSerializerOptions.Default))
            .HasColumnName("signature_manifest")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(signatureComparer);

        var readerAssessmentComparer = new ValueComparer<CecReaderAssessment?>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => v == null ? null : JsonSerializer.Deserialize<CecReaderAssessment>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default));

        builder.Property(d => d.Reader1Assessment)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<CecReaderAssessment>(v, JsonSerializerOptions.Default))
            .HasColumnName("reader_1_assessment")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(readerAssessmentComparer);

        builder.Property(d => d.Reader2Assessment)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => v == null ? null : JsonSerializer.Deserialize<CecReaderAssessment>(v, JsonSerializerOptions.Default))
            .HasColumnName("reader_2_assessment")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(readerAssessmentComparer);

        var labPanelsComparer = new ValueComparer<List<CecLabPanel>>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => v == null ? new List<CecLabPanel>() : JsonSerializer.Deserialize<List<CecLabPanel>>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default) ?? new List<CecLabPanel>());

        builder.Property(d => d.LabPanels)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => string.IsNullOrWhiteSpace(v) ? new List<CecLabPanel>() : JsonSerializer.Deserialize<List<CecLabPanel>>(v, JsonSerializerOptions.Default) ?? new List<CecLabPanel>())
            .HasColumnName("lab_panels")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(labPanelsComparer);

        var ecgRecordsComparer = new ValueComparer<List<CecEcgRecord>>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => v == null ? new List<CecEcgRecord>() : JsonSerializer.Deserialize<List<CecEcgRecord>>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default) ?? new List<CecEcgRecord>());

        builder.Property(d => d.EcgRecords)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => string.IsNullOrWhiteSpace(v) ? new List<CecEcgRecord>() : JsonSerializer.Deserialize<List<CecEcgRecord>>(v, JsonSerializerOptions.Default) ?? new List<CecEcgRecord>())
            .HasColumnName("ecg_records")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(ecgRecordsComparer);

        var diagnosticAttachmentsComparer = new ValueComparer<List<CecDiagnosticAttachment>>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => v == null ? new List<CecDiagnosticAttachment>() : JsonSerializer.Deserialize<List<CecDiagnosticAttachment>>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default) ?? new List<CecDiagnosticAttachment>());

        builder.Property(d => d.DiagnosticAttachments)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => string.IsNullOrWhiteSpace(v) ? new List<CecDiagnosticAttachment>() : JsonSerializer.Deserialize<List<CecDiagnosticAttachment>>(v, JsonSerializerOptions.Default) ?? new List<CecDiagnosticAttachment>())
            .HasColumnName("diagnostic_attachments")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(diagnosticAttachmentsComparer);

        builder.Property<long>("_version")
            .HasColumnName("_version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken();

        builder.HasIndex(d => d.StudyId)
            .HasDatabaseName("ix_cec_dossiers_study_id");

        builder.HasIndex(d => d.WorkflowStatus)
            .HasDatabaseName("ix_cec_dossiers_workflow_status");
    }
}
