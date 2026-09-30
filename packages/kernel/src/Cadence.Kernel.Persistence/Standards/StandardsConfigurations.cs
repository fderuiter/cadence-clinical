using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Persistence.Configurations;
using Cadence.Kernel.Standards;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Kernel.Persistence.Standards;

/// <summary>
/// EF Core mapping for <c>kernel.standard_versions</c> and its definition tables (spec §6.3). Definitions are keyed by
/// their natural names within the standard version, and every reference is a composite <c>(tenant_id, …)</c> foreign
/// key (K-ID-1). Standard dataset and variable names are stored as data only (K-PROJ-1).
/// </summary>
internal sealed class StandardVersionConfiguration : IEntityTypeConfiguration<StandardVersion>
{
    public void Configure(EntityTypeBuilder<StandardVersion> builder)
    {
        builder.ToTable("standard_versions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.StandardCode).HasMaxLength(StandardPackageContent.MaxNameLength).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(StandardPackageContent.MaxNameLength).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Publisher).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(1000);
        builder.Property(x => x.ContentHash).HasMaxLength(StandardsColumns.HashLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.StandardCode, x.Version }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Datasets).WithOne()
            .HasForeignKey(d => new { d.TenantId, d.StandardVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.ValueLevelDefinitions).WithOne()
            .HasForeignKey(d => new { d.TenantId, d.StandardVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Terminology).WithOne()
            .HasForeignKey(d => new { d.TenantId, d.StandardVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Datasets).HasField("_datasets");
        builder.Navigation(x => x.ValueLevelDefinitions).HasField("_valueLevelDefinitions");
        builder.Navigation(x => x.Terminology).HasField("_terminology");
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.standard_datasets</c>.</summary>
internal sealed class StandardDatasetConfiguration : IEntityTypeConfiguration<StandardDataset>
{
    public void Configure(EntityTypeBuilder<StandardDataset> builder)
    {
        builder.ToTable("standard_datasets");
        builder.HasKey(x => new { x.TenantId, x.StandardVersionId, x.Name });
        builder.Property(x => x.Name).HasMaxLength(StandardPackageContent.MaxNameLength);
        builder.Property(x => x.Label).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Class).HasMaxLength(255);
        builder.Property(x => x.Structure).HasMaxLength(1000);
        builder.HasMany(x => x.Variables).WithOne()
            .HasForeignKey(v => new { v.TenantId, v.StandardVersionId, v.DatasetName })
            .HasPrincipalKey(x => new { x.TenantId, x.StandardVersionId, x.Name })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Variables).HasField("_variables");
    }
}

/// <summary>EF Core mapping for <c>kernel.standard_variables</c>.</summary>
internal sealed class StandardVariableConfiguration : IEntityTypeConfiguration<StandardVariable>
{
    public void Configure(EntityTypeBuilder<StandardVariable> builder)
    {
        builder.ToTable("standard_variables");
        builder.HasKey(x => new { x.TenantId, x.StandardVersionId, x.DatasetName, x.Name });
        builder.Property(x => x.DatasetName).HasMaxLength(StandardPackageContent.MaxNameLength);
        builder.Property(x => x.Name).HasMaxLength(StandardPackageContent.MaxNameLength);
        builder.Property(x => x.Label).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DataType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(255);
        builder.Property(x => x.Core).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.PrimitiveCollection(x => x.Codelists).IsRequired();
        builder.PrimitiveCollection(x => x.ValueList).IsRequired();
        builder.Property(x => x.DescribedValueDomain).HasMaxLength(255);
    }
}

/// <summary>EF Core mapping for <c>kernel.standard_value_level_definitions</c>.</summary>
internal sealed class StandardValueLevelDefinitionConfiguration : IEntityTypeConfiguration<StandardValueLevelDefinition>
{
    public void Configure(EntityTypeBuilder<StandardValueLevelDefinition> builder)
    {
        builder.ToTable("standard_value_level_definitions");
        builder.HasKey(x => new { x.TenantId, x.StandardVersionId, x.Ordinal });
        builder.Property(x => x.Dataset).HasMaxLength(StandardPackageContent.MaxNameLength).IsRequired();
        builder.Property(x => x.Variable).HasMaxLength(StandardPackageContent.MaxNameLength).IsRequired();
        builder.Property(x => x.Where).HasColumnType("jsonb").IsRequired()
            .HasConversion(new WhereClauseConverter(), new WhereClauseComparer());
        builder.Property(x => x.DataType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.PrimitiveCollection(x => x.Codelists).IsRequired();
        builder.PrimitiveCollection(x => x.ValueList).IsRequired();

        // A definition always targets a variable its standard version defines.
        builder.HasOne<StandardVariable>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StandardVersionId, x.Dataset, x.Variable })
            .HasPrincipalKey(v => new { v.TenantId, v.StandardVersionId, v.DatasetName, v.Name })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private sealed class WhereClauseConverter() : ValueConverter<List<WhereCondition>, string>(
        v => JsonSerializer.Serialize(v, StandardsColumns.Json),
        v => JsonSerializer.Deserialize<List<WhereCondition>>(v, StandardsColumns.Json) ?? new List<WhereCondition>());

    private sealed class WhereClauseComparer() : ValueComparer<List<WhereCondition>>(
        (l, r) => JsonSerializer.Serialize(l, StandardsColumns.Json) == JsonSerializer.Serialize(r, StandardsColumns.Json),
        v => JsonSerializer.Serialize(v, StandardsColumns.Json).GetHashCode(System.StringComparison.Ordinal),
        v => v.ToList());
}

/// <summary>EF Core mapping for <c>kernel.standard_terminology</c>.</summary>
internal sealed class StandardTerminologyBindingConfiguration : IEntityTypeConfiguration<StandardTerminologyBinding>
{
    public void Configure(EntityTypeBuilder<StandardTerminologyBinding> builder)
    {
        builder.ToTable("standard_terminology");
        builder.HasKey(x => new { x.TenantId, x.StandardVersionId, x.CodeSystem, x.Version });
        builder.Property(x => x.CodeSystem).HasMaxLength(StandardPackageContent.MaxNameLength);
        builder.Property(x => x.Version).HasMaxLength(StandardPackageContent.MaxNameLength);
    }
}

/// <summary>EF Core mapping for <c>kernel.library_items</c>: one row per version, unique by kind, key and version.</summary>
internal sealed class LibraryItemConfiguration : IEntityTypeConfiguration<LibraryItem>
{
    public void Configure(EntityTypeBuilder<LibraryItem> builder)
    {
        builder.ToTable("library_items");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.Property(x => x.Key).HasMaxLength(LibraryItem.MaxKeyLength).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(LibraryItem.MaxTitleLength).IsRequired();
        // text, not jsonb: jsonb rewrites numbers, and the stored bytes must re-hash to ContentHash.
        builder.Property(x => x.Content).HasColumnType("text").IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(StandardsColumns.HashLength).IsRequired();
        builder.PrimitiveCollection(x => x.StandardVersionIds).IsRequired()
            .ElementType(e => e.HasConversion<StandardVersionIdValueConverter>());
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Kind, x.Key, x.ItemVersion }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.study_standards_profiles</c>: at most one per study.</summary>
internal sealed class StudyStandardsProfileConfiguration : IEntityTypeConfiguration<StudyStandardsProfile>
{
    public void Configure(EntityTypeBuilder<StudyStandardsProfile> builder)
    {
        builder.ToTable("study_standards_profiles");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.PrimitiveCollection(x => x.StandardVersionIds).IsRequired()
            .ElementType(e => e.HasConversion<StandardVersionIdValueConverter>());
        builder.PrimitiveCollection(x => x.ControlledTerminology).IsRequired();
        builder.PrimitiveCollection(x => x.LibraryPins).IsRequired()
            .ElementType(e => e.HasConversion<LibraryItemIdValueConverter>());
        builder.Ignore(x => x.ControlledTerminologyRefs);
        builder.HasIndex(x => new { x.TenantId, x.StudyId }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>Shared column settings for the standards tables.</summary>
internal static class StandardsColumns
{
    /// <summary><c>sha256:</c> plus 64 hex characters.</summary>
    public const int HashLength = 71;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
