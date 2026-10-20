using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Intelligence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// Proposal reads and rejection for the Canvas review screen and Needs Attention (spec §11, ADR-2489, ADR-2635). A
/// proposal is seen by whoever may see it in its study (ADR-2457); each affected object the reader may not see is masked
/// in place (ADR-2471), so a reviewer always sees how many objects a proposal names. Rejection is a kernel command sent
/// through <see cref="IKernelCommandSender"/> with the signed reason for change; approval needs an electronic signature
/// and is not served here yet (#5552).
/// </summary>
public static class KernelProposalEndpoints
{
    /// <summary>The most proposals one list returns.</summary>
    public const int MaxLimit = 200;

    /// <summary>Maps <c>/studies/{studyRef}/proposals</c> and <c>/proposals</c> on the kernel group.</summary>
    public static RouteGroupBuilder MapKernelProposals(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var group = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>().WithTags("Kernel proposals");

        group.MapGet("/studies/{studyRef}/proposals", ListAsync)
            .WithName("KernelListProposals")
            .WithSummary("A study's proposals, newest first, optionally only those with one status (Open, Approved, Rejected, Withdrawn, Expired).")
            .WithDescription("A study the caller may not see is refused; a proposal the caller may not see is masked in place (ADR-2471).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/proposals/{proposalRef}", GetAsync)
            .WithName("KernelGetProposal")
            .WithSummary("One proposal with its intent, plan, affected objects, requested capabilities, model, content hash and decision.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/proposals/{proposalRef}/rejection", RejectAsync)
            .WithName("KernelRejectProposal")
            .WithSummary("Rejects an open proposal; the signed reason for change is the rejection reason (proposal.reject).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesKernelCommand<RejectProposal>();

        return kernel;
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelProposalResponse>>, ProblemHttpResult>> ListAsync(
        string studyRef,
        string? status,
        int? limit,
        HttpContext context,
        IKernelNavigator navigator,
        IProposalReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(studyRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(studyRef);
        }

        if (node.Kind != NodeKinds.Study)
        {
            return KernelProblems.WrongKind(node, NodeKinds.Study);
        }

        ProposalStatus? wanted = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<ProposalStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return KernelProblems.BadRequest("Invalid status", $"'{status}' is not a proposal status: Open, Approved, Rejected, Withdrawn or Expired.");
            }

            wanted = parsed;
        }

        if (limit is < 1 or > MaxLimit)
        {
            return KernelProblems.BadRequest("Invalid limit", $"The limit is 1 to {MaxLimit}.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } study)
        {
            return KernelProblems.NotFound(node);
        }

        // Listing a hidden study's proposals would describe the study, so the list is refused, not masked.
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [study.Ref], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(study.Ref) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You can't see this study ({studyMask}), so its proposals are not listed.");
        }

        var proposals = await reader
            .ListForStudyAsync(tenant, StudyId.FromGuid(node.Id), wanted, clock.UtcNow, limit ?? 50, cancellationToken)
            .ConfigureAwait(false);
        var masks = await MaskAsync(visibility, actor, tenant, proposals, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelProposalResponse>>([.. proposals.Select(p => KernelProposalResponse.From(p, masks))]);
    }

    internal static async Task<Results<Ok<KernelProposalResponse>, ProblemHttpResult>> GetAsync(
        string proposalRef,
        HttpContext context,
        IProposalReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (ParseProposal(proposalRef) is not { } id)
        {
            return ProposalRefInvalid(proposalRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await reader.GetAsync(tenant, id, clock.UtcNow, cancellationToken).ConfigureAwait(false) is not { } proposal)
        {
            return KernelProblems.NotFound(NodeRef.For(id));
        }

        var masks = await MaskAsync(visibility, actor, tenant, [proposal], cancellationToken).ConfigureAwait(false);
        return masks.For(NodeRef.For(proposal.Id)) is { } mask
            ? KernelProblems.NotVisible($"You may not see {NodeRef.For(proposal.Id)} ({mask}).")
            : TypedResults.Ok(KernelProposalResponse.From(proposal, masks));
    }

    internal static async Task<Results<Ok<KernelProposalStateResponse>, ProblemHttpResult>> RejectAsync(
        string proposalRef,
        HttpContext context,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken)
    {
        if (ParseProposal(proposalRef) is not { } id)
        {
            return ProposalRefInvalid(proposalRef);
        }

        var result = await sender.SendAsync(
            new RejectProposal(
                KernelRequestTenant.Get(context),
                id,
                context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString().Trim(),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Value is { } state
            ? TypedResults.Ok(KernelProposalStateResponse.From(state))
            : KernelErrorEndpoints.Problem(result.Error!);
    }

    /// <summary>The masks for each proposal and every object it names, read in one visibility call.</summary>
    private static async ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
        INodeVisibility visibility, ICurrentActor actor, TenantId tenant, IReadOnlyList<ProposalSummary> proposals, CancellationToken cancellationToken)
    {
        var nodes = proposals.SelectMany(p => (IEnumerable<NodeRef>)[NodeRef.For(p.Id), NodeRef.For(p.Plan.VersionId), .. p.Affected.Select(NodeRef.Parse)]);
        return await KernelReadMasking.MaskAsync(visibility, actor, tenant, nodes, cancellationToken).ConfigureAwait(false);
    }

    private static ProposalId? ParseProposal(string text) =>
        NodeRef.TryParse(text, out var node) && node.Kind == NodeKinds.Proposal ? ProposalId.FromGuid(node.Id) : null;

    private static ProblemHttpResult ProposalRefInvalid(string text) =>
        KernelProblems.BadRequest("Invalid NodeRef", $"'{text}' is not a proposal NodeRef (proposal:<uuid>).");
}

/// <summary>
/// A proposal as a reviewer reads it. When the reader may not see the proposal every field but <see cref="Mask"/> is
/// null; an affected object or plan the reader may not see keeps its place with its mask (ADR-2471).
/// </summary>
/// <param name="Ref">The proposal, as a NodeRef.</param>
/// <param name="Study">Its study, as a NodeRef.</param>
/// <param name="Intent">What it would achieve.</param>
/// <param name="Plan">The artifact version holding its plan and reasoning.</param>
/// <param name="Affected">The objects it would change or depends on, in the order proposed.</param>
/// <param name="SimulatedImpact">The result hash of a simulation over it, or null.</param>
/// <param name="RequestedCapabilities">The capabilities a person would use to execute it.</param>
/// <param name="ProposedAt">When it was proposed.</param>
/// <param name="ExpiresAt">When it stops being approvable.</param>
/// <param name="ProposedBy">The proposing actor's id.</param>
/// <param name="ProposedByKind">The proposing actor's kind: <c>Person</c>, <c>AutomationAgent</c> or <c>Service</c>.</param>
/// <param name="RequestedBy">The person an automation agent proposed for (K-AI-6), or null.</param>
/// <param name="ModelId">The model that produced it, for an automation agent (K-PROV-3).</param>
/// <param name="ConfidenceScore">The confidence score the proposer gave, or null.</param>
/// <param name="ContentHash">The hash an approval signs.</param>
/// <param name="Status">Its status now: Open, Approved, Rejected, Withdrawn or Expired.</param>
/// <param name="DecidedAt">When it was decided.</param>
/// <param name="DecidedBy">Who decided it.</param>
/// <param name="ApprovalAttestation">The attestation an approval was signed with, as a NodeRef.</param>
/// <param name="DecisionReason">Why it was rejected or withdrawn.</param>
/// <param name="IsEscalated">True when it was escalated for review.</param>
/// <param name="EscalatedTo">Whom it was escalated to.</param>
/// <param name="EscalationReason">Why it was escalated.</param>
/// <param name="Mask">Why the reader may not see it, or null.</param>
public sealed record KernelProposalResponse(
    string? Ref,
    string? Study,
    string? Intent,
    KernelProposalPlanResponse? Plan,
    IReadOnlyList<KernelProposalAffectedResponse>? Affected,
    string? SimulatedImpact,
    IReadOnlyList<string>? RequestedCapabilities,
    DateTimeOffset? ProposedAt,
    DateTimeOffset? ExpiresAt,
    string? ProposedBy,
    string? ProposedByKind,
    string? RequestedBy,
    string? ModelId,
    double? ConfidenceScore,
    string? ContentHash,
    string? Status,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    string? ApprovalAttestation,
    string? DecisionReason,
    bool? IsEscalated,
    string? EscalatedTo,
    string? EscalationReason,
    string? Mask)
{
    internal static KernelProposalResponse From(ProposalSummary proposal, IReadOnlyDictionary<NodeRef, MaskReason> masks)
    {
        if (masks.For(NodeRef.For(proposal.Id)) is { } hidden)
        {
            return new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, hidden.ToString());
        }

        var plan = NodeRef.For(proposal.Plan.VersionId);
        return new(
            NodeRef.For(proposal.Id).ToString(),
            NodeRef.For(proposal.StudyId).ToString(),
            proposal.Intent,
            masks.For(plan) is { } planMask
                ? new KernelProposalPlanResponse(plan.ToString(), null, null, null, null, planMask.ToString())
                : new KernelProposalPlanResponse(
                    plan.ToString(), proposal.Plan.Title, proposal.Plan.Number, proposal.Plan.MediaType, proposal.Plan.ContentHash, null),
            [.. proposal.Affected.Select(a => new KernelProposalAffectedResponse(a, masks.For(NodeRef.Parse(a))?.ToString()))],
            proposal.SimulatedImpact,
            proposal.RequestedCapabilities,
            proposal.ProposedAt,
            proposal.ExpiresAt,
            proposal.ProposedById,
            proposal.ProposedByKind.ToString(),
            proposal.RequestedById,
            proposal.ModelId,
            proposal.ConfidenceScore,
            proposal.ContentHash,
            proposal.Status.ToString(),
            proposal.DecidedAt,
            proposal.DecidedById,
            proposal.ApprovalAttestationId is { } attestation ? NodeRef.For(attestation).ToString() : null,
            proposal.DecisionReason,
            proposal.IsEscalated,
            proposal.EscalatedTo,
            proposal.EscalationReason,
            null);
    }
}

/// <summary>The artifact version holding a proposal's plan; its fields are null and <see cref="Mask"/> set when the reader may not see it.</summary>
/// <param name="Ref">The artifact version, as a NodeRef.</param>
/// <param name="Title">The artifact's title.</param>
/// <param name="Number">The version's number.</param>
/// <param name="MediaType">The media type of its bytes.</param>
/// <param name="ContentHash">The hash of its bytes.</param>
/// <param name="Mask">Why the reader may not see it, or null.</param>
public sealed record KernelProposalPlanResponse(string Ref, string? Title, int? Number, string? MediaType, string? ContentHash, string? Mask);

/// <summary>An object a proposal names, with its mask when the reader may not see it.</summary>
/// <param name="Ref">The object, as a NodeRef.</param>
/// <param name="Mask">Why the reader may not see it, or null.</param>
public sealed record KernelProposalAffectedResponse(string Ref, string? Mask);

/// <summary>A proposal after a command.</summary>
/// <param name="Ref">The proposal, as a NodeRef.</param>
/// <param name="Status">Its status after the command.</param>
/// <param name="ContentHash">The hash an approval signs.</param>
/// <param name="DecidedAt">When it was decided.</param>
/// <param name="DecidedBy">Who decided it.</param>
public sealed record KernelProposalStateResponse(string Ref, string Status, string ContentHash, DateTimeOffset? DecidedAt, string? DecidedBy)
{
    internal static KernelProposalStateResponse From(ProposalState state) =>
        new(NodeRef.For(state.ProposalId).ToString(), state.Status.ToString(), state.ContentHash, state.DecidedAt, state.DecidedBy);
}
