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
/// EF Core 10 entity mapping for the <c>cec_chair_adjudications</c> table.
/// Configures Chair tie-breaker determinations and embedded e-signature manifests.
/// @req:PRD-CEC-002
/// @req:PRD-SYS-053
/// </summary>
public sealed class CecChairAdjudicationConfiguration : IEntityTypeConfiguration<CecChairAdjudication>
{
    public void Configure(EntityTypeBuilder<CecChairAdjudication> builder)
    {
        builder.ToTable("cec_chair_adjudications");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasMaxLength(255);

        builder.Property(a => a.DossierId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(a => a.ChairId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(a => a.ChairName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(a => a.FinalClassification)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(a => a.FinalSubtype)
            .HasMaxLength(255);

        builder.Property(a => a.FinalAdjudicatedOnsetDate)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.BindingReader)
            .HasMaxLength(100)
            .IsRequired();

        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => v == null ? new List<string>() : JsonSerializer.Deserialize<List<string>>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default) ?? new List<string>());

        builder.Property(a => a.CriteriaConfirmed)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => string.IsNullOrWhiteSpace(v) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default) ?? new List<string>())
            .HasColumnName("criteria_confirmed")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(stringListComparer);

        var signatureComparer = new ValueComparer<SignatureManifest>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(StringComparison.Ordinal),
            v => v == null ? null! : JsonSerializer.Deserialize<SignatureManifest>(
                JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default)!);

        builder.Property(a => a.SignatureManifest)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<SignatureManifest>(v, JsonSerializerOptions.Default)!)
            .HasColumnName("signature_manifest")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(signatureComparer);

        builder.Property<long>("_version")
            .HasColumnName("_version")
            .HasDefaultValue(0L)
            .IsConcurrencyToken();

        builder.HasIndex(a => a.DossierId)
            .HasDatabaseName("ix_cec_chair_adjudications_dossier_id");
    }
}
