using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Bitemporal;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.roles</c> (spec §9.3): a role's code is unique in its tenant and its capabilities are
/// a <c>text[]</c>. Insert-only in the database, so a role never changes after it is defined.
/// </summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", t => t.HasCheckConstraint("ck_roles_capabilities", "cardinality(capabilities) > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(Role.MaxCodeLength).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(Role.MaxNameLength).IsRequired();
        builder.Property(x => x.Capabilities).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.grants</c> (spec §9.3). A grant names exactly one capability or one role; a
/// delegation references its parent grant in the same tenant. The validity history in <c>grant_periods</c> is always
/// loaded with the grant.
/// </summary>
internal sealed class GrantConfiguration : IEntityTypeConfiguration<Grant>
{
    public void Configure(EntityTypeBuilder<Grant> builder)
    {
        builder.ToTable("grants", t =>
        {
            t.HasCheckConstraint("ck_grants_target", "(capability IS NULL) <> (role_id IS NULL)");
            t.HasCheckConstraint(
                "ck_grants_scope",
                "(scope_level = 'Tenant' AND scope_target_id IS NULL AND scope_country IS NULL)"
                + " OR (scope_level IN ('Organization', 'Study', 'StudySite') AND scope_target_id IS NOT NULL AND scope_country IS NULL)"
                + " OR (scope_level = 'StudyCountry' AND scope_target_id IS NOT NULL AND scope_country ~ '^[A-Z]{2}$')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GranteeKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.GranteeId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Capability).HasMaxLength(96);
        builder.Property(x => x.ScopeLevel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ScopeCountry).HasMaxLength(2);
        builder.Property(x => x.IssuedBy).HasMaxLength(300).IsRequired();
        builder.Ignore(x => x.Grantee);
        builder.Ignore(x => x.Scope);
        builder.Ignore(x => x.CurrentValidity);
        builder.HasIndex(x => new { x.TenantId, x.GranteeKind, x.GranteeId });
        builder.HasIndex(x => new { x.TenantId, x.ParentGrantId });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Role>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RoleId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Grant>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ParentGrantId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Periods).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.GrantId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Periods).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.grant_periods</c>, the bitemporal validity of a grant (ADR-2246). The migration adds the
/// K-TIME-1 and K-TIME-2 guards keyed by grant.
/// </summary>
internal sealed class GrantPeriodConfiguration : IEntityTypeConfiguration<GrantPeriod>
{
    public void Configure(EntityTypeBuilder<GrantPeriod> builder)
    {
        builder.ToTable("grant_periods");
        builder.HasKey(x => x.Id);

        // Rows are created with their id; without this EF would treat a new row added to a loaded grant as an existing
        // one to update.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasBitemporalPeriod();
        builder.HasIndex(x => new { x.TenantId, x.GrantId });
    }
}
