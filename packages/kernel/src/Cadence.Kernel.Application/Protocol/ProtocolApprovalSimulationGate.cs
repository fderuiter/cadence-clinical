using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Application.Protocol;

/// <summary>Result of evaluating whether a protocol version is ready for Part 11 approval (K-PROT-2, K-SIM-5, ADR-2482).</summary>
/// <param name="ProtocolVersionId">Evaluated protocol version identifier.</param>
/// <param name="Status">Current lifecycle status of the protocol version.</param>
/// <param name="IsAmendment">True when the protocol version was forked from a parent version.</param>
/// <param name="ParentVersionId">Parent protocol version identifier when forked, otherwise null.</param>
/// <param name="Ready">True when the version is frozen in review and satisfies the simulation review gate.</param>
/// <param name="SimulationReportSaved">True when a matching <see cref="ArtifactKind.SimulationReport"/> artifact version exists.</param>
/// <param name="ReviewSigned">True when a verified <see cref="AttestationMeaning.Reviewed"/> attestation exists over a matching simulation report.</param>
/// <param name="SimulationReportArtifactVersionId">Latest matching <see cref="ArtifactVersionId"/> when saved, or the attested one when signed.</param>
/// <param name="ReviewAttestationId">Verified <see cref="AttestationId"/> with meaning <see cref="AttestationMeaning.Reviewed"/> when ready.</param>
/// <param name="Explanation">Structured kernel explanation describing met and unmet approval conditions.</param>
/// <param name="BlockingError">Blocking <see cref="KernelError"/> when approval would be refused, otherwise null.</param>
public sealed record ProtocolApprovalReadinessEvaluation(
    ProtocolVersionId ProtocolVersionId,
    ProtocolVersionStatus Status,
    bool IsAmendment,
    ProtocolVersionId? ParentVersionId,
    bool Ready,
    bool SimulationReportSaved,
    bool ReviewSigned,
    ArtifactVersionId? SimulationReportArtifactVersionId,
    AttestationId? ReviewAttestationId,
    Explanation Explanation,
    KernelError? BlockingError);

/// <summary>Read-only precheck for the forked protocol amendment simulation review gate (K-PROT-2, K-SIM-5).</summary>
public static class ProtocolApprovalSimulationGate
{
    /// <summary>Verifies whether a forked protocol version satisfies the simulation review gate prior to approval.</summary>
    public static async Task<KernelError?> CheckAsync(
        TenantId tenantId,
        IKernelUnitOfWorkFactory unitOfWorkFactory,
        IAttestationVerifier verifier,
        Actor actor,
        DateTimeOffset recordedAt,
        ProtocolVersionId versionId,
        AttestationId? explicitReviewAttestationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWorkFactory);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(actor);

        var probeCommand = new ApproveProtocolVersion(tenantId, versionId, AttestationId.New(), "precheck", "Simulation review gate precheck", explicitReviewAttestationId);
        var execution = new CommandExecution(
            probeCommand,
            probeCommand.GetType().Name,
            actor,
            recordedAt,
            Guid.CreateVersion7(),
            new CommandMetadata(Guid.CreateVersion7()),
            "precheck");
        await using var tx = unitOfWorkFactory.Begin(execution);
        var amended = await tx.Work.ReadAsync<ProtocolVersion>(versionId, cancellationToken).ConfigureAwait(false);
        if (amended?.ParentVersionId is not { } parentId)
        {
            return null;
        }

        return await VerifySimulationReviewGateAsync(
            verifier,
            tenantId,
            tx.Work,
            amended,
            parentId,
            explicitReviewAttestationId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Evaluates full approval readiness, matching simulation report evidence, and verified review attestation for a protocol version.</summary>
    public static async Task<ProtocolApprovalReadinessEvaluation?> EvaluateAsync(
        TenantId tenantId,
        IKernelUnitOfWorkFactory unitOfWorkFactory,
        IAttestationVerifier verifier,
        Actor actor,
        DateTimeOffset recordedAt,
        ProtocolVersionId versionId,
        AttestationId? explicitReviewAttestationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWorkFactory);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(actor);

        var probeCommand = new ApproveProtocolVersion(tenantId, versionId, AttestationId.New(), "precheck", "Approval readiness precheck", explicitReviewAttestationId);
        var execution = new CommandExecution(
            probeCommand,
            probeCommand.GetType().Name,
            actor,
            recordedAt,
            Guid.CreateVersion7(),
            new CommandMetadata(Guid.CreateVersion7()),
            "precheck");
        await using var tx = unitOfWorkFactory.Begin(execution);
        if (await tx.Work.ReadAsync<ProtocolVersion>(versionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return null;
        }

        return await EvaluateVersionReadinessAsync(
            verifier,
            tenantId,
            tx.Work,
            version,
            explicitReviewAttestationId,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<KernelError?> VerifySimulationReviewGateAsync(
        IAttestationVerifier verifier,
        TenantId tenantId,
        IKernelUnitOfWork work,
        ProtocolVersion amended,
        ProtocolVersionId parentId,
        AttestationId? explicitReviewAttestationId,
        CancellationToken cancellationToken)
    {
        var subjectRef = NodeRef.For(amended.Id, amended.VersionIndex);
        var inReviewCondition = EvaluatedCondition.Satisfied(
            "protocol.version.in_review",
            $"Protocol version {amended.Number} is frozen in review ({amended.ContentHash}).",
            subjectRef);
        var gate = await InspectSimulationReviewGateAsync(
            verifier,
            tenantId,
            work,
            amended,
            parentId,
            explicitReviewAttestationId,
            inReviewCondition,
            cancellationToken).ConfigureAwait(false);
        return gate.BlockingError;
    }

    private static async Task<ProtocolApprovalReadinessEvaluation> EvaluateVersionReadinessAsync(
        IAttestationVerifier verifier,
        TenantId tenantId,
        IKernelUnitOfWork work,
        ProtocolVersion version,
        AttestationId? explicitReviewAttestationId,
        CancellationToken cancellationToken)
    {
        var subjectRef = NodeRef.For(version.Id, version.VersionIndex);
        var unversionedSubject = NodeRef.For(version.Id);
        var isAmendment = version.ParentVersionId is not null;

        if (version.Status != ProtocolVersionStatus.InReview || string.IsNullOrWhiteSpace(version.ContentHash))
        {
            var conditions = new List<EvaluatedCondition>
            {
                EvaluatedCondition.Unsatisfied(
                    "protocol.version.in_review",
                    $"Protocol version {version.Number} has status {version.Status}; submit it for review to freeze its content hash before approving.",
                    subjectRef),
            };
            if (version.ParentVersionId is { } draftParentId)
            {
                var draftParentRef = NodeRef.For(draftParentId);
                conditions.Add(EvaluatedCondition.Unsatisfied(
                    "protocol.amendment.simulation_saved",
                    "Save an unmasked SimulationReport artifact binding the parent and frozen amended protocol versions.",
                    subjectRef,
                    draftParentRef));
                conditions.Add(EvaluatedCondition.Unsatisfied(
                    "protocol.amendment.simulation_reviewed",
                    "Record a verified 21 CFR Part 11 Reviewed signature over the matching SimulationReport artifact version.",
                    subjectRef,
                    draftParentRef));
            }

            var notInReviewExplanation = new Explanation(
                subjectRef,
                $"Protocol version {version.Number} cannot be approved while its status is {version.Status}.",
                "kernel.protocol.approval_readiness",
                [new ExplanationStep($"Protocol version {version.Number} has status {version.Status} rather than InReview.", [subjectRef])],
                conditions,
                version.Status == ProtocolVersionStatus.Draft
                    ? [new NextAction("Submit the protocol version for review to freeze its content hash before requesting approval.", "protocol.submit_for_review", unversionedSubject)]
                    : []);
            var blocking = ProtocolErrors.InvalidTransition with { Explanation = notInReviewExplanation };
            return new ProtocolApprovalReadinessEvaluation(
                version.Id,
                version.Status,
                isAmendment,
                version.ParentVersionId,
                Ready: false,
                SimulationReportSaved: false,
                ReviewSigned: false,
                SimulationReportArtifactVersionId: null,
                ReviewAttestationId: null,
                notInReviewExplanation,
                blocking);
        }

        var inReviewCondition = EvaluatedCondition.Satisfied(
            "protocol.version.in_review",
            $"Protocol version {version.Number} is frozen in review ({version.ContentHash}).",
            subjectRef);

        if (version.ParentVersionId is not { } parentId)
        {
            var v1ReadyExplanation = new Explanation(
                subjectRef,
                $"Initial protocol version {version.Number} is frozen in review and ready for 21 CFR Part 11 approval.",
                "kernel.protocol.approval_readiness",
                [new ExplanationStep($"Protocol version {version.Number} is the initial study version, so amendment simulation sign-off is not required.", [subjectRef])],
                [inReviewCondition],
                []);
            return new ProtocolApprovalReadinessEvaluation(
                version.Id,
                version.Status,
                IsAmendment: false,
                ParentVersionId: null,
                Ready: true,
                SimulationReportSaved: false,
                ReviewSigned: false,
                SimulationReportArtifactVersionId: null,
                ReviewAttestationId: null,
                v1ReadyExplanation,
                BlockingError: null);
        }

        var gate = await InspectSimulationReviewGateAsync(
            verifier,
            tenantId,
            work,
            version,
            parentId,
            explicitReviewAttestationId,
            inReviewCondition,
            cancellationToken).ConfigureAwait(false);
        return new ProtocolApprovalReadinessEvaluation(
            version.Id,
            version.Status,
            IsAmendment: true,
            parentId,
            Ready: gate.BlockingError is null,
            gate.SimulationReportSaved,
            gate.ReviewSigned,
            gate.SimulationReportArtifactVersionId,
            gate.ReviewAttestationId,
            gate.Explanation,
            gate.BlockingError);
    }

    private static async Task<(
        bool SimulationReportSaved,
        bool ReviewSigned,
        ArtifactVersionId? SimulationReportArtifactVersionId,
        AttestationId? ReviewAttestationId,
        Explanation Explanation,
        KernelError? BlockingError)> InspectSimulationReviewGateAsync(
        IAttestationVerifier verifier,
        TenantId tenantId,
        IKernelUnitOfWork work,
        ProtocolVersion amended,
        ProtocolVersionId parentId,
        AttestationId? explicitReviewAttestationId,
        EvaluatedCondition inReviewCondition,
        CancellationToken cancellationToken)
    {
        var subjectRef = NodeRef.For(amended.Id, amended.VersionIndex);
        var parent = await work.ReadAsync<ProtocolVersion>(parentId, cancellationToken).ConfigureAwait(false);
        var parentRef = parent is not null ? NodeRef.For(parent.Id, parent.VersionIndex) : NodeRef.For(parentId);
        var studyRef = NodeRef.For(amended.StudyId);
        if (parent?.ContentHash is not { } parentHash || amended.ContentHash is not { } amendedHash)
        {
            const string detail = "The parent protocol version does not have a frozen content hash.";
            var refusal = SimulationReviewRefusal(
                amended,
                detail,
                [subjectRef, parentRef],
                [
                    inReviewCondition,
                    EvaluatedCondition.Unsatisfied("protocol.amendment.simulation_saved", detail, subjectRef, parentRef),
                    EvaluatedCondition.Unsatisfied("protocol.amendment.simulation_reviewed", detail, subjectRef, parentRef),
                ],
                new NextAction("Run impact simulation on the frozen amendment, save the SimulationReport artifact, and record a Reviewed electronic signature over its artifact_version before approving.", "simulation.save_report", studyRef));
            return (false, false, null, null, refusal.Explanation!, refusal);
        }

        var studyId = amended.StudyId;
        var reports = await work.ReadAllAsync<Artifact>(
            a => a.StudyId == studyId && a.Kind == ArtifactKind.SimulationReport,
            cancellationToken).ConfigureAwait(false);

        var matchingVersions = new Dictionary<Guid, ArtifactVersion>();
        foreach (var report in reports)
        {
            foreach (var artifactVersion in report.Versions)
            {
                if (SimulationArtifactBinding.TryParseStorageRef(artifactVersion.StorageRef, out var binding)
                    && binding is not null
                    && string.Equals(binding.ResultHash, artifactVersion.ContentHash, StringComparison.Ordinal)
                    && binding.MatchesProtocolAmendment(parentId, parentHash, amended.Id, amendedHash))
                {
                    matchingVersions[artifactVersion.Id.Value] = artifactVersion;
                }
            }
        }

        if (matchingVersions.Count == 0)
        {
            var detail = $"No saved SimulationReport artifact in {studyRef} binds parent {parentRef} ({parentHash}) to amended version {subjectRef} ({amendedHash}).";
            var refusal = SimulationReviewRefusal(
                amended,
                detail,
                [subjectRef, parentRef, studyRef],
                [
                    inReviewCondition,
                    EvaluatedCondition.Unsatisfied("protocol.amendment.simulation_saved", detail, subjectRef, parentRef, studyRef),
                    EvaluatedCondition.Unsatisfied("protocol.amendment.simulation_reviewed", "Record a verified Reviewed electronic signature over the saved SimulationReport artifact version.", subjectRef, parentRef),
                ],
                new NextAction("Run impact simulation on the frozen amendment, save the SimulationReport artifact, and record a Reviewed electronic signature over its artifact_version before approving.", "simulation.save_report", studyRef));
            return (false, false, null, null, refusal.Explanation!, refusal);
        }

        var latestReportVersion = matchingVersions.Values
            .OrderByDescending(v => v.RecordedAt)
            .ThenByDescending(v => v.Number)
            .First();
        var latestReportRef = NodeRef.For(latestReportVersion.Id);
        var savedCondition = EvaluatedCondition.Satisfied(
            "protocol.amendment.simulation_saved",
            $"Saved SimulationReport {latestReportRef} binds parent {parentRef} and amended {subjectRef}.",
            subjectRef,
            parentRef,
            latestReportRef);

        if (explicitReviewAttestationId is { } reviewId)
        {
            var explicitAttestation = await work.ReadAsync<Attestation>(reviewId, cancellationToken).ConfigureAwait(false);
            if (explicitAttestation is null
                || !string.Equals(explicitAttestation.TargetKind, NodeKinds.ArtifactVersion, StringComparison.Ordinal)
                || !matchingVersions.TryGetValue(explicitAttestation.TargetId, out var targetedVersion))
            {
                const string detail = "The named ReviewAttestationId does not target a SimulationReport artifact version matching the parent and amended protocol versions.";
                var refusal = SimulationReviewRefusal(
                    amended,
                    detail,
                    [subjectRef, parentRef, latestReportRef],
                    [
                        inReviewCondition,
                        savedCondition,
                        EvaluatedCondition.Unsatisfied("protocol.amendment.simulation_reviewed", detail, latestReportRef),
                    ],
                    new NextAction("Record a Reviewed electronic signature over the matching SimulationReport artifact_version before approving.", "attestation.sign", latestReportRef));
                return (true, false, latestReportVersion.Id, null, refusal.Explanation!, refusal);
            }

            var targetedRef = NodeRef.For(targetedVersion.Id);
            var verification = await verifier.VerifyAsync(
                new AttestationRequirement(
                    tenantId,
                    reviewId,
                    AttestationMeaning.Reviewed,
                    targetedVersion.SignableTarget,
                    Actor.Person(explicitAttestation.ActorId)),
                cancellationToken).ConfigureAwait(false);
            if (verification.Satisfied)
            {
                var attestationRef = NodeRef.For(reviewId);
                var explanation = BuildSatisfiedSimulationGateExplanation(amended, subjectRef, parentRef, targetedRef, attestationRef, inReviewCondition);
                return (true, true, targetedVersion.Id, reviewId, explanation, null);
            }

            var rejectDetail = verification.Reason ?? "The simulation review attestation is not valid.";
            var rejectRefusal = SimulationReviewRefusal(
                amended,
                rejectDetail,
                [subjectRef, parentRef, targetedRef, NodeRef.For(reviewId)],
                [
                    inReviewCondition,
                    savedCondition,
                    EvaluatedCondition.Unsatisfied("protocol.amendment.simulation_reviewed", rejectDetail, targetedRef, NodeRef.For(reviewId)),
                ],
                new NextAction("Record a fresh Reviewed electronic signature over the matching SimulationReport artifact_version before approving.", "attestation.sign", targetedRef));
            return (true, false, targetedVersion.Id, null, rejectRefusal.Explanation!, rejectRefusal);
        }

        var reviewAttestations = await work.ReadAllAsync<Attestation>(
            a => a.Meaning == AttestationMeaning.Reviewed && a.TargetKind == NodeKinds.ArtifactVersion,
            cancellationToken).ConfigureAwait(false);

        foreach (var candidateAttestation in reviewAttestations.OrderByDescending(a => a.RecordedAt))
        {
            if (!matchingVersions.TryGetValue(candidateAttestation.TargetId, out var matchedVersion))
            {
                continue;
            }

            var verification = await verifier.VerifyAsync(
                new AttestationRequirement(
                    tenantId,
                    candidateAttestation.Id,
                    AttestationMeaning.Reviewed,
                    matchedVersion.SignableTarget,
                    Actor.Person(candidateAttestation.ActorId)),
                cancellationToken).ConfigureAwait(false);
            if (verification.Satisfied)
            {
                var matchedRef = NodeRef.For(matchedVersion.Id);
                var attestationRef = NodeRef.For(candidateAttestation.Id);
                var explanation = BuildSatisfiedSimulationGateExplanation(amended, subjectRef, parentRef, matchedRef, attestationRef, inReviewCondition);
                return (true, true, matchedVersion.Id, candidateAttestation.Id, explanation, null);
            }
        }

        const string unreviewedDetail = "No verified Reviewed attestation exists over a matching SimulationReport artifact version.";
        var unreviewedRefusal = SimulationReviewRefusal(
            amended,
            unreviewedDetail,
            [subjectRef, parentRef, latestReportRef],
            [
                inReviewCondition,
                savedCondition,
                EvaluatedCondition.Unsatisfied("protocol.amendment.simulation_reviewed", unreviewedDetail, latestReportRef),
            ],
            new NextAction("Record a Reviewed electronic signature over the saved SimulationReport artifact_version before approving.", "attestation.sign", latestReportRef));
        return (true, false, latestReportVersion.Id, null, unreviewedRefusal.Explanation!, unreviewedRefusal);
    }

    private static Explanation BuildSatisfiedSimulationGateExplanation(
        ProtocolVersion amended,
        NodeRef subjectRef,
        NodeRef parentRef,
        NodeRef reportVersionRef,
        NodeRef attestationRef,
        EvaluatedCondition inReviewCondition) =>
        new(
            subjectRef,
            $"Forked protocol version {amended.Number} satisfies the simulation review gate and is ready for 21 CFR Part 11 approval.",
            "kernel.protocol.simulation_gate",
            [new ExplanationStep($"SimulationReport {reportVersionRef} is attested with meaning Reviewed by {attestationRef}.", [subjectRef, parentRef, reportVersionRef, attestationRef])],
            [
                inReviewCondition,
                EvaluatedCondition.Satisfied(
                    "protocol.amendment.simulation_saved",
                    $"Saved SimulationReport {reportVersionRef} binds parent {parentRef} and amended {subjectRef}.",
                    subjectRef,
                    parentRef,
                    reportVersionRef),
                EvaluatedCondition.Satisfied(
                    "protocol.amendment.simulation_reviewed",
                    $"Verified Reviewed attestation {attestationRef} covers SimulationReport {reportVersionRef}.",
                    reportVersionRef,
                    attestationRef),
            ],
            []);

    private static KernelError SimulationReviewRefusal(
        ProtocolVersion amended,
        string detail,
        IReadOnlyList<NodeRef> evidence,
        IReadOnlyList<EvaluatedCondition> conditions,
        NextAction nextAction) =>
        ProtocolErrors.SimulationReviewRequired.WithMessage($"{ProtocolErrors.SimulationReviewRequired.Message} {detail}") with
        {
            Explanation = new Explanation(
                NodeRef.For(amended.Id, amended.VersionIndex),
                $"Approval of forked protocol version {amended.Number} is refused until a matching SimulationReport artifact is attested with meaning Reviewed (K-PROT-2, K-SIM-5).",
                "kernel.protocol.simulation_gate",
                [new ExplanationStep(detail, evidence)],
                conditions,
                [nextAction]),
        };
}
