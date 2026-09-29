namespace Cadence.Kernel.Time;

/// <summary>
/// The single source of "now" for kernel, domain and entity code (spec §4.1 K-TIME-3).
/// Domain and entity code must never read <c>DateTime.Now</c>/<c>DateTime.UtcNow</c> or their
/// <see cref="DateTimeOffset"/> equivalents directly (enforced by <c>BannedSymbols.txt</c>); command
/// handlers take the timestamp from an injected <see cref="IClock"/> instead, which keeps tests
/// deterministic and keeps device time separate from server time (spec §10).
/// @req:PRD-SUB-001
/// </summary>
public interface IClock
{
    /// <summary>Gets the current instant as a UTC <see cref="DateTimeOffset"/> (offset zero).</summary>
    DateTimeOffset UtcNow { get; }
}
