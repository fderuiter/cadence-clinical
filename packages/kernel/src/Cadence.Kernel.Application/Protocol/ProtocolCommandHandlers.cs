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
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
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
/// Handles <see cref="SubmitProtocolVersionForReview"/>: checks external references, amendment impacted elements, and
/// registered <see cref="IProtocolSubmissionCheck"/>s before freezing the draft.
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
/// Handles <see cref="ApproveProtocolVersion"/>: verifies the <c>Approved</c> attestation, checks the simulation
/// review gate for forked amendments, records element lineage, and compiles published form snapshots.
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
            && await ProtocolApprovalSimulationGate.VerifySimulationReviewGateAsync(
                    verifier,
                    context.TenantId,
                    context.Work,
                    version,
                    parentId,
                    command.ReviewAttestationId,
                    cancellationToken)
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
}

/// <summary>Handles <see cref="WithdrawProtocolVersion"/>.</summary>
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
        services.AddTransient<ICommandHandler<ImportProtocolDraft, ProtocolVersionState>, ImportProtocolDraftHandler>();
        services.AddCommandValidator<ImportProtocolDraft, ImportProtocolDraftValidator>();
        return services;
    }
}
