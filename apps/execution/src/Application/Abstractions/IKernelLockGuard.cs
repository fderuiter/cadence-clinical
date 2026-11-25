using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Synchronous Clinical Kernel write-lock guard for standalone execution mutations. Queries live <c>Frozen</c> and
/// <c>Locked</c> assertions over HTTP without referencing internal kernel assemblies (<c>K-XS-2</c>).
/// </summary>
public interface IKernelLockGuard
{
    /// <summary>
    /// Verifies that no active write-denying lock (<c>Frozen</c> or <c>Locked</c>) covers <paramref name="studyCode"/>,
    /// optional <paramref name="siteCode"/>, or optional <paramref name="subjectCode"/> for the ambient HTTP caller's
    /// tenant.
    /// </summary>
    Task<Result<bool>> EnsureUnlockedAsync(
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies in one batch pass that no active write-denying lock (<c>Frozen</c> or <c>Locked</c>) covers
    /// <paramref name="studyCode"/> or any of <paramref name="siteCodes"/> for the ambient HTTP caller's tenant.
    /// </summary>
    async Task<Result<bool>> EnsureUnlockedBatchAsync(
        string? studyCode,
        IReadOnlyCollection<string> siteCodes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return true;
        }

        if (siteCodes is null || siteCodes.Count == 0)
        {
            return await EnsureUnlockedAsync(studyCode, null, null, cancellationToken).ConfigureAwait(false);
        }

        var checkedStudyOnly = false;
        foreach (string siteCode in siteCodes)
        {
            if (string.IsNullOrWhiteSpace(siteCode))
            {
                if (checkedStudyOnly)
                {
                    continue;
                }

                checkedStudyOnly = true;
                var studyResult = await EnsureUnlockedAsync(studyCode, null, null, cancellationToken).ConfigureAwait(false);
                if (studyResult.IsFailure)
                {
                    return studyResult;
                }

                continue;
            }

            var siteResult = await EnsureUnlockedAsync(studyCode, siteCode, null, cancellationToken).ConfigureAwait(false);
            if (siteResult.IsFailure)
            {
                return siteResult;
            }
        }

        return true;
    }

    /// <summary>
    /// Verifies that no active write-denying lock (<c>Frozen</c> or <c>Locked</c>) covers <paramref name="studyCode"/>,
    /// optional <paramref name="siteCode"/>, or optional <paramref name="subjectCode"/> when executing as
    /// <paramref name="serviceAccount"/> within <paramref name="tenantCode"/>.
    /// </summary>
    Task<Result<bool>> EnsureUnlockedForServiceAsync(
        ServiceAccount serviceAccount,
        string? tenantCode,
        string? studyCode,
        string? siteCode = null,
        string? subjectCode = null,
        CancellationToken cancellationToken = default);
}
