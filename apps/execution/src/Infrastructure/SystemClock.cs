using Cadence.Apps.Execution.Application.Abstractions;

namespace Cadence.Apps.Execution.Infrastructure;

/// <summary>
/// Production <see cref="IClock"/> implementation, wrapping the real system clock.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
