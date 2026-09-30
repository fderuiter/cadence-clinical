using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>EF Core mapping for <c>kernel.code_systems</c>; a URI is registered once per tenant (spec §6.1).</summary>
internal sealed class CodeSystemConfiguration : IEntityTypeConfiguration<CodeSystem>
{
    public void Configure(EntityTypeBuilder<CodeSystem> builder)
    {
        builder.ToTable("code_systems");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Uri).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Publisher).HasMaxLength(255).IsRequired();
        builder.Property(x => x.License).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Uri }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.code_system_versions</c> and its entries in <c>kernel.code_system_entries</c>. Both
/// tables are insert-only: a version never changes once imported.
/// </summary>
internal sealed class CodeSystemVersionConfiguration : IEntityTypeConfiguration<CodeSystemVersion>
{
    public void Configure(EntityTypeBuilder<CodeSystemVersion> builder)
    {
        builder.ToTable("code_system_versions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Version).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PackageSha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.PackageFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PackageSourceUri).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.PackageFormat).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.CodeSystemId, x.Version }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CodeSystem>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CodeSystemId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Entries).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.CodeSystemVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.code_system_entries</c>.</summary>
internal sealed class CodeSystemEntryConfiguration : IEntityTypeConfiguration<CodeSystemEntry>
{
    public void Configure(EntityTypeBuilder<CodeSystemEntry> builder)
    {
        builder.ToTable("code_system_entries");
        builder.HasKey(x => new { x.TenantId, x.CodeSystemVersionId, x.Ordinal });
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Display).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.GroupCode).HasMaxLength(64);
        builder.Property(x => x.SubmissionValue).HasMaxLength(255);
        builder.Property(x => x.Definition);
        builder.Property(x => x.Synonyms).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.CodeSystemVersionId, x.GroupCode, x.Code }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(x => new { x.TenantId, x.CodeSystemVersionId, x.Code });
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.concepts</c> and its versions in <c>kernel.concept_revisions</c> (insert-only). A CDISC
/// concept code identifies at most one concept per tenant.
/// </summary>
internal sealed class ConceptConfiguration : IEntityTypeConfiguration<Concept>
{
    public void Configure(EntityTypeBuilder<Concept> builder)
    {
        builder.ToTable("concepts");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Origin).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ExternalCode).HasMaxLength(64);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.PreferredLabel).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Definition).IsRequired();
        builder.Ignore(x => x.Current);
        builder.HasIndex(x => new { x.TenantId, x.ExternalCode }).IsUnique().HasFilter("external_code IS NOT NULL");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Revisions).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.ConceptId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.concept_revisions</c>.</summary>
internal sealed class ConceptRevisionConfiguration : IEntityTypeConfiguration<ConceptRevision>
{
    public void Configure(EntityTypeBuilder<ConceptRevision> builder)
    {
        builder.ToTable("concept_revisions");
        builder.HasKey(x => new { x.TenantId, x.ConceptId, x.Version });
        builder.Property(x => x.PreferredLabel).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Definition).IsRequired();
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.concept_mappings</c>. The concept version it maps must exist (composite key into
/// <c>concept_revisions</c>) and so must the code-system version.
/// </summary>
internal sealed class ConceptMappingConfiguration : IEntityTypeConfiguration<ConceptMapping>
{
    public void Configure(EntityTypeBuilder<ConceptMapping> builder)
    {
        builder.ToTable("concept_mappings");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Relation).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Evidence).HasMaxLength(2000).IsRequired();
        builder.Ignore(x => x.Concept);
        builder.Ignore(x => x.Target);
        builder.HasIndex(x => new { x.TenantId, x.CodeSystemVersionId, x.Code });
        builder.HasIndex(x => new { x.TenantId, x.ConceptId });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConceptRevision>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ConceptId, x.ConceptVersion })
            .HasPrincipalKey(x => new { x.TenantId, x.ConceptId, x.Version })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CodeSystemVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CodeSystemVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}
