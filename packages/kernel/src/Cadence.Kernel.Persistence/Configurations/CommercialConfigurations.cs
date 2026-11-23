using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.subscriptions</c>: one commercial subscription aggregate header per tenant, isolated by
/// Row-Level Security and composite <c>(tenant_id, id)</c> alternate key (K-ID-1, ADR-2250), with bitemporal history in
/// <c>kernel.subscription_periods</c> and <c>kernel.product_entitlements</c>.
/// </summary>
internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.Tier);
        builder.Ignore(x => x.Status);
        builder.Ignore(x => x.StartedAt);
        builder.Ignore(x => x.TrialEndsAt);
        builder.Ignore(x => x.ExpiredAt);
        builder.Ignore(x => x.ActivatedAt);
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Periods)
            .WithOne()
            .HasForeignKey(x => new { x.TenantId, x.SubscriptionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Periods).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.HasMany(x => x.Entitlements)
            .WithOne()
            .HasForeignKey(x => new { x.TenantId, x.SubscriptionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Entitlements).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.subscription_periods</c>: bitemporal lifecycle revisions of a tenant's
/// <see cref="Subscription"/>, guarded by <see cref="BitemporalSql"/> (<c>KT001</c> append-only + GiST exclusion).
/// </summary>
internal sealed class SubscriptionPeriodConfiguration : IEntityTypeConfiguration<SubscriptionPeriod>
{
    public void Configure(EntityTypeBuilder<SubscriptionPeriod> builder)
    {
        builder.ToTable("subscription_periods");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Tier).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.TrialEndsAt);
        builder.Property(x => x.ExpiredAt);
        builder.Property(x => x.ActivatedAt);
        builder.Property(x => x.StudyPhase).HasMaxLength(64);
        builder.Property(x => x.StudyDurationMonths);
        builder.Property(x => x.CountriesCsv).HasMaxLength(1024);
        builder.Property(x => x.SiteCount);
        builder.Property(x => x.ParticipantCount);
        builder.Property(x => x.ActiveStudyCount);
        builder.Property(x => x.BillingConfirmation).HasMaxLength(SubscriptionScopingValidator.MaxBillingConfirmationLength);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Ignore(x => x.IsCurrent);
        builder.HasBitemporalPeriod();
        builder.HasIndex(x => new { x.TenantId, x.SubscriptionId });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.product_entitlements</c>: bitemporal commercial product-family entitlement revisions
/// under a tenant's <see cref="Subscription"/>, guarded by <see cref="BitemporalSql"/> (<c>KT001</c> + GiST exclusion).
/// </summary>
internal sealed class ProductEntitlementConfiguration : IEntityTypeConfiguration<ProductEntitlement>
{
    public void Configure(EntityTypeBuilder<ProductEntitlement> builder)
    {
        builder.ToTable("product_entitlements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Family).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired();
        builder.Property(x => x.GrantedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Ignore(x => x.IsCurrent);
        builder.HasBitemporalPeriod();
        builder.HasIndex(x => new { x.TenantId, x.SubscriptionId, x.Family });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
