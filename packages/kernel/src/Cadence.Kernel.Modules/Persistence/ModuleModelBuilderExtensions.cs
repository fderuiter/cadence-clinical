using System;

using Cadence.Core.Domain;

using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Modules;

/// <summary>Mapping helpers for aggregates a module's commands write (ADR-2600).</summary>
public static class ModuleModelBuilderExtensions
{
    /// <summary>
    /// Maps the 21 CFR Part 11 audit fields the pipeline stamps (<see cref="AggregateRootBase"/>), with
    /// <c>VersionIndex</c> as the optimistic-concurrency token: an update whose version changed underneath writes no row,
    /// and the command fails with a concurrency conflict instead of overwriting.
    /// </summary>
    public static EntityTypeBuilder<T> HasCommandAuditColumns<T>(this EntityTypeBuilder<T> builder)
        where T : AggregateRootBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired().IsConcurrencyToken();
        return builder;
    }
}
