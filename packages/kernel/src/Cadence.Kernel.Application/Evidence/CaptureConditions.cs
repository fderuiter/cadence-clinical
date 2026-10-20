using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Application.Evidence;

/// <summary>
/// Whether new evidence may be captured for an enrollment's participant (spec §7.4, K-CONS-2): recording an
/// observation, opening a form instance, or saving a new value in one. Today the one condition is that the participant is
/// not withdrawn; corrections of values captured before the withdrawal are not new capture and are not checked here.
/// </summary>
/// <remarks>
/// This is the seam for computable consent (#4777): once consent scopes exist, the condition becomes
/// <c>ConsentAllows</c> for the capture purpose at the capture time (K-CONS-1), so a withdrawal closes only the
/// withdrawn scopes and capture under a remaining scope goes on. The refusal carries an <see cref="Explanation"/>
/// from this producer, which the command pipeline audits (<see cref="Commands.IKernelCommandTransaction.RecordRefusalAsync"/>).
/// </remarks>
internal static class CaptureConditions
{
    /// <summary>Producer name on explanations of capture refusals (K-EXP-1).</summary>
    public const string ExplanationProducer = "kernel.capture";

    /// <summary>Code of the condition that the participant takes part in the study.</summary>
    public const string NotWithdrawn = "participation.not-withdrawn";

    /// <summary>
    /// Reads the enrollment and returns <see cref="ObservationErrors.ParticipantWithdrawn"/>, with its explanation, when
    /// the participant is withdrawn; null when capture may go on. Call it after sharing the scope locks that cover the
    /// write (<see cref="IKernelUnitOfWork.ShareScopesAsync"/>, which includes the enrollment's): a withdrawal takes the
    /// enrollment's scope lock exclusively, so it either commits before this read and is seen, or waits for the capture.
    /// </summary>
    public static async Task<KernelError?> RefusalAsync(
        IKernelUnitOfWork work, EnrollmentId enrollmentId, string commandType, CancellationToken cancellationToken)
    {
        if (await work.ReadAsync<Enrollment>(enrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment
            || enrollment.Status != EnrollmentStatus.Withdrawn)
        {
            return null;
        }

        var evidence = NodeRef.For(enrollment.Id, enrollment.VersionIndex);
        var condition = EvaluatedCondition.Unsatisfied(
            NotWithdrawn,
            $"The participant takes part in the study; enrollment {enrollment.Id} was withdrawn at {enrollment.StatusSince:O}.",
            evidence);
        var reason = ObservationErrors.ParticipantWithdrawn.Message;
        return ObservationErrors.ParticipantWithdrawn with
        {
            Explanation = new Explanation(
                evidence,
                $"{commandType} is not permitted.",
                ExplanationProducer,
                [new ExplanationStep(reason, [evidence])],
                [condition],
                [new NextAction("Correct a value captured before the withdrawal, or capture data for an enrollment that is not withdrawn.")]),
        };
    }
}
