using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;

namespace Cadence.Kernel.Api;

/// <summary>
/// A kernel object as an inspector, breadcrumb or palette row shows it (ADR-2481). An object the reader may not see
/// comes back masked (ADR-2471): only its kind and <see cref="Mask"/>, nothing that identifies it.
/// </summary>
/// <param name="Ref">The object's NodeRef, pinned to the version read (<c>kind:id@version</c>), or null when masked.</param>
/// <param name="Kind">The object's kind, for example <c>study</c>, shown even when masked.</param>
/// <param name="Title">Display title, or null when masked.</param>
/// <param name="Code">Human-facing code, when the kind has one and the object is not masked.</param>
/// <param name="Status">Lifecycle state as the kernel records it, when the kind has one and the object is not masked.</param>
/// <param name="Mask">Why the object is masked (<c>NotPermitted</c>, <c>Blinded</c>, <c>IdentityVault</c>), or null when visible.</param>
public sealed record KernelNodeResponse(string? Ref, string Kind, string? Title, string? Code, string? Status, string? Mask)
{
    internal static KernelNodeResponse From(NodeSummary node, MaskReason? mask) =>
        mask is { } reason
            ? new(null, node.Ref.Kind, null, null, null, reason.ToString())
            : new(node.Ref.ToString(), node.Ref.Kind, node.Title, node.Code, node.Status, null);
}

/// <summary>
/// One level of a zoom path, from the tenant down to the object. A masked level keeps its place and kind; a grouping
/// level is masked with the object below it.
/// </summary>
/// <param name="Level">The level's kind, or a grouping level such as <c>country</c>.</param>
/// <param name="Label">What the breadcrumb shows, or null when the object at this level is masked.</param>
/// <param name="Ref">The object at this level, or null for a grouping level or a masked object.</param>
/// <param name="Mask">Why the object at this level is masked, or null.</param>
public sealed record KernelZoomStepResponse(string Level, string? Label, string? Ref, string? Mask)
{
    internal static KernelZoomStepResponse From(ZoomStep step, MaskReason? mask) =>
        mask is { } reason
            ? new(step.Level, null, null, reason.ToString())
            : new(step.Level, step.Label, step.Ref?.ToString(), null);
}

/// <summary>A child of a scope node. A masked child keeps its place; its child count and group are withheld.</summary>
/// <param name="Node">The child.</param>
/// <param name="ChildCount">How many children the child has, or null when that level is not queryable yet or the child is masked.</param>
/// <param name="Group">The grouping level the child sits under (its country), or null when there is none or the child is masked.</param>
public sealed record KernelChildResponse(KernelNodeResponse Node, int? ChildCount, string? Group)
{
    internal static KernelChildResponse From(ScopeChild child, MaskReason? mask) =>
        mask is null
            ? new(KernelNodeResponse.From(child.Node, null), child.ChildCount, child.Group)
            : new(KernelNodeResponse.From(child.Node, mask), null, null);
}

/// <summary>A condition a rule evaluated, whether it held, and the objects it was decided on (K-AUTH-6).</summary>
/// <param name="Code">Stable machine code, for example <c>qualification.current</c>.</param>
/// <param name="Description">What the condition requires.</param>
/// <param name="Met">Whether it held.</param>
/// <param name="Evidence">NodeRefs of the objects the decision rests on.</param>
public sealed record KernelConditionResponse(string Code, string Description, bool Met, IReadOnlyList<string> Evidence)
{
    internal static KernelConditionResponse From(EvaluatedCondition condition) =>
        new(condition.Code, condition.Description, condition.Met, Refs(condition.Evidence));

    internal static IReadOnlyList<string> Refs(IEnumerable<NodeRef> refs) => [.. refs.Select(r => r.ToString())];
}

/// <summary>One claim in an explanation's chain, with the objects that support it.</summary>
/// <param name="Claim">What the step establishes.</param>
/// <param name="Evidence">NodeRefs the reader can open to check it.</param>
public sealed record KernelExplanationStepResponse(string Claim, IReadOnlyList<string> Evidence);

/// <summary>Something the reader can do to change an outcome.</summary>
/// <param name="Description">The action in words.</param>
/// <param name="Capability">The capability it needs, when it is a kernel command.</param>
/// <param name="Target">The NodeRef it applies to, when there is one.</param>
public sealed record KernelNextActionResponse(string Description, string? Capability, string? Target);

/// <summary>Why something holds (ADR-2482). The kernel produces it; clients render it and never compose one (K-EXP-1).</summary>
/// <param name="Subject">The NodeRef explained, or null.</param>
/// <param name="Claim">The conclusion, as one sentence.</param>
/// <param name="Producer">The component that produced it, for example <c>kernel.authorization</c>.</param>
/// <param name="Steps">The chain of reasoning.</param>
/// <param name="Conditions">Every condition evaluated, met and unmet.</param>
/// <param name="NextActions">What would change the outcome.</param>
public sealed record KernelExplanationResponse(
    string? Subject,
    string Claim,
    string Producer,
    IReadOnlyList<KernelExplanationStepResponse> Steps,
    IReadOnlyList<KernelConditionResponse> Conditions,
    IReadOnlyList<KernelNextActionResponse> NextActions)
{
    internal static KernelExplanationResponse From(Explanation explanation) =>
        new(
            explanation.Subject?.ToString(),
            explanation.Claim,
            explanation.Producer,
            [.. explanation.Steps.Select(s => new KernelExplanationStepResponse(s.Claim, KernelConditionResponse.Refs(s.Evidence)))],
            [.. explanation.Conditions.Select(KernelConditionResponse.From)],
            [.. explanation.NextActions.Select(a => new KernelNextActionResponse(a.Description, a.Capability, a.Target?.ToString()))]);
}

/// <summary>
/// A dry-run authorization decision for the calling actor (ADR-2483): the same decision sending the command would get
/// now, with its explanation.
/// </summary>
/// <param name="Capability">The capability asked about.</param>
/// <param name="VerbClass">Its verb class: <c>Read</c>, <c>Propose</c>, <c>Execute</c> or <c>Approve</c>.</param>
/// <param name="Resource">The NodeRef it was asked on.</param>
/// <param name="Permitted">Whether the actor may exercise it now.</param>
/// <param name="Reason">Why not, or why, in one sentence.</param>
/// <param name="Explanation">The decision explained.</param>
public sealed record KernelDecisionResponse(
    string Capability,
    string VerbClass,
    string Resource,
    bool Permitted,
    string? Reason,
    KernelExplanationResponse Explanation)
{
    internal static KernelDecisionResponse From(Capability capability, NodeRef resource, AuthorizationDecision decision, Explanation explanation) =>
        new(capability.Token, capability.VerbClass.ToString(), resource.ToString(), decision.Permitted, decision.Reason,
            KernelExplanationResponse.From(explanation));
}

/// <summary>A kernel command offered on an object, with the calling actor's dry-run decision (ADR-2483).</summary>
/// <param name="Command">The command, for example <c>ReviseConcept</c>.</param>
/// <param name="Decision">Whether the actor may send it now, and why.</param>
public sealed record KernelCommandResponse(string Command, KernelDecisionResponse Decision)
{
    internal static KernelCommandResponse From(CommandAvailability command) =>
        new(command.Command, KernelDecisionResponse.From(command.Capability, command.Resource, command.Decision, command.Explanation));
}

/// <summary>One search result (ADR-2484).</summary>
/// <param name="Ref">The object found, pinned to the version read.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Title">Its title, name or display text.</param>
/// <param name="Code">Its human code, when it has one.</param>
/// <param name="Detail">Context that tells similar results apart.</param>
/// <param name="ExactCode">True when the code equals the search text.</param>
/// <param name="Rank">Text relevance; higher is better.</param>
public sealed record KernelSearchHitResponse(string Ref, string Kind, string Title, string? Code, string? Detail, bool ExactCode, double Rank)
{
    internal static KernelSearchHitResponse From(SearchHit hit) =>
        new(hit.Ref.ToString(), hit.Ref.Kind, hit.Title, hit.Code, hit.Detail, hit.ExactCode, hit.Rank);
}

/// <summary>
/// A protocol version without its content (spec §6.2). A version the reader may not see keeps its place, with only
/// <see cref="Mask"/> set.
/// </summary>
/// <param name="Ref">The version's NodeRef, or null when masked.</param>
/// <param name="Study">The study's NodeRef, or null when masked.</param>
/// <param name="Number">Its number in the study, from 1, or null when masked.</param>
/// <param name="ParentVersion">The version it was forked from, or null for the first version or when masked.</param>
/// <param name="Status">Its lifecycle status (<c>Draft</c>, <c>InReview</c>, <c>Approved</c>, <c>Superseded</c>, <c>Withdrawn</c>), or null when masked.</param>
/// <param name="ContentHash">The content hash, set once the version is submitted for review.</param>
/// <param name="AmendmentIntent">What the amendment sets out to change, or null for the first version.</param>
/// <param name="ApprovalAttestation">The NodeRef of the attestation that approved it, once approved.</param>
/// <param name="WithdrawalReason">Why it was withdrawn, once withdrawn.</param>
/// <param name="Mask">Why the version is masked, or null when visible.</param>
public sealed record KernelProtocolVersionResponse(
    string? Ref,
    string? Study,
    int? Number,
    string? ParentVersion,
    string? Status,
    string? ContentHash,
    string? AmendmentIntent,
    string? ApprovalAttestation,
    string? WithdrawalReason,
    string? Mask)
{
    internal static KernelProtocolVersionResponse From(ProtocolVersionSummary version, MaskReason? mask) =>
        mask is { } reason
            ? new(null, null, null, null, null, null, null, null, null, reason.ToString())
            : new(
                NodeRef.For(version.Id).ToString(),
                NodeRef.For(version.StudyId).ToString(),
                version.Number,
                RefOf(version.ParentVersionId),
                version.Status.ToString(),
                version.ContentHash,
                version.AmendmentIntent,
                RefOf(version.ApprovalAttestationId),
                version.WithdrawalReason,
                null);

    private static string? RefOf<TId>(TId? id)
        where TId : struct, IEntityId<TId> => id is { } value ? NodeRef.For(value).ToString() : null;
}
