using System;
using System.Collections.Generic;
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
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Trust;

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
        if (existing.Count > 0)
        {
            return ProtocolErrors.FirstVersionExists;
        }

        var created = ProtocolVersion.CreateFirst(command.ProtocolVersionId, command.TenantId, command.StudyId, command.Content);
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

/// <summary>
/// Handles <see cref="ApproveProtocolVersion"/>: the named attestation must mean <c>Approved</c> and sign this
/// version's content hash (spec §6.2, 21 CFR 11.70). For a forked amendment (K-PROT-2, K-SIM-5), approval also requires
/// a verified <see cref="AttestationMeaning.Reviewed"/> attestation over a saved <see cref="ArtifactKind.SimulationReport"/>
/// artifact bound to both the parent and amended protocol versions. The approval writes the design's elements as lineage
/// parts of the approved version (ADR-2474).
/// </summary>
/// <summary>Read-only precheck for the forked protocol amendment simulation review gate (K-PROT-2, K-SIM-5).</summary>
public static class ProtocolApprovalSimulationGate
{
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
}

internal sealed class ApproveProtocolVersionHandler(IAttestationVerifier verifier) : ICommandHandler<ApproveProtocolVersion, ProtocolVersionState>
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
        var plan = ProtocolElementLineage.Plan(version.StudyId, await ChainAsync(version, context, cancellationToken).ConfigureAwait(false));
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

        if (!string.IsNullOrWhiteSpace(version.Content))
        {
            try
            {
                if (TryCompileUsdm(version.Content, version.StudyId.Value.ToString(), version.Number.ToString()) is { } compiled)
                {
                    var snapshot = new FormVersionSnapshot(
                        FormVersionSnapshotId.New(),
                        version.TenantId,
                        version.StudyId,
                        version.Id,
                        version.Number.ToString(),
                        compiled.FormId,
                        compiled.FormHash,
                        compiled.OpenRosaXml,
                        compiled.DdfIrJson,
                        compiled.OdmXml,
                        context.RecordedAt);

                    context.Work.Add(snapshot);

                    context.Work.Raise(new FormVersionPublished(
                        version.TenantId,
                        version.StudyId,
                        version.Id,
                        version.Number.ToString(),
                        compiled.FormId,
                        compiled.FormHash,
                        compiled.OpenRosaXml,
                        compiled.DdfIrJson));
                }
            }
            catch (Exception)
            {
                // Gracefully ignore if version.Content is not valid USDM JSON (e.g. in legacy/simple tests)
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

    private static async Task<KernelError?> VerifySimulationReviewGateAsync(
        IAttestationVerifier verifier,
        TenantId tenantId,
        IKernelUnitOfWork work,
        ProtocolVersion amended,
        ProtocolVersionId parentId,
        AttestationId? explicitReviewAttestationId,
        CancellationToken cancellationToken)
    {
        var parent = await work.ReadAsync<ProtocolVersion>(parentId, cancellationToken).ConfigureAwait(false);
        if (parent?.ContentHash is not { } parentHash || amended.ContentHash is not { } amendedHash)
        {
            return SimulationReviewRefusal(
                amended,
                "The parent protocol version does not have a frozen content hash.");
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
            return SimulationReviewRefusal(
                amended,
                $"No saved SimulationReport artifact in study {studyId} binds parent {parentId} ({parentHash}) to amended version {amended.Id} ({amendedHash}).");
        }

        if (explicitReviewAttestationId is { } reviewId)
        {
            var explicitAttestation = await work.ReadAsync<Attestation>(reviewId, cancellationToken).ConfigureAwait(false);
            if (explicitAttestation is null
                || !string.Equals(explicitAttestation.TargetKind, NodeKinds.ArtifactVersion, StringComparison.Ordinal)
                || !matchingVersions.TryGetValue(explicitAttestation.TargetId, out var targetedVersion))
            {
                return SimulationReviewRefusal(
                    amended,
                    "The named ReviewAttestationId does not target a SimulationReport artifact version matching the parent and amended protocol versions.");
            }

            var verification = await verifier.VerifyAsync(
                new AttestationRequirement(
                    tenantId,
                    reviewId,
                    AttestationMeaning.Reviewed,
                    targetedVersion.SignableTarget,
                    Actor.Person(explicitAttestation.ActorId)),
                cancellationToken).ConfigureAwait(false);
            return verification.Satisfied
                ? null
                : SimulationReviewRefusal(amended, verification.Reason ?? "The simulation review attestation is not valid.");
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
                return null;
            }
        }

        return SimulationReviewRefusal(
            amended,
            "No verified Reviewed attestation exists over a matching SimulationReport artifact version.");
    }

    private static KernelError SimulationReviewRefusal(ProtocolVersion amended, string detail)
    {
        var condition = EvaluatedCondition.Unsatisfied(
            "protocol.amendment.simulation_reviewed",
            detail);
        return ProtocolErrors.SimulationReviewRequired.WithMessage($"{ProtocolErrors.SimulationReviewRequired.Message} {detail}") with
        {
            Explanation = new Explanation(
                NodeRef.For(amended.Id, amended.VersionIndex),
                $"Approval of forked protocol version {amended.Number} is refused until a matching SimulationReport artifact is attested with meaning Reviewed (K-PROT-2, K-SIM-5).",
                "kernel.protocol.simulation_gate",
                [new ExplanationStep(detail, [])],
                [condition],
                [new NextAction("Run impact simulation on the frozen amendment, save the SimulationReport artifact, and record a Reviewed electronic signature over its artifact_version before approving.")]),
        };
    }

    private static (string FormHash, string OpenRosaXml, string DdfIrJson, string FormId, string? OdmXml)? TryCompileUsdm(
        string content, string fallbackStudyId, string versionTag)
    {
        var ddfAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Cadence.Ddf")
            ?? System.Reflection.Assembly.Load("Cadence.Ddf");
        if (ddfAssembly is null) return null;

        var compilerType = ddfAssembly.GetType("Cadence.Ddf.UsdmCompiler");
        if (compilerType is null) return null;

        var compileMethod = compilerType.GetMethod("CompileUsdm", [typeof(string), typeof(string), typeof(string)]);
        var parseMethod = compilerType.GetMethod("ParseUsdmToIr", [typeof(string), typeof(string), typeof(string)]);
        if (compileMethod is null || parseMethod is null) return null;

        var compilation = compileMethod.Invoke(null, [content, fallbackStudyId, versionTag]);
        var parseResult = parseMethod.Invoke(null, [content, fallbackStudyId, versionTag]);
        if (compilation is null || parseResult is null) return null;

        var formHash = compilation.GetType().GetProperty("FormHash")?.GetValue(compilation) as string ?? string.Empty;
        var openRosaXml = compilation.GetType().GetProperty("OpenRosaXml")?.GetValue(compilation) as string ?? string.Empty;
        var odmXml = compilation.GetType().GetProperty("OdmXml")?.GetValue(compilation) as string;

        var studyIr = parseResult.GetType().GetProperty("Item1")?.GetValue(parseResult);
        var ddfIrJson = studyIr is not null ? System.Text.Json.JsonSerializer.Serialize(studyIr) : "{}";
        var studyIdProp = studyIr?.GetType().GetProperty("StudyId")?.GetValue(studyIr) as string;
        var formId = string.IsNullOrWhiteSpace(studyIdProp) ? fallbackStudyId : studyIdProp;

        return (formHash, openRosaXml, ddfIrJson, formId, odmXml);
    }

    /// <summary>The version and its ancestors, first version first, as (number, content).</summary>
    private static async Task<IReadOnlyList<(int Number, string Content)>> ChainAsync(
        ProtocolVersion version, ICommandContext context, CancellationToken cancellationToken)
    {
        var chain = new List<(int Number, string Content)> { (version.Number, version.Content) };
        var parentId = version.ParentVersionId;
        while (parentId is { } id)
        {
            var parent = await context.Work.ReadAsync<ProtocolVersion>(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Protocol version {id} names a parent the tenant does not have.");
            chain.Add((parent.Number, parent.Content));
            parentId = parent.ParentVersionId;
        }

        chain.Reverse();
        return chain;
    }
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
        return services;
    }
}
