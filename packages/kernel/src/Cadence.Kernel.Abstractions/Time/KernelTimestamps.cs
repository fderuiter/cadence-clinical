namespace Cadence.Kernel.Time;

/// <summary>
/// The precision kernel timestamps are stored and hashed at. PostgreSQL <c>timestamptz</c> holds whole microseconds,
/// so a value hashed at .NET's 100 ns precision would not verify after a round-trip; every recorded time the kernel
/// writes goes through <see cref="ToStoredPrecision"/> first (ADR-2449, matching ADR-2446).
/// </summary>
public static class KernelTimestamps
{
    /// <summary>Converts to UTC and truncates to whole microseconds.</summary>
    public static DateTimeOffset ToStoredPrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
