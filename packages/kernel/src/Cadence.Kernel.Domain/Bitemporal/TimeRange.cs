using System;

namespace Cadence.Kernel.Domain.Bitemporal;

/// <summary>
/// A half-open time interval <c>[Start, End)</c> in UTC; a null <see cref="End"/> is unbounded (still open). Persisted
/// as a PostgreSQL <c>tstzrange</c> (ADR-2246, spec §4.1). <see cref="Contains"/> and <see cref="Overlaps"/> have the
/// same semantics as the SQL <c>@&gt;</c> and <c>&amp;&amp;</c> operators, and translate to them in kernel queries.
/// @req:PRD-SYS-053
/// </summary>
public readonly record struct TimeRange
{
    /// <summary>Creates <c>[start, end)</c>, or <c>[start, ∞)</c> when <paramref name="end"/> is null.</summary>
    /// <exception cref="ArgumentException"><paramref name="end"/> precedes <paramref name="start"/>.</exception>
    public TimeRange(DateTimeOffset start, DateTimeOffset? end)
    {
        if (end.HasValue && end.Value < start)
        {
            throw new ArgumentException("A range cannot end before it starts.", nameof(end));
        }

        Start = start.ToUniversalTime();
        End = end?.ToUniversalTime();
    }

    /// <summary>Inclusive lower bound, in UTC.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>Exclusive upper bound in UTC, or null when the range is still open.</summary>
    public DateTimeOffset? End { get; }

    /// <summary>True when the range has no upper bound (SQL <c>upper_inf</c>).</summary>
    public bool IsOpen => End is null;

    /// <summary>Creates an open range <c>[start, ∞)</c>.</summary>
    public static TimeRange From(DateTimeOffset start) => new(start, null);

    /// <summary>True when <paramref name="instant"/> falls inside the range (SQL <c>range @&gt; instant</c>).</summary>
    public bool Contains(DateTimeOffset instant) => instant >= Start && (End is null || instant < End.Value);

    /// <summary>True when the two ranges share at least one instant (SQL <c>a &amp;&amp; b</c>).</summary>
    public bool Overlaps(TimeRange other) =>
        (other.End is null || Start < other.End.Value) && (End is null || other.Start < End.Value);

    /// <summary>Returns this open range closed at <paramref name="end"/>.</summary>
    /// <exception cref="InvalidOperationException">The range is already closed.</exception>
    public TimeRange Close(DateTimeOffset end) =>
        IsOpen ? new TimeRange(Start, end) : throw new InvalidOperationException("The range is already closed.");
}
