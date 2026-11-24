using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Shared endpoint helper that translates a failed <see cref="IKernelLockGuard"/> check into an RFC 9457
/// <c>423 Locked</c> problem response via <see cref="DomainErrorHttpMapping"/>.
/// </summary>
internal static class ExecutionLockGuard
{
    internal static async Task<IResult?> EnsureUnlockedAsync(
        IKernelLockGuard? lockGuard,
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default)
    {
        if (lockGuard is null || string.IsNullOrWhiteSpace(studyCode))
        {
            return null;
        }

        var check = await lockGuard
            .EnsureUnlockedAsync(studyCode, siteCode, subjectCode, cancellationToken)
            .ConfigureAwait(false);
        return check.IsFailure ? check.Error!.ToProblemResult() : null;
    }
}
