using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Background;

/// <summary>
/// Coordinates single-leader background passes across replicas (ADR-2601) without exposing kernel persistence types to
/// application modules (K-XS-2).
/// </summary>
public interface IKernelLeaderLock
{
    /// <summary>
    /// Runs <paramref name="pass"/> if this replica acquires <paramref name="job"/>'s session advisory lock; returns
    /// <see langword="false"/> when another replica currently holds it.
    /// </summary>
    Task<bool> TryRunAsync(string job, Func<CancellationToken, Task> pass, CancellationToken cancellationToken);
}
