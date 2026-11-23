using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Verifies against the Clinical Kernel that no active <c>Frozen</c> or <c>Locked</c> data lock
/// covers the target study, site, or subject scope before standalone Execution writes proceed.
/// </summary>
public interface IKernelLockGuard
{
    /// <summary>
    /// Checks whether writes are permitted for the given clinical scope. Returns <see langword="null"/> when
    /// unlocked, or a <see cref="DomainError"/> describing why the write must be rejected (HTTP 423 when locked,
    /// or fail-closed conflict/forbidden when the kernel lock check cannot succeed).
    /// </summary>
    Task<DomainError?> CheckWriteAllowedAsync(
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default);
}
