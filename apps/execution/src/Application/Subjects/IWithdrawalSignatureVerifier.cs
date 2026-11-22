using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Port verifying and consuming a single-use Part 11 step-up token (<c>X-Sig-Token</c>) for participant withdrawal
/// when an HTTP request is handled directly by Execution without already being consumed by the Clinical Kernel.
/// </summary>
public interface IWithdrawalSignatureVerifier
{
    /// <summary> Canonical HTTP action bound into withdrawal step-up tokens. </summary>
    public const string Action = "/api/v1/kernel/enrollments/withdraw";

    /// <summary> Canonical semantic action bound into withdrawal step-up tokens. </summary>
    public const string SemanticAction = "kernel.enrollment.withdraw";

    /// <summary>
    /// Verifies and consumes <c>X-Sig-Token</c> when an ambient HTTP request is active; succeeds as a no-op when
    /// invoked outside an HTTP request context (in-memory application-layer tests).
    /// </summary>
    Task<Result<bool>> VerifyAndConsumeIfHttpAsync(CancellationToken cancellationToken);
}
