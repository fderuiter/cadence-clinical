using System;

using Cadence.Kernel.Domain.Bitemporal;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using NpgsqlTypes;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// Stores a domain <see cref="TimeRange"/> as a PostgreSQL <c>tstzrange</c> <c>[start, end)</c>, with an open range
/// stored as an infinite upper bound (ADR-2246). The domain type stays free of Npgsql.
/// @req:PRD-SYS-053
/// </summary>
internal sealed class TimeRangeValueConverter()
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
