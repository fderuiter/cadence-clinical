using Cadence.Kernel.Time;

namespace Cadence.Testing.Time;

/// <summary>
/// Deterministic, settable <see cref="IClock"/> test double. The clock stands still until a test
/// calls <see cref="Advance"/> or <see cref="Set"/>, so timestamps can be asserted exactly.
/// Every value it returns is normalised to UTC (offset zero), matching <see cref="SystemClock"/>.
/// @req:PRD-SUB-001
/// </summary>
public sealed class FakeClock : IClock
{
    /// <summary>The default starting instant: 2026-01-01T00:00:00Z.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private DateTimeOffset _utcNow;

    /// <summary>Creates a clock frozen at <see cref="DefaultStart"/>.</summary>
    public FakeClock()
        : this(DefaultStart)
    {
    }

    /// <summary>Creates a clock frozen at <paramref name="utcNow"/> (converted to UTC).</summary>
    public FakeClock(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }
    }

    /// <summary>Moves the clock to <paramref name="utcNow"/> (converted to UTC). It may move backwards.</summary>
    public void Set(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            _utcNow = utcNow.ToUniversalTime();
        }
    }

    /// <summary>Moves the clock forward by <paramref name="delta"/> and returns the new instant.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative.</exception>
    public DateTimeOffset Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        lock (_gate)
        {
            _utcNow = _utcNow.Add(delta);
            return _utcNow;
        }
    }
}
