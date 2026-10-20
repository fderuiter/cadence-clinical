using Cadence.Kernel.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.tenants</c>. The identity and ownership tables (spec §3) in this file keep
/// human-facing codes unique within the scope spec §3.2 states. Tenant isolation (K-ID-1, ADR-2250): every
/// tenant-scoped table also has a unique <c>(tenant_id, id)</c> key, and every reference between tenant-scoped tables
/// is a composite <c>(tenant_id, x_id)</c> foreign key, so a row can never point at another tenant's row. Row-Level
/// Security is added by the <c>TenantIsolation</c> migration (<see cref="Tenancy.TenantIsolationSql"/>).
/// </summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", t => t.HasCheckConstraint("ck_tenants_residency", "residency IS NULL OR residency ~ '^[A-Z]{2,8}$'"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Residency).HasMaxLength(8);
        builder.HasIndex(x => x.Code).IsUnique();
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.organizations</c>.</summary>
internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.people</c>: tenant-independent, pseudonymous identity (K-ID-4), the one kernel table
/// without a <c>tenant_id</c>. It holds no direct identifier: names and contact details are in the IdentityVault
/// (ADR-2458). Its row-level policy shows a person only to tenants the person has a <see cref="TenantMembership"/>
/// in.
/// </summary>
internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people");
        builder.HasKey(x => x.Id);
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.sites</c>; <c>SiteCode</c> is unique per institution.</summary>
internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable("sites");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SiteCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Country).HasMaxLength(2).IsRequired();
        builder.HasIndex(x => new { x.InstitutionId, x.SiteCode }).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.InstitutionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.studies</c>; <c>StudyCode</c> is unique per tenant.</summary>
internal sealed class StudyConfiguration : IEntityTypeConfiguration<Study>
{
    public void Configure(EntityTypeBuilder<Study> builder)
    {
        builder.ToTable("studies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StudyCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Phase).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.StudyCode }).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SponsorId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.study_sites</c>; one row per Study × Site.</summary>
internal sealed class StudySiteConfiguration : IEntityTypeConfiguration<StudySite>
{
    public void Configure(EntityTypeBuilder<StudySite> builder)
    {
        builder.ToTable("study_sites");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.StudyId, x.SiteId }).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });

        // Lets an enrollment's foreign key prove its study site belongs to the study it names.
        builder.HasAlternateKey(x => new { x.TenantId, x.Id, x.StudyId });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Site>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SiteId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.tenant_memberships</c>: one row per person per tenant (K-ID-4). The person reference
/// is a plain foreign key because <c>people</c> is tenant-independent.
/// </summary>
internal sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_memberships");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.PersonId }).IsUnique();
        builder.HasIndex(x => x.PersonId);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}
