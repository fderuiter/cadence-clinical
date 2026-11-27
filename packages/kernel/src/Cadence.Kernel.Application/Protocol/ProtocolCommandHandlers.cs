using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Trust;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Protocol;

/// <summary>Handles <see cref="CreateProtocolDraft"/>: a study's first protocol version.</summary>
internal sealed class CreateProtocolDraftHandler : ICommandHandler<CreateProtocolDraft, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        CreateProtocolDraft command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (!await context.Work.ExistsAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false))
        {
            return ProtocolErrors.StudyUnknown;
        }

        var studyId = command.StudyId;
        await context.Work.LockAsync<Study>(studyId, cancellationToken).ConfigureAwait(false);
        var existing = await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false);
        CommandResult<ProtocolVersion> created;
        if (existing.Count == 0)
        {
            created = ProtocolVersion.CreateFirst(command.ProtocolVersionId, command.TenantId, command.StudyId, command.Content);
        }
        else if (existing.All(v => v.Status is ProtocolVersionStatus.Withdrawn or ProtocolVersionStatus.Superseded))
        {
            var nextNumber = existing.Max(v => v.Number) + 1;
            created = ProtocolVersion.CreateRecoveryDraft(command.ProtocolVersionId, command.TenantId, command.StudyId, nextNumber, command.Content);
        }
        else
        {
            return ProtocolErrors.FirstVersionExists;
        }

        if (created.Value is not { } version)
        {
            return created.Error!;
        }

        context.Work.Add(version);
        context.Work.Raise(new ProtocolVersionDrafted(version.TenantId, version.StudyId, version.Id, version.Number, null, null));
        return ProtocolVersionStates.Of(version);
    }
}

/// <summary>Handles <see cref="ForkProtocolVersion"/>: an amended draft numbered after every version in the study.</summary>
internal sealed class ForkProtocolVersionHandler : ICommandHandler<ForkProtocolVersion, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ForkProtocolVersion command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (await context.Work.ReadAsync<ProtocolVersion>(command.ParentVersionId, cancellationToken).ConfigureAwait(false) is not { } parent)
        {
            return KernelErrors.NotFound.WithMessage("The parent protocol version does not exist.");
        }

        // Numbers are taken under the study's lock, so concurrent forks number one after another (K-TX-4); the unique
        // (tenant, study, number) index stays as the last line of defence.
        var studyId = parent.StudyId;
        await context.Work.LockAsync<Study>(studyId, cancellationToken).ConfigureAwait(false);
        var siblings = await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false);
        var number = siblings.Max(v => v.Number) + 1;

        var forked = ProtocolVersion.Fork(parent, command.ProtocolVersionId, number, command.Amendment);
        if (forked.Value is not { } version)
        {
            return forked.Error!;
        }

        context.Work.Add(version);

        // The fork is derived from the approved parent it copies, which is what amendment impact walks from (K-PROT-2).
        context.Work.LinkInput(NodeRef.For(parent.Id, parent.VersionIndex), LineageEdgeKind.DerivedFrom);
        context.Work.Raise(new ProtocolVersionDrafted(
            version.TenantId, version.StudyId, version.Id, version.Number, version.ParentVersionId, version.Amendment));
        return ProtocolVersionStates.Of(version);
    }
}

/// <summary>Handles <see cref="ReviseProtocolDraft"/>.</summary>
internal sealed class ReviseProtocolDraftHandler : ICommandHandler<ReviseProtocolDraft, ProtocolVersionState>
{
    public Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ReviseProtocolDraft command, ICommandContext context, CancellationToken cancellationToken) =>
        ProtocolVersionStates.ChangeAsync(context, command.ProtocolVersionId, cancellationToken, version =>
            version.Revise(command.Content, command.Amendment)
            ?? Raise(context, new ProtocolVersionRevised(version.TenantId, version.StudyId, version.Id)));

    private static KernelError? Raise(ICommandContext context, object domainEvent)
    {
        context.Work.Raise(domainEvent);
        return null;
    }
}

/// <summary>
/// Handles <see cref="SubmitProtocolVersionForReview"/>. Freezing the content is when its references outside the
/// design must resolve (K-SEM-1): every concept version, code-system version and artifact it names must exist in the
/// tenant, so reviewers sign a design whose meaning is pinned. For a forked amendment, any schedule or eligibility
/// element changed relative to the parent must be declared in <see cref="ProtocolAmendment.ImpactedElements"/>
/// (ADR-2475). The registered <see cref="IProtocolSubmissionCheck"/>s then run in registration order; the first
/// refusal stops the submission.
/// </summary>
internal sealed class SubmitProtocolVersionForReviewHandler(IEnumerable<IProtocolSubmissionCheck> checks) : ICommandHandler<SubmitProtocolVersionForReview, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        SubmitProtocolVersionForReview command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelErrors.NotFound;
        }

        if (version.Status == ProtocolVersionStatus.Draft
            && version.ParentVersionId is { } parentId
            && version.Amendment is { } amendment)
        {
            var parent = await context.Work.ReadAsync<ProtocolVersion>(parentId, cancellationToken).ConfigureAwait(false);
            if (parent is not null)
            {
                var soaDiff = ScheduleOfActivitiesDiff.Compare(parent.Design(), version.Design());
                var undeclared = soaDiff.Undeclared(amendment.ImpactedElements);
                if (undeclared.Count > 0)
                {
                    var elementList = string.Join(", ", undeclared);
                    var condition = EvaluatedCondition.Unsatisfied(
                        "protocol.amendment.impacted_elements",
                        $"Declare all changed Schedule of Activities and eligibility elements in ImpactedElements: {elementList}.");
                    return ProtocolErrors.UndeclaredImpactedElements
                        .WithMessage($"{ProtocolErrors.UndeclaredImpactedElements.Message} Undeclared elements: {elementList}.") with
                    {
                        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
                        {
                            ["impactedElements"] = [.. undeclared],
                        },
                        Explanation = new Explanation(
                            NodeRef.For(version.Id, version.VersionIndex),
                            $"Submission of protocol version {version.Number} is refused because its amendment leaves out changed schedule or eligibility elements ({elementList}).",
                            "kernel.protocol.amendment_scope",
                            [new ExplanationStep(ProtocolErrors.UndeclaredImpactedElements.Message, [NodeRef.For(parent.Id, parent.VersionIndex)])],
                            [condition],
                            [new NextAction($"Revise the amendment's ImpactedElements to include: {elementList}.")]),
                    };
                }
            }
        }

        if (version.Status == ProtocolVersionStatus.Draft
            && await UnknownReferenceAsync(context, version.Design().ExternalReferences(), cancellationToken).ConfigureAwait(false) is { } unknown)
        {
            return ProtocolErrors.ReferenceUnknown.WithMessage($"{ProtocolErrors.ReferenceUnknown.Message} Unknown: {unknown}.");
        }

        if (version.Status == ProtocolVersionStatus.Draft)
        {
            foreach (var check in checks)
            {
                if (await check.CheckAsync(version.StudyId, version.Design(), context, cancellationToken).ConfigureAwait(false) is { } refused)
                {
                    return refused;
                }
            }
        }

        if (version.SubmitForReview() is { } error)
        {
            return error;
        }

        context.Work.Raise(new ProtocolVersionSubmittedForReview(version.TenantId, version.StudyId, version.Id, version.ContentHash!));
        return ProtocolVersionStates.Of(version);
    }

    /// <summary>The first reference the tenant cannot resolve, or null. A concept reference needs that version to exist.</summary>
    private static async Task<string?> UnknownReferenceAsync(
        ICommandContext context, DesignExternalReferences references, CancellationToken cancellationToken)
    {
        foreach (var group in references.Concepts.GroupBy(c => c.ConceptId).OrderBy(g => g.Key.Value))
        {
            var concept = await context.Work.ReadAsync<Concept>(group.Key, cancellationToken).ConfigureAwait(false);
            var missing = group.Where(c => concept is null || c.Version > concept.Version).OrderBy(c => c.Version).ToList();
            if (missing.Count > 0)
            {
                return $"concept {missing[0]}";
            }
        }

        foreach (var codeSystemVersion in references.CodeSystemVersions.OrderBy(v => v.Value))
        {
            if (!await context.Work.ExistsAsync<CodeSystemVersion>(codeSystemVersion, cancellationToken).ConfigureAwait(false))
            {
                return $"code-system version {codeSystemVersion}";
            }
        }

        foreach (var artifact in references.Artifacts.OrderBy(a => a.Value))
        {
            if (!await context.Work.ExistsAsync<Artifact>(artifact, cancellationToken).ConfigureAwait(false))
            {
                return $"artifact {artifact}";
            }
        }

        return null;
    }
}

/// <summary>Handles <see cref="ReturnProtocolVersionToDraft"/>.</summary>
internal sealed class ReturnProtocolVersionToDraftHandler : ICommandHandler<ReturnProtocolVersionToDraft, ProtocolVersionState>
{
    public Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ReturnProtocolVersionToDraft command, ICommandContext context, CancellationToken cancellationToken) =>
        ProtocolVersionStates.ChangeAsync(context, command.ProtocolVersionId, cancellationToken, version =>
        {
            if (version.ReturnToDraft() is { } error)
            {
                return error;
            }

            context.Work.Raise(new ProtocolVersionReturnedToDraft(version.TenantId, version.StudyId, version.Id));
            return null;
        });
}

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
    public static Task<KernelError?> CheckAsync(
        TenantId tenantId,
        IKernelUnitOfWorkFactory unitOfWorkFactory,
        IAttestationVerifier verifier,
        Actor actor,
        DateTimeOffset recordedAt,
        ProtocolVersionId versionId,
        AttestationId? explicitReviewAttestationId,
        CancellationToken cancellationToken) =>
        ApproveProtocolVersionHandler.CheckSimulationReviewGateAsync(
            tenantId,
            unitOfWorkFactory,
            verifier,
            actor,
            recordedAt,
            versionId,
            explicitReviewAttestationId,
            cancellationToken);

    /// <summary>Evaluates full approval readiness, matching simulation report evidence, and verified review attestation for a protocol version.</summary>
    public static Task<ProtocolApprovalReadinessEvaluation?> EvaluateAsync(
        TenantId tenantId,
        IKernelUnitOfWorkFactory unitOfWorkFactory,
        IAttestationVerifier verifier,
        Actor actor,
        DateTimeOffset recordedAt,
        ProtocolVersionId versionId,
        AttestationId? explicitReviewAttestationId,
        CancellationToken cancellationToken) =>
        ApproveProtocolVersionHandler.EvaluateApprovalReadinessAsync(
            tenantId,
            unitOfWorkFactory,
            verifier,
            actor,
            recordedAt,
            versionId,
            explicitReviewAttestationId,
            cancellationToken);
}

/// <summary>
/// Handles <see cref="ApproveProtocolVersion"/>: the named attestation must mean <c>Approved</c> and sign this
/// version's content hash (spec §6.2, 21 CFR 11.70). For a forked amendment (K-PROT-2, K-SIM-5), approval also requires
/// a verified <see cref="AttestationMeaning.Reviewed"/> attestation over a saved <see cref="ArtifactKind.SimulationReport"/>
/// artifact bound to both the parent and amended protocol versions. The approval writes the design's elements as lineage
/// parts of the approved version (ADR-2474).
/// </summary>
internal sealed class ApproveProtocolVersionHandler(
    IAttestationVerifier verifier,
    IProtocolFormCompiler? formCompiler = null) : ICommandHandler<ApproveProtocolVersion, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ApproveProtocolVersion command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelErrors.NotFound;
        }

        if (version.Status != ProtocolVersionStatus.InReview)
        {
            return version.Approve(command.AttestationId)!;
        }

        if (await ProtocolVersionStates.VerifyAsync(verifier, context, command.AttestationId, AttestationMeaning.Approved, version, cancellationToken)
                .ConfigureAwait(false) is { } rejected)
        {
            return rejected;
        }

        if (version.ParentVersionId is { } parentId
            && await VerifySimulationReviewGateAsync(verifier, context.TenantId, context.Work, version, parentId, command.ReviewAttestationId, cancellationToken)
                .ConfigureAwait(false) is { } gateRefusal)
        {
            return gateRefusal;
        }

        version.Approve(command.AttestationId);
        var chain = await ProtocolElementLineage.ChainAsync(context.Work, version, cancellationToken).ConfigureAwait(false);
        var plan = ProtocolElementLineage.Plan(version.StudyId, chain);
        foreach (var part in plan.Parts)
        {
            context.Work.IncludePart(part.Node, part.NewVersion);
        }

        foreach (var edge in plan.Edges)
        {
            context.Work.LinkPart(edge.From, edge.To, edge.Kind);
        }

        context.Work.Raise(new ProtocolVersionApproved(
            version.TenantId, version.StudyId, version.Id, version.Number, version.ContentHash!, command.AttestationId));

        if (formCompiler is not null && version.Design() is { } design)
        {
            var versionTag = version.Number.ToString(CultureInfo.InvariantCulture);
            if (formCompiler.Compile(design, version.StudyId.Value.ToString(), versionTag) is { } compiled)
            {
                var snapshot = new FormVersionSnapshot(
                    FormVersionSnapshotId.New(context.RecordedAt),
                    version.TenantId,
                    version.StudyId,
                    version.Id,
                    versionTag,
                    compiled.FormId,
                    compiled.FormHash,
                    compiled.OpenRosaXml,
                    compiled.DdfIrJson,
                    compiled.OdmXml,
                    context.RecordedAt);

                context.Work.Add(snapshot);
                var elements = ProtocolElementLineage.Elements(version.StudyId, chain);
                var snapshotTarget = NodeRef.For(snapshot.Id);
                foreach (var spec in design.CollectionSpecs)
                {
                    if (elements.TryGetValue(spec.Id, out var specElement))
                    {
                        context.Work.LinkInput(specElement, LineageEdgeKind.DerivedFrom, target: snapshotTarget);
                    }
                }

                context.Work.Raise(new FormVersionPublished(
                    version.TenantId,
                    version.StudyId,
                    version.Id,
                    versionTag,
                    compiled.FormId,
                    compiled.FormHash,
                    compiled.OpenRosaXml,
                    compiled.DdfIrJson));
            }
        }

        return ProtocolVersionStates.Of(version);
    }

    public static Task<KernelError?> CheckSimulationReviewGateAsync(
        TenantId tenantId,
        IKernelUnitOfWorkFactory unitOfWorkFactory,
        IAttestationVerifier verifier,
        Actor actor,
        DateTimeOffset recordedAt,
        ProtocolVersionId versionId,
        AttestationId? explicitReviewAttestationId,
        CancellationToken cancellationToken) =>
        VerifyGateCoreAsync(tenantId, unitOfWorkFactory, verifier, actor, recordedAt, versionId, explicitReviewAttestationId, cancellationToken);

    public static async Task<ProtocolApprovalReadinessEvaluation?> EvaluateApprovalReadinessAsync(
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

    private static async Task<KernelError?> VerifyGateCoreAsync(
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

    private static async Task<KernelError?> VerifySimulationReviewGateAsync(
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

/// <summary>
/// Handles <see cref="WithdrawProtocolVersion"/>: the named attestation must mean <c>Withdrawn</c> and sign this
/// version's content hash; the reason for change is the withdrawal reason.
/// </summary>
internal sealed class WithdrawProtocolVersionHandler(IAttestationVerifier verifier) : ICommandHandler<WithdrawProtocolVersion, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        WithdrawProtocolVersion command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.FindAsync<ProtocolVersion>(command.ProtocolVersionId, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelErrors.NotFound;
        }

        if (version.Status is not (ProtocolVersionStatus.InReview or ProtocolVersionStatus.Approved))
        {
            return version.Withdraw(command.AttestationId, command.ReasonForChange)!;
        }

        if (await ProtocolVersionStates.VerifyAsync(verifier, context, command.AttestationId, AttestationMeaning.Withdrawn, version, cancellationToken)
                .ConfigureAwait(false) is { } rejected)
        {
            return rejected;
        }

        if (version.Withdraw(command.AttestationId, command.ReasonForChange) is { } error)
        {
            return error;
        }

        context.Work.Raise(new ProtocolVersionWithdrawn(
            version.TenantId, version.StudyId, version.Id, command.AttestationId, command.ReasonForChange));
        return ProtocolVersionStates.Of(version);
    }
}

/// <summary>Shared steps of the protocol handlers.</summary>
internal static class ProtocolVersionStates
{
    public static CommandResult<ProtocolVersionState> Of(ProtocolVersion version) =>
        CommandResult<ProtocolVersionState>.Success(
            new ProtocolVersionState(version.Id, version.StudyId, version.Number, version.Status, version.ContentHash));

    /// <summary>Loads the version for change, applies <paramref name="change"/> and returns the new state.</summary>
    public static async Task<CommandResult<ProtocolVersionState>> ChangeAsync(
        ICommandContext context,
        ProtocolVersionId id,
        CancellationToken cancellationToken,
        Func<ProtocolVersion, KernelError?> change)
    {
        if (await context.Work.FindAsync<ProtocolVersion>(id, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelErrors.NotFound;
        }

        return change(version) is { } error ? CommandResult<ProtocolVersionState>.Failure(error) : Of(version);
    }

    /// <summary>
    /// Checks that the acting person signed the attestation over the version's frozen content; returns the rejection,
    /// or null.
    /// </summary>
    public static async Task<KernelError?> VerifyAsync(
        IAttestationVerifier verifier,
        ICommandContext context,
        AttestationId attestation,
        AttestationMeaning meaning,
        ProtocolVersion version,
        CancellationToken cancellationToken)
    {
        var verification = await verifier
            .VerifyAsync(new AttestationRequirement(context.TenantId, attestation, meaning, version.SignableTarget!, context.Actor), cancellationToken)
            .ConfigureAwait(false);
        return verification.Satisfied
            ? null
            : ProtocolErrors.AttestationRejected.WithMessage(verification.Reason ?? ProtocolErrors.AttestationRejected.Message);
    }
}

/// <summary>Validates structural inputs on <see cref="ImportProtocolDraft"/> before authorization and persistence.</summary>
internal sealed class ImportProtocolDraftValidator : AbstractValidator<ImportProtocolDraft>
{
    public ImportProtocolDraftValidator()
    {
        RuleFor(x => x.StudyId)
            .Must(id => !id.IsEmpty)
            .WithMessage("StudyId is required.");

        RuleFor(x => x.TargetVersionId)
            .Must(id => !id.IsEmpty)
            .WithMessage("TargetVersionId is required.");

        RuleFor(x => x.Mode)
            .IsInEnum()
            .WithMessage("Mode must be a defined ProtocolDraftImportMode.");

        RuleFor(x => x.Content)
            .NotEmpty()
            .WithMessage("Content is required.");

        RuleFor(x => x.ParentVersionId)
            .Must(id => id is { IsEmpty: false })
            .When(x => x.Mode == ProtocolDraftImportMode.ForkApproved)
            .WithMessage("ParentVersionId is required when forking an approved protocol version.");

        RuleFor(x => x.Amendment)
            .NotNull()
            .When(x => x.Mode == ProtocolDraftImportMode.ForkApproved)
            .WithMessage("Amendment is required when forking an approved protocol version.");

        RuleFor(x => x.StagedConcepts)
            .NotNull()
            .WithMessage("StagedConcepts must not be null.");

        RuleForEach(x => x.StagedConcepts).ChildRules(concept =>
        {
            concept.RuleFor(c => c.ConceptId)
                .Must(id => !id.IsEmpty)
                .WithMessage("ConceptId is required.");
            concept.RuleFor(c => c.PreferredLabel)
                .NotEmpty()
                .MaximumLength(500)
                .WithMessage("PreferredLabel is required and at most 500 characters.");
            concept.RuleFor(c => c.Definition)
                .NotEmpty()
                .WithMessage("Definition is required.");
        });
    }
}

/// <summary>
/// Handles <see cref="ImportProtocolDraft"/>: acquires the study scope lock, validates protocol version lifecycle
/// rules, and atomically commits staged <see cref="Concept"/> aggregates alongside the <see cref="ProtocolVersion"/>
/// draft creation, revision, reopening, or fork.
/// </summary>
internal sealed class ImportProtocolDraftHandler : ICommandHandler<ImportProtocolDraft, ProtocolVersionState>
{
    public async Task<CommandResult<ProtocolVersionState>> HandleAsync(
        ImportProtocolDraft command, ICommandContext context, CancellationToken cancellationToken)
    {
        var studyId = command.StudyId;
        if (!await context.Work.ExistsAsync<Study>(studyId, cancellationToken).ConfigureAwait(false))
        {
            return ProtocolErrors.StudyUnknown;
        }

        await context.Work.LockScopeAsync(NodeRef.For(studyId), cancellationToken).ConfigureAwait(false);

        var preparedConcepts = new List<Concept>();
        if (command.StagedConcepts.Count > 0)
        {
            var requestedCodes = command.StagedConcepts
                .Where(c => c.Origin == ConceptOrigin.CdiscBiomedicalConcept && !string.IsNullOrWhiteSpace(c.ExternalCode))
                .Select(c => c.ExternalCode!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var existingByCode = requestedCodes.Count == 0
                ? []
                : await context.Work.ReadAllAsync<Concept>(
                    c => c.Origin == ConceptOrigin.CdiscBiomedicalConcept && c.ExternalCode != null && requestedCodes.Contains(c.ExternalCode),
                    cancellationToken).ConfigureAwait(false);
            var existingCodes = existingByCode
                .Where(c => c.ExternalCode is not null)
                .Select(c => c.ExternalCode!)
                .ToHashSet(StringComparer.Ordinal);
            var seenIds = new HashSet<ConceptId>();
            var seenCodes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var staged in command.StagedConcepts.OrderBy(c => c.ConceptId.Value))
            {
                if (!seenIds.Add(staged.ConceptId))
                {
                    continue;
                }

                if (staged.Origin == ConceptOrigin.CdiscBiomedicalConcept && !string.IsNullOrWhiteSpace(staged.ExternalCode))
                {
                    var normalizedCode = staged.ExternalCode.Trim();
                    if (!seenCodes.Add(normalizedCode) || existingCodes.Contains(normalizedCode))
                    {
                        continue;
                    }
                }

                if (await context.Work.ExistsAsync<Concept>(staged.ConceptId, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var defined = Concept.Define(
                    staged.ConceptId,
                    command.TenantId,
                    staged.Origin,
                    staged.ExternalCode,
                    staged.Kind,
                    staged.PreferredLabel,
                    staged.Definition,
                    context.RecordedAt);
                if (!defined.IsSuccess)
                {
                    return defined.Error!;
                }

                preparedConcepts.Add(defined.Value!);
            }
        }

        ProtocolVersion targetVersion;
        switch (command.Mode)
        {
            case ProtocolDraftImportMode.CreateDraft:
                {
                    if (await context.Work.ExistsAsync<ProtocolVersion>(command.TargetVersionId, cancellationToken).ConfigureAwait(false))
                    {
                        return KernelErrors.AlreadyExists;
                    }

                    var existing = await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false);
                    CommandResult<ProtocolVersion> created;
                    if (existing.Count == 0)
                    {
                        created = ProtocolVersion.CreateFirst(command.TargetVersionId, command.TenantId, studyId, command.Content);
                    }
                    else if (existing.All(v => v.Status is ProtocolVersionStatus.Withdrawn or ProtocolVersionStatus.Superseded))
                    {
                        var nextNumber = existing.Max(v => v.Number) + 1;
                        created = ProtocolVersion.CreateRecoveryDraft(command.TargetVersionId, command.TenantId, studyId, nextNumber, command.Content);
                    }
                    else
                    {
                        return ProtocolErrors.FirstVersionExists;
                    }

                    if (created.Value is not { } draft)
                    {
                        return created.Error!;
                    }

                    if (command.Amendment is not null)
                    {
                        return ProtocolErrors.AmendmentNotAllowed;
                    }

                    context.Work.Add(draft);
                    context.Work.Raise(new ProtocolVersionDrafted(draft.TenantId, draft.StudyId, draft.Id, draft.Number, null, null));
                    targetVersion = draft;
                    break;
                }

            case ProtocolDraftImportMode.UpdateExisting:
            case ProtocolDraftImportMode.ReopenInReview:
                {
                    if (await context.Work.FindAsync<ProtocolVersion>(command.TargetVersionId, cancellationToken).ConfigureAwait(false) is not { } existing
                        || existing.StudyId != studyId)
                    {
                        return KernelErrors.NotFound;
                    }

                    if (command.Mode == ProtocolDraftImportMode.ReopenInReview && existing.Status != ProtocolVersionStatus.InReview)
                    {
                        return existing.ReturnToDraft()!;
                    }

                    var reopened = false;
                    if (existing.Status == ProtocolVersionStatus.InReview)
                    {
                        if (existing.ReturnToDraft() is { } reopenError)
                        {
                            return reopenError;
                        }

                        reopened = true;
                    }

                    if (existing.Revise(command.Content, command.Amendment) is { } reviseError)
                    {
                        return reviseError;
                    }

                    if (reopened)
                    {
                        context.Work.Raise(new ProtocolVersionReturnedToDraft(existing.TenantId, existing.StudyId, existing.Id));
                    }

                    context.Work.Raise(new ProtocolVersionRevised(existing.TenantId, existing.StudyId, existing.Id));
                    targetVersion = existing;
                    break;
                }

            case ProtocolDraftImportMode.ForkApproved:
                {
                    if (await context.Work.ExistsAsync<ProtocolVersion>(command.TargetVersionId, cancellationToken).ConfigureAwait(false))
                    {
                        return KernelErrors.AlreadyExists;
                    }

                    if (command.ParentVersionId is not { IsEmpty: false } parentId
                        || await context.Work.ReadAsync<ProtocolVersion>(parentId, cancellationToken).ConfigureAwait(false) is not { } parent
                        || parent.StudyId != studyId)
                    {
                        return KernelErrors.NotFound.WithMessage("The parent protocol version does not exist.");
                    }

                    var siblings = await context.Work.ReadAllAsync<ProtocolVersion>(v => v.StudyId == studyId, cancellationToken).ConfigureAwait(false);
                    var number = siblings.Max(v => v.Number) + 1;
                    var forked = ProtocolVersion.Fork(parent, command.TargetVersionId, number, command.Amendment!);
                    if (forked.Value is not { } draft)
                    {
                        return forked.Error!;
                    }

                    if (draft.Revise(command.Content, command.Amendment) is { } reviseError)
                    {
                        return reviseError;
                    }

                    context.Work.Add(draft);
                    context.Work.LinkInput(NodeRef.For(parent.Id, parent.VersionIndex), LineageEdgeKind.DerivedFrom);
                    context.Work.Raise(new ProtocolVersionDrafted(
                        draft.TenantId, draft.StudyId, draft.Id, draft.Number, draft.ParentVersionId, draft.Amendment));
                    context.Work.Raise(new ProtocolVersionRevised(draft.TenantId, draft.StudyId, draft.Id));
                    targetVersion = draft;
                    break;
                }

            default:
                return ProtocolErrors.InvalidTransition;
        }

        foreach (var concept in preparedConcepts)
        {
            context.Work.Add(concept);
            context.Work.Raise(new ConceptDefined(
                command.TenantId,
                concept.Current,
                concept.Origin,
                concept.ExternalCode,
                concept.Kind,
                concept.PreferredLabel));
        }

        return ProtocolVersionStates.Of(targetVersion);
    }
}

/// <summary>Registers the protocol version command handlers (spec §6.2).</summary>
public static class ProtocolServiceCollectionExtensions
{
    /// <summary>Called by <see cref="KernelApplicationServiceCollectionExtensions.AddKernelCommandPipeline"/>.</summary>
    public static IServiceCollection AddKernelProtocolHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<CreateProtocolDraft, ProtocolVersionState>, CreateProtocolDraftHandler>();
        services.AddTransient<ICommandHandler<ReviseProtocolDraft, ProtocolVersionState>, ReviseProtocolDraftHandler>();
        services.AddTransient<ICommandHandler<SubmitProtocolVersionForReview, ProtocolVersionState>, SubmitProtocolVersionForReviewHandler>();
        services.AddTransient<ICommandHandler<ReturnProtocolVersionToDraft, ProtocolVersionState>, ReturnProtocolVersionToDraftHandler>();
        services.AddTransient<ICommandHandler<ApproveProtocolVersion, ProtocolVersionState>, ApproveProtocolVersionHandler>();
        services.AddTransient<ICommandHandler<WithdrawProtocolVersion, ProtocolVersionState>, WithdrawProtocolVersionHandler>();
        services.AddTransient<ICommandHandler<ForkProtocolVersion, ProtocolVersionState>, ForkProtocolVersionHandler>();
        services.AddTransient<ICommandHandler<ImportProtocolDraft, ProtocolVersionState>, ImportProtocolDraftHandler>();
        services.AddCommandValidator<ImportProtocolDraft, ImportProtocolDraftValidator>();
        return services;
    }
}
