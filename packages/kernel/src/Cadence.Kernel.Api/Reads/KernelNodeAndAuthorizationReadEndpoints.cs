using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Validation;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Kernel.Api;

internal static class KernelNodeAndAuthorizationReadEndpoints
{
    internal static async Task<Results<Ok<KernelBatchNodesResponse>, ProblemHttpResult>> GetNodesAsync(
        [FromQuery(Name = "ref")] string[]? refs,
        HttpContext context,
        IKernelNavigator navigator,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (refs is null || refs.Length == 0)
        {
            return TypedResults.Ok(new KernelBatchNodesResponse(new Dictionary<string, KernelNodeResponse>(StringComparer.Ordinal)));
        }

        if (refs.Length > KernelReadEndpoints.MaxBatchNodes)
        {
            return KernelProblems.BadRequest("Too many node references", $"Specify at most {KernelReadEndpoints.MaxBatchNodes} ref parameters per request.");
        }

        var requested = new List<(string Key, NodeRef Parsed)>(refs.Length);
        foreach (var rawRef in refs)
        {
            var trimmed = (rawRef ?? string.Empty).Trim();
            if (!NodeRef.TryParse(trimmed, out var parsed))
            {
                return KernelProblems.InvalidNodeRef(rawRef ?? string.Empty);
            }

            requested.Add((trimmed, parsed));
        }

        var tenant = KernelRequestTenant.Get(context);
        var resolved = await ResolveMaskedNodesAsync(tenant, requested, navigator, visibility, actor, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new KernelBatchNodesResponse(resolved));
    }

    internal static async Task<IReadOnlyDictionary<string, KernelNodeResponse>> ResolveMaskedNodesAsync(
        TenantId tenant,
        IReadOnlyList<(string Key, NodeRef Parsed)> requested,
        IKernelNavigator navigator,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (requested.Count == 0)
        {
            return new Dictionary<string, KernelNodeResponse>(StringComparer.Ordinal);
        }

        var summariesByUnversioned = await navigator
            .ResolveManyAsync(tenant, [.. requested.Select(r => r.Parsed)], cancellationToken)
            .ConfigureAwait(false);

        var matched = new List<(string Key, NodeSummary Summary)>(requested.Count);
        foreach (var (key, parsed) in requested)
        {
            if (!summariesByUnversioned.TryGetValue(parsed.Unversioned(), out var summary))
            {
                continue;
            }

            if (parsed.Kind == NodeKinds.Concept && parsed.Version is { } requestedVersion && summary.Ref.Version != requestedVersion)
            {
                summary = await navigator.ResolveAsync(tenant, parsed, cancellationToken).ConfigureAwait(false) ?? summary;
            }

            matched.Add((key, summary));
        }

        if (matched.Count == 0)
        {
            return new Dictionary<string, KernelNodeResponse>(StringComparer.Ordinal);
        }

        var maskTargets = matched
            .SelectMany(m => (IEnumerable<NodeRef>)[m.Summary.Ref, m.Summary.Ref.Unversioned()])
            .Distinct()
            .ToList();
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, maskTargets, cancellationToken).ConfigureAwait(false);

        var responses = new Dictionary<string, KernelNodeResponse>(matched.Count, StringComparer.Ordinal);
        foreach (var (key, summary) in matched)
        {
            var mask = masks.For(summary.Ref) ?? masks.For(summary.Ref.Unversioned());
            responses[key] = KernelNodeResponse.From(summary, mask);
        }

        return responses;
    }

    internal static async Task<Results<Ok<KernelNodeResponse>, ProblemHttpResult>> GetNodeAsync(
        string nodeRef, HttpContext context, IKernelNavigator navigator, INodeVisibility visibility, ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(nodeRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } summary)
        {
            return KernelProblems.NotFound(node);
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [summary.Ref], cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelNodeResponse.From(summary, masks.For(summary.Ref)));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelZoomStepResponse>>, ProblemHttpResult>> GetZoomPathAsync(
        string nodeRef, HttpContext context, IKernelNavigator navigator, INodeVisibility visibility, ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(nodeRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.GetZoomPathAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } path)
        {
            return KernelProblems.NotFound(node);
        }

        var masks = await KernelReadMasking.MaskAsync(
            visibility, actor, tenant, path.Where(s => s.Ref is not null).Select(s => s.Ref!), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelZoomStepResponse>>([.. path.Select((s, i) => KernelZoomStepResponse.From(s, MaskOfStep(path, i, masks)))]);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelChildResponse>>, ProblemHttpResult>> GetChildrenAsync(
        string nodeRef, HttpContext context, IKernelNavigator navigator, INodeVisibility visibility, ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(nodeRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } parent)
        {
            return KernelProblems.NotFound(node);
        }

        // Visibility reaches up the scope path, so every child of a hidden object is hidden too: even masked, the list
        // would say how many there are (a site's enrolled participants, a study's sites). It is refused, not masked (#5033).
        var parentMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [parent.Ref], cancellationToken).ConfigureAwait(false);
        if (parentMasks.For(parent.Ref) is { } parentMask)
        {
            return KernelProblems.NotVisible($"You can't see this {node.Kind} ({parentMask}), so its children are not listed.");
        }

        if (await navigator.GetChildrenAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } children)
        {
            return KernelProblems.NotFound(node);
        }

        var masks = await KernelReadMasking.MaskAsync(
            visibility, actor, tenant, children.Select(c => c.Node.Ref), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelChildResponse>>([.. children.Select(c => KernelChildResponse.From(c, masks.For(c.Node.Ref)))]);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelCommandResponse>>, ProblemHttpResult>> GetCommandsAsync(
        string nodeRef,
        HttpContext context,
        IAuthorizationProbe probe,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(nodeRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        var hidden = actor.Actor is not null
            && !await IsProbedNodeVisibleAsync(tenant, node, context.RequestServices, visibility, actor, cancellationToken).ConfigureAwait(false);
        var commands = hidden
            ? await probe.DenyHiddenCommandsAsync(tenant, node, cancellationToken).ConfigureAwait(false)
            : await probe.GetCommandsAsync(tenant, node, cancellationToken).ConfigureAwait(false);
        var masks = await MaskProbedExplanationsAsync(
            visibility,
            actor,
            tenant,
            node,
            hidden,
            commands.Select(c => c.Explanation),
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelCommandResponse>>([.. commands.Select(c => KernelCommandResponse.From(c, masks))]);
    }

    internal static async Task<Results<Ok<KernelDecisionResponse>, ProblemHttpResult>> CanIAsync(
        string capability,
        string resource,
        HttpContext context,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(resource, out var node))
        {
            return KernelProblems.InvalidNodeRef(resource);
        }

        // The verb class comes from the kernel capability declaration, never from the caller.
        if (catalog.FindCapability(capability) is not { } declared)
        {
            return KernelProblems.BadRequest("Unknown capability", $"No kernel command or read declares the capability '{capability}'.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var hidden = actor.Actor is not null
            && !await IsProbedNodeVisibleAsync(tenant, node, context.RequestServices, visibility, actor, cancellationToken).ConfigureAwait(false);
        var decision = hidden
            ? await probe.DenyHiddenAsync(tenant, declared, node, cancellationToken).ConfigureAwait(false)
            : await probe.CanIAsync(tenant, declared, node, cancellationToken).ConfigureAwait(false);
        var explanation = decision.Explain(declared.Token, node);
        var masks = await MaskProbedExplanationsAsync(
            visibility,
            actor,
            tenant,
            node,
            hidden,
            [explanation],
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelDecisionResponse.From(declared, node, decision, explanation, masks));
    }

    private static async Task<IReadOnlyDictionary<NodeRef, MaskReason>> MaskProbedExplanationsAsync(
        INodeVisibility visibility,
        ICurrentActor actor,
        TenantId tenant,
        NodeRef probedNode,
        bool hidden,
        IEnumerable<Explanation> explanations,
        CancellationToken cancellationToken)
    {
        var masks = await KernelReadMasking.MaskExplanationsAsync(
            visibility,
            actor,
            tenant,
            explanations,
            cancellationToken).ConfigureAwait(false);
        if (!hidden)
        {
            return masks;
        }

        // Non-confirming hidden probes always mask the probed node as NotPermitted so detected blinded
        // and undetected findings produce identical explanation shapes (K-AUTH-4, K-EXP-2).
        var normalized = new Dictionary<NodeRef, MaskReason>(masks)
        {
            [probedNode] = MaskReason.NotPermitted,
            [probedNode.Unversioned()] = MaskReason.NotPermitted,
        };
        return normalized;
    }

    private static async Task<bool> IsProbedNodeVisibleAsync(
        TenantId tenant,
        NodeRef node,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [node], cancellationToken).ConfigureAwait(false);
        if (masks.For(node) is not null)
        {
            return false;
        }

        if (node.Kind == NodeKinds.ValidationFinding
            && KernelValidationEndpoints.ResolveActiveValidationFindings(services) is { } findings)
        {
            var finding = await findings.FindAsync(tenant, ValidationFindingId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false);
            if (finding is null
                || !await KernelValidationEndpoints.IsFindingVisibleAsync(
                    tenant,
                    finding,
                    services,
                    visibility,
                    actor,
                    cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private static MaskReason? MaskOfStep(IReadOnlyList<ZoomStep> path, int index, IReadOnlyDictionary<NodeRef, MaskReason> masks) =>
        path[index].Ref is { } own
            ? masks.For(own)
            : masks.For(path.Skip(index + 1).Select(s => s.Ref).FirstOrDefault(r => r is not null));
}
