using Cadence.Kernel.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.tenants</c>. The identity and ownership tables (spec §3) in this file keep
/// human-facing codes unique within the scope spec §3.2 states; tenant isolation with composite keys and RLS is
/// added by #4780 (ADR-2250).
/// @req:PRD-SYS-053
/// </summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.organizations</c>. @req:PRD-SYS-053</summary>
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
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.people</c> (tenant-independent identity, K-ID-4). @req:PRD-SYS-053</summary>
internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.FirstName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.Email).IsUnique();
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.sites</c>; <c>SiteCode</c> is unique per institution. @req:PRD-SYS-053</summary>
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
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.InstitutionId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.studies</c>; <c>StudyCode</c> is unique per tenant. @req:PRD-SYS-053</summary>
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
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.SponsorId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.study_sites</c>; one row per Study × Site. @req:PRD-SYS-053</summary>
internal sealed class StudySiteConfiguration : IEntityTypeConfiguration<StudySite>
{
    public void Configure(EntityTypeBuilder<StudySite> builder)
    {
        builder.ToTable("study_sites");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.StudyId, x.SiteId }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany().HasForeignKey(x => x.StudyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}
