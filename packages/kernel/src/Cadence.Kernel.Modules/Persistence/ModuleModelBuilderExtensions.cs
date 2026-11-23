using System;

using Cadence.Core.Domain;
using Cadence.Kernel.Domain.Bitemporal;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using NpgsqlTypes;

namespace Cadence.Kernel.Modules;

/// <summary>Mapping helpers for aggregates and bitemporal intervals a module's commands write (ADR-2600, ADR-2625).</summary>
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

    /// <summary>
    /// Maps <see cref="IBitemporalEntity.Period"/> to PostgreSQL <c>valid_period</c> and <c>recorded_period</c>
    /// <c>tstzrange</c> columns (<c>K-TIME-1</c>, <c>K-TIME-2</c>, ADR-2246).
    /// </summary>
    public static EntityTypeBuilder<T> HasModuleBitemporalPeriod<T>(this EntityTypeBuilder<T> builder)
        where T : class, IBitemporalEntity
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ComplexProperty<TemporalPeriod>(nameof(IBitemporalEntity.Period), period =>
        {
            period.Property(p => p.Valid)
                .HasConversion<ModuleTimeRangeValueConverter>()
                .HasColumnName(ModuleBitemporalSql.ValidPeriodColumn)
                .HasColumnType("tstzrange")
                .IsRequired();
            period.Property(p => p.Recorded)
                .HasConversion<ModuleTimeRangeValueConverter>()
                .HasColumnName(ModuleBitemporalSql.RecordedPeriodColumn)
                .HasColumnType("tstzrange")
                .IsRequired();
        });

        return builder;
    }
}

/// <summary>Stores a domain <see cref="TimeRange"/> as a PostgreSQL <c>tstzrange</c> <c>[start, end)</c>.</summary>
public sealed class ModuleTimeRangeValueConverter()
    : ValueConverter<TimeRange, NpgsqlRange<DateTimeOffset>>(
        range => ToProvider(range),
        range => FromProvider(range))
{
    internal static NpgsqlRange<DateTimeOffset> ToProvider(TimeRange range) =>
        range.End is { } end
            ? new NpgsqlRange<DateTimeOffset>(range.Start, true, end, false)
            : new NpgsqlRange<DateTimeOffset>(range.Start, true, false, default, false, true);

    internal static TimeRange FromProvider(NpgsqlRange<DateTimeOffset> range) =>
        new(range.LowerBound, range.UpperBoundInfinite ? null : range.UpperBound);
}
