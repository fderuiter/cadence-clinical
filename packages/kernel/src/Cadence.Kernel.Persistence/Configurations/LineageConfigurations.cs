using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Lineage;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>EF Core mapping for <c>kernel.lineage_nodes</c>: one row per object version a command wrote.</summary>
internal sealed class LineageNodeConfiguration : IEntityTypeConfiguration<LineageNodeRecord>
{
    public void Configure(EntityTypeBuilder<LineageNodeRecord> builder)
    {
        builder.ToTable("lineage_nodes");
        builder.HasKey(x => new { x.TenantId, x.NodeId, x.Kind, x.Version });
        builder.Property(x => x.Kind).HasMaxLength(48).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Derived });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.lineage_edges</c>. The <c>to</c> index serves the backward walk; the unique index,
/// which stores an edge once per pair and kind, leads with <c>from</c> and serves the forward walk.
/// </summary>
internal sealed class LineageEdgeConfiguration : IEntityTypeConfiguration<LineageEdgeRecord>
{
    public void Configure(EntityTypeBuilder<LineageEdgeRecord> builder)
    {
        builder.ToTable("lineage_edges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FromKind).HasMaxLength(48).IsRequired();
        builder.Property(x => x.ToKind).HasMaxLength(48).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ToId, x.ToKind, x.ToVersion });
        builder.HasIndex(x => new { x.TenantId, x.FromId, x.FromKind, x.FromVersion, x.ToId, x.ToKind, x.ToVersion, x.Kind }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
