using System;

using Cadence.Core.Domain.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Database.Configurations;

/// <summary>
/// EF Core 10 Fluent API configuration for the shared 21 CFR Part 11 audit log entry table.
/// </summary>
public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    /// <summary>
    /// Configures the composite descending sequence index and predecessor hash index required for indexed
    /// per-<see cref="AuditLogEntry.EntityName"/> Merkle chain tip resolution.
    /// </summary>
    public static void ConfigureChainIndexes(EntityTypeBuilder<AuditLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasIndex(x => new { x.EntityName, x.Timestamp, x.VersionIndex, x.Id })
            .IsDescending(false, true, true, true);
        builder.HasIndex(x => new { x.EntityName, x.PredecessorHash });
    }

    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
        builder.Property(x => x.VersionIndex).IsRequired();
        builder.Property(x => x.ChangesJson);
        builder.Property(x => x.PredecessorHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.BlockHash).HasMaxLength(64).IsRequired();

        ConfigureChainIndexes(builder);
    }
}
