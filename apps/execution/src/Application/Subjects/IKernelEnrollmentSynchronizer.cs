using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Outbound port synchronizing subject screening in Execution with pseudonymous participant registration and site
/// enrollment screening in the Clinical Kernel over signed HTTP (K-XS-1, K-XS-2: never via <c>Cadence.Kernel.Persistence</c>).
/// </summary>
public interface IKernelEnrollmentSynchronizer
{
    /// <summary>
    /// Ensures the participant and screening enrollment exist in the Clinical Kernel for the caller's tenant before
    /// Execution commits the subject record.
    /// </summary>
    Task<Result<Guid>> SynchronizeScreeningEnrollmentAsync(
        Guid participantId,
        string studyCode,
        string siteCode,
        string subjectCode,
        DateTimeOffset screenedAt,
        string changeReason,
        CancellationToken cancellationToken);
}
