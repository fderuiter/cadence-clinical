using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Bitemporal;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.effectivities</c> (spec §7.1). The scope columns must fit the level, a protocol version
/// has one effectivity per scope, and a study-site scope names a study site of the same study. The status history in
/// <c>effectivity_status_periods</c> is always loaded with the effectivity.
/// </summary>
internal sealed class EffectivityConfiguration : IEntityTypeConfiguration<Effectivity>
{
    public void Configure(EntityTypeBuilder<Effectivity> builder)
    {
        builder.ToTable("effectivities", t => t.HasCheckConstraint(
            "ck_effectivities_scope",
            "(scope_level = 'Study' AND scope_country IS NULL AND scope_study_site_id IS NULL)"
            + " OR (scope_level = 'Country' AND scope_country IS NOT NULL AND scope_country ~ '^[A-Z]{2}$' AND scope_study_site_id IS NULL)"
            + " OR (scope_level = 'StudySite' AND scope_country IS NULL AND scope_study_site_id IS NOT NULL)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ScopeLevel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ScopeCountry).HasMaxLength(2);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Ignore(x => x.Scope);
        builder.HasIndex(x => new { x.TenantId, x.StudyId });
        builder.HasIndex(x => new { x.TenantId, x.ProtocolVersionId, x.ScopeLevel, x.ScopeCountry, x.ScopeStudySiteId })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProtocolVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ProtocolVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudySite>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ScopeStudySiteId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id, x.StudyId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Periods).WithOne()
            .HasForeignKey(x => new { x.TenantId, x.EffectivityId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Periods).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.effectivity_status_periods</c>, the bitemporal status history of an effectivity
/// (ADR-2246). The migration adds the K-TIME-1 and K-TIME-2 guards keyed by effectivity.
/// </summary>
internal sealed class EffectivityStatusPeriodConfiguration : IEntityTypeConfiguration<EffectivityStatusPeriod>
{
    public void Configure(EntityTypeBuilder<EffectivityStatusPeriod> builder)
    {
        builder.ToTable("effectivity_status_periods");
        builder.HasKey(x => x.Id);

        // Rows are created with their id; without this EF would treat a new row added to a loaded effectivity as an
        // existing one to update.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Evidence).IsRequired();
        builder.HasBitemporalPeriod();
        builder.HasIndex(x => new { x.TenantId, x.EffectivityId });
    }
}
