using Cadence.Kernel.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.subscriptions</c>: one commercial subscription per tenant, isolated by Row-Level
/// Security and composite <c>(tenant_id, id)</c> alternate key (K-ID-1, ADR-2250).
/// </summary>
internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Tier).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.product_entitlements</c>: commercial product-family entitlements under a tenant's
/// <see cref="Subscription"/>, bound by composite <c>(tenant_id, subscription_id)</c> foreign key (K-ID-1).
/// </summary>
internal sealed class ProductEntitlementConfiguration : IEntityTypeConfiguration<ProductEntitlement>
{
    public void Configure(EntityTypeBuilder<ProductEntitlement> builder)
    {
        builder.ToTable("product_entitlements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Family).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired();
        builder.Property(x => x.GrantedAt).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Family }).IsUnique();
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Subscription>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SubscriptionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}
