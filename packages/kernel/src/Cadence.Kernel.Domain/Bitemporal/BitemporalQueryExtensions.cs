using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Kernel.Domain.Bitemporal;

/// <summary>
/// LINQ query extensions for point-in-time state reconstruction across valid time and recorded time.
/// Conforms to ADR-2246 and PRD-SYS-053.
/// </summary>
public static class BitemporalQueryExtensions
{
    /// <summary>
    /// Filters an IQueryable sequence of bitemporal entities to those effective as of the specified dual coordinates (validAt, knownAt).
    /// </summary>
    public static IQueryable<T> AsOf<T>(
        this IQueryable<T> query,
        DateTimeOffset validAt,
        DateTimeOffset knownAt)
        where T : class, IBitemporalEntity
    {
        return query.Where(e =>
            e.Period.ValidFrom <= validAt &&
            (!e.Period.ValidTo.HasValue || e.Period.ValidTo.Value > validAt) &&
            e.Period.RecordedFrom <= knownAt &&
            (!e.Period.RecordedTo.HasValue || e.Period.RecordedTo.Value > knownAt));
    }

    /// <summary>
    /// Filters an IEnumerable sequence of bitemporal entities to those effective as of the specified dual coordinates (validAt, knownAt).
    /// </summary>
    public static IEnumerable<T> AsOf<T>(
        this IEnumerable<T> source,
        DateTimeOffset validAt,
        DateTimeOffset knownAt)
        where T : IBitemporalEntity
    {
        return source.Where(e => e.Period.IsEffectiveAsOf(validAt, knownAt));
    }

    /// <summary>
    /// Filters an IQueryable sequence of bitemporal entities to active, un-superseded clinical truth as of the given timestamp.
    /// </summary>
    public static IQueryable<T> AsOfCurrent<T>(
        this IQueryable<T> query,
        DateTimeOffset now)
        where T : class, IBitemporalEntity
    {
        return query.Where(e =>
            !e.Period.RecordedTo.HasValue &&
            (!e.Period.ValidTo.HasValue || e.Period.ValidTo.Value > now));
    }

    /// <summary>
    /// Filters an IEnumerable sequence of bitemporal entities to active, un-superseded clinical truth as of the given timestamp.
    /// </summary>
    public static IEnumerable<T> AsOfCurrent<T>(
        this IEnumerable<T> source,
        DateTimeOffset now)
        where T : IBitemporalEntity
    {
        return source.Where(e => e.Period.IsCurrent(now));
    }
}
