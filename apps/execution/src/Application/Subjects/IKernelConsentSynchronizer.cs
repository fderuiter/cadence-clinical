using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Synchronizes signed participant consent from Execution into the Clinical Kernel's bitemporal
/// <c>ConsentStatus</c> and <c>Applicability</c> aggregates (<c>POST /api/v1/kernel/consents/sync</c>, PRD-SUB-007).
/// </summary>
public interface IKernelConsentSynchronizer
{
    Task<Result<bool>> SynchronizeConsentAsync(
        string studyCode,
        string? siteCode,
        string subjectCode,
        int versionIndex,
        string? versionTag,
        DateTimeOffset signedAt,
        bool isPaperOverride,
        string changeReason,
        CancellationToken cancellationToken,
        string? explicitTenantCode = null);
}
