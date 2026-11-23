using System;

using Cadence.Kernel.Domain.Bitemporal;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using NpgsqlTypes;

namespace Cadence.Kernel.Persistence.Bitemporal;

/// <summary>
/// Stores a domain <see cref="TimeRange"/> as a PostgreSQL <c>tstzrange</c> <c>[start, end)</c>, with an open range
/// stored as an infinite upper bound (ADR-2246). The domain type stays free of Npgsql.
/// </summary>
internal sealed class TimeRangeValueConverter()
    : ValueConverter<TimeRange, NpgsqlRange<DateTimeOffset>>(
        range => ToProvider(range),
        range => FromProvider(range))
{
    internal static NpgsqlRange<DateTimeOffset> ToProvider(TimeRange range) =>
        Cadence.Kernel.Modules.ModuleTimeRangeValueConverter.ToProvider(range);

    internal static TimeRange FromProvider(NpgsqlRange<DateTimeOffset> range) =>
        Cadence.Kernel.Modules.ModuleTimeRangeValueConverter.FromProvider(range);
}
