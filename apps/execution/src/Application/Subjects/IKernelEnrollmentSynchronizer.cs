using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Outbound port synchronizing subject screening, lifecycle transitions, and signed withdrawals in Execution with
/// pseudonymous participant registration and site enrollment in the Clinical Kernel over signed HTTP
/// (K-XS-1, K-XS-2: never via <c>Cadence.Kernel.Persistence</c>).
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
        CancellationToken cancellationToken,
        string? fallbackTenantId = null,
        string? fallbackActorId = null);

    /// <summary>
    /// Synchronizes an enrollment status transition (<c>Enrolled</c>, <c>Randomized</c>, <c>Active</c>,
    /// <c>Completed</c>, <c>ScreenFailed</c>) in the Clinical Kernel before Execution commits the subject state.
    /// </summary>
    Task<Result<KernelEnrollmentSyncResult>> SynchronizeStatusChangeAsync(
        string studyCode,
        string subjectCode,
        string targetStatus,
        DateTimeOffset effectiveAt,
        string changeReason,
        CancellationToken cancellationToken,
        string? idempotencyToken = null);

    /// <summary>
    /// Synchronizes a signed participant withdrawal (<c>AttestationMeaning.Withdrawn</c> + <c>WithdrawParticipant</c>)
    /// in the Clinical Kernel using the caller's single-use <c>X-Sig-Token</c> before Execution commits the withdrawal.
    /// </summary>
    Task<Result<KernelWithdrawalSyncResult>> SynchronizeWithdrawalAsync(
        string studyCode,
        string subjectCode,
        string withdrawalKind,
        DateTimeOffset effectiveAt,
        string changeReason,
        CancellationToken cancellationToken);
}

/// <summary>Result returned when a kernel enrollment status transition succeeds or is skipped for a non-tenant caller.</summary>
public sealed record KernelEnrollmentSyncResult(Guid EnrollmentId, string Status, int? EnrollmentNumber);

/// <summary>Result returned when a kernel signed withdrawal succeeds or is skipped for a non-tenant caller.</summary>
public sealed record KernelWithdrawalSyncResult(Guid EnrollmentId, Guid AttestationId, string Status, string WithdrawalKind);
