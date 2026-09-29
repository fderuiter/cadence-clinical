namespace Cadence.Kernel.Time;

/// <summary>
/// Production <see cref="IClock"/> implementation backed by <see cref="TimeProvider.System"/>.
/// Register as a singleton; it is stateless and thread-safe.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SystemClock : IClock
{
    /// <summary>Gets the shared instance.</summary>
    public static SystemClock Instance { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
}
