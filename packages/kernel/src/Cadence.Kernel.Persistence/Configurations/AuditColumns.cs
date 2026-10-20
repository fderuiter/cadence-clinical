using Cadence.Core.Domain;

using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// Shared mapping for the 21 CFR Part 11 audit fields every kernel aggregate carries (<see cref="AggregateRootBase"/>).
/// <c>VersionIndex</c> is the optimistic-concurrency token.
/// </summary>
internal static class AuditColumns
{
    public static void Configure<T>(EntityTypeBuilder<T> builder)
        where T : AggregateRootBase
    {
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired().IsConcurrencyToken();
    }
}
