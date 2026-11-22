using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Modules;

namespace Cadence.Kernel.Application.Execution;

/// <summary>
/// Kernel unit-of-work lock for study governance transitions that coordinate a primary governance aggregate
/// (<c>Effectivity</c>, <c>ConsentStatus</c>, or <c>Enrollment</c>) with its study's bitemporal <c>Applicability</c>
/// aggregates in one atomic audited transaction (spec §7.5, K-TX-1, K-TX-2, K-TIME-6).
/// </summary>
public interface IStudyGovernanceLock
{
    Task LockStudyGovernanceAsync(StudyId studyId, CancellationToken cancellationToken);
}

/// <summary>Extension helper for kernel execution and effectivity handlers.</summary>
public static class StudyGovernanceLocking
{
    public static Task LockStudyGovernanceAsync(
        this IKernelUnitOfWork work,
        StudyId studyId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (work is not IStudyGovernanceLock governance)
        {
            throw new InvalidOperationException("Only the kernel's unit of work supports study governance locks.");
        }

        return governance.LockStudyGovernanceAsync(studyId, cancellationToken);
    }
}
