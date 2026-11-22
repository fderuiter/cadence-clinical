using System.Linq;
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
/// Whether new evidence may be captured for an enrollment's participant (spec §7.4, §7.5, K-TIME-6, K-CONS-2, PRD-SUB-007):
/// recording an observation, opening a form instance, or saving a new item value in one. Corrections to previously recorded
/// values are not new capture and are permitted.
/// </summary>
internal static class CaptureConditions
{
    /// <summary>Producer name on explanations of capture refusals (K-EXP-1).</summary>
    public const string ExplanationProducer = "kernel.capture";

    /// <summary>Code of the condition that the participant takes part in the study.</summary>
    public const string NotWithdrawn = "participation.not-withdrawn";

    /// <summary>Code of the condition that the enrollment's study site is active (PRD-SSU-003).</summary>
    public const string StudySiteActive = "study_site.active";

    /// <summary>Code of the condition that protocol applicability is consistent (K-TIME-6).</summary>
    public const string ApplicabilityConsistent = "applicability.consistent";

    /// <summary>Code of the condition that no protocol amendment is awaiting participant re-consent (PRD-SUB-007).</summary>
    public const string ReconsentCompleted = "consent.reconsent-completed";

    public static async Task<KernelError?> RefusalAsync(
        IKernelUnitOfWork work, EnrollmentId enrollmentId, string commandType, CancellationToken cancellationToken)
    {
        if (await work.ReadAsync<Enrollment>(enrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return null;
        }

        var enrollmentEvidence = NodeRef.For(enrollment.Id, enrollment.VersionIndex);
        if (enrollment.Status == EnrollmentStatus.Withdrawn)
        {
            var condition = EvaluatedCondition.Unsatisfied(
                NotWithdrawn,
                $"The participant takes part in the study; enrollment {enrollment.Id} was withdrawn at {enrollment.StatusSince:O}.",
                enrollmentEvidence);
            var reason = ObservationErrors.ParticipantWithdrawn.Message;
            return ObservationErrors.ParticipantWithdrawn with
            {
                Explanation = new Explanation(
                    enrollmentEvidence,
                    $"{commandType} is not permitted.",
                    ExplanationProducer,
                    [new ExplanationStep(reason, [enrollmentEvidence])],
                    [condition],
                    [new NextAction("Correct a value captured before the withdrawal, or capture data for an enrollment that is not withdrawn.")]),
            };
        }

        var studySite = await work.ReadAsync<StudySite>(enrollment.StudySiteId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(studySite?.Status, Execution.ActivateStudySiteHandler.ActiveStatus, System.StringComparison.Ordinal))
        {
            var siteEvidence = NodeRef.For(enrollment.StudySiteId);
            var condition = EvaluatedCondition.Unsatisfied(
                StudySiteActive,
                $"Study site {enrollment.StudySiteId} has status '{studySite?.Status ?? "Unknown"}' and must be Active before capturing clinical data.",
                siteEvidence,
                enrollmentEvidence);
            return ObservationErrors.StudySiteNotActive with
            {
                Explanation = new Explanation(
                    enrollmentEvidence,
                    $"{commandType} is not permitted.",
                    ExplanationProducer,
                    [new ExplanationStep(ObservationErrors.StudySiteNotActive.Message, [siteEvidence, enrollmentEvidence])],
                    [condition],
                    [new NextAction("Activate the study site after greenlight certification and protocol effectivity before capturing clinical data.")]),
            };
        }

        var applicability = (await work.ReadAllAsync<Applicability>(
            a => a.EnrollmentId == enrollmentId,
            cancellationToken).ConfigureAwait(false)).SingleOrDefault();
        if (applicability is null)
        {
            return null;
        }

        var applicabilityEvidence = NodeRef.For(applicability.Id, applicability.VersionIndex);
        if (applicability.IsInconsistent)
        {
            var detail = string.IsNullOrWhiteSpace(applicability.InconsistencyReason)
                ? $"Enrollment {enrollment.Id} has an unresolved protocol applicability inconsistency."
                : applicability.InconsistencyReason;
            var condition = EvaluatedCondition.Unsatisfied(ApplicabilityConsistent, detail, applicabilityEvidence, enrollmentEvidence);
            return ObservationErrors.ApplicabilityInconsistent with
            {
                Explanation = new Explanation(
                    enrollmentEvidence,
                    $"{commandType} is not permitted.",
                    ExplanationProducer,
                    [new ExplanationStep(ObservationErrors.ApplicabilityInconsistent.Message, [applicabilityEvidence, enrollmentEvidence])],
                    [condition],
                    [new NextAction("Resolve the protocol applicability inconsistency on the enrollment before capturing new clinical data.")]),
            };
        }

        var pending = applicability.CurrentPendingTransitions();
        if (pending.Count > 0)
        {
            var targetIds = pending.Select(p => p.TargetVersionId).Distinct().ToList();
            var targetVersions = await work.ReadAllAsync<ProtocolVersion>(v => targetIds.Contains(v.Id), cancellationToken).ConfigureAwait(false);
            var labels = targetVersions.OrderBy(v => v.Number).Select(v => $"v{v.Number}").ToList();
            var versionLabel = labels.Count > 0 ? string.Join(", ", labels) : string.Join(", ", targetIds);
            var condition = EvaluatedCondition.Unsatisfied(
                ReconsentCompleted,
                $"Enrollment {enrollment.Id} requires participant re-consent for protocol version {versionLabel} before new data capture.",
                applicabilityEvidence,
                enrollmentEvidence);
            return ObservationErrors.ReconsentRequired with
            {
                Explanation = new Explanation(
                    enrollmentEvidence,
                    $"{commandType} is not permitted.",
                    ExplanationProducer,
                    [new ExplanationStep(ObservationErrors.ReconsentRequired.Message, [applicabilityEvidence, enrollmentEvidence])],
                    [condition],
                    [new NextAction($"Record signed participant consent covering protocol version {versionLabel} to unlock new visit forms and observations.")]),
            };
        }

        return null;
    }
}
