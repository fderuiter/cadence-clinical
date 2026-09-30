using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Kernel.Api;

/// <summary>
/// The kernel's read endpoints for the Clinical Canvas (ADR-2540): navigation (ADR-2481), dry-run authorization and
/// the command catalogue with their explanations (ADR-2482, ADR-2483), search (ADR-2484) and protocol versions. They only read: nothing
/// here sends a command. Objects the caller may not see are masked (ADR-2471): kept in place in paths and child
/// lists, left out of search. Every endpoint acts in the tenant resolved from the caller's verified claims and, for
/// authorization, as the caller.
/// </summary>
public static class KernelReadEndpoints
{
    /// <summary>Maps the read endpoints on the kernel route group (<see cref="KernelApiEndpointRouteBuilderExtensions.MapKernelApi"/>).</summary>
    public static RouteGroupBuilder MapKernelReads(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var reads = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>();

        reads.MapGet("/nodes/{nodeRef}", GetNodeAsync)
            .WithName("KernelGetNode")
            .WithSummary("Resolves a NodeRef to its summary.")
            .WithDescription("Title, code and state of one kernel object in the caller's tenant, at its current version.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/nodes/{nodeRef}/path", GetZoomPathAsync)
            .WithName("KernelGetZoomPath")
            .WithSummary("The zoom path from the tenant down to an object.")
            .WithDescription("Presentation only: the path never grants or implies authority (spec §9.3).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/nodes/{nodeRef}/children", GetChildrenAsync)
            .WithName("KernelGetChildren")
            .WithSummary("The children of a scope node, with their own child counts.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/nodes/{nodeRef}/commands", GetCommandsAsync)
            .WithName("KernelGetCommands")
            .WithSummary("The kernel commands offered on an object, each with the caller's dry-run decision.")
            .WithDescription("Each decision is the one sending the command would get now (ADR-2483), explained (ADR-2482).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/authorization/can-i", CanIAsync)
            .WithName("KernelCanI")
            .WithSummary("Whether the caller may exercise a capability on an object now, and why.")
            .WithDescription("A dry run: it executes nothing and is recorded in the authorization access log, not the audit chain (ADR-2483).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/search", SearchAsync)
            .WithName("KernelSearch")
            .WithSummary("Finds kernel objects in the caller's tenant by code or title.")
            .WithDescription("Each word matches as a prefix; an exact code ranks first. Sources that reveal identity are searched only when the caller holds the capability (ADR-2484).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/studies/{studyRef}/protocol-versions", ListProtocolVersionsAsync)
            .WithName("KernelListProtocolVersions")
            .WithSummary("A study's protocol versions, newest first, without their content.")
            .WithDescription("A study the caller may not see is refused; a version the caller may not see is masked in place (ADR-2471).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/protocol-versions/{versionRef}", GetProtocolVersionAsync)
            .WithName("KernelGetProtocolVersion")
            .WithSummary("One protocol version, without its content.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return kernel;
    }

    internal static async Task<Results<Ok<KernelNodeResponse>, ProblemHttpResult>> GetNodeAsync(
        string nodeRef, HttpContext context, IKernelNavigator navigator, INodeVisibility visibility, ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return InvalidNodeRef(nodeRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } summary)
        {
            return NotFound(node);
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
            return InvalidNodeRef(nodeRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.GetZoomPathAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } path)
        {
            return NotFound(node);
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
            return InvalidNodeRef(nodeRef);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.GetChildrenAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } children)
        {
            return NotFound(node);
        }

        var masks = await KernelReadMasking.MaskAsync(
            visibility, actor, tenant, children.Select(c => c.Node.Ref), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelChildResponse>>([.. children.Select(c => KernelChildResponse.From(c, masks.For(c.Node.Ref)))]);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelCommandResponse>>, ProblemHttpResult>> GetCommandsAsync(
        string nodeRef, HttpContext context, IAuthorizationProbe probe, CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return InvalidNodeRef(nodeRef);
        }

        var commands = await probe.GetCommandsAsync(KernelRequestTenant.Get(context), node, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelCommandResponse>>([.. commands.Select(KernelCommandResponse.From)]);
    }

    internal static async Task<Results<Ok<KernelDecisionResponse>, ProblemHttpResult>> CanIAsync(
        string capability,
        string resource,
        HttpContext context,
        IAuthorizationProbe probe,
        KernelCommandCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(resource, out var node))
        {
            return InvalidNodeRef(resource);
        }

        // The verb class comes from the command that declares the capability, never from the caller.
        if (catalog.Entries.Select(e => e.Capability).FirstOrDefault(c => string.Equals(c.Token, capability, StringComparison.Ordinal))
            is not { } declared)
        {
            return TypedResults.Problem(
                title: "Unknown capability",
                detail: $"No kernel command declares the capability '{capability}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var decision = await probe.CanIAsync(KernelRequestTenant.Get(context), declared, node, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelDecisionResponse.From(declared, node, decision, decision.Explain(declared.Token, node)));
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelSearchHitResponse>>, ProblemHttpResult>> SearchAsync(
        string q,
        string[]? kind,
        int? limit,
        HttpContext context,
        IKernelSearch search,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var pageSize = limit ?? 20;
        if (pageSize is < 1 or > SearchRequest.MaxLimit)
        {
            return TypedResults.Problem(
                title: "Invalid limit",
                detail: $"limit must be between 1 and {SearchRequest.MaxLimit}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var request = new SearchRequest(q, kind is { Length: > 0 } ? kind : null, pageSize);
        var tenant = KernelRequestTenant.Get(context);
        var hits = await search.SearchAsync(tenant, request, cancellationToken).ConfigureAwait(false);

        // A hit matched the caller's text, so a masked one would confirm the hidden object exists: it is left out.
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, hits.Select(h => h.Ref), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelSearchHitResponse>>(
            [.. hits.Where(h => masks.For(h.Ref) is null).Select(KernelSearchHitResponse.From)]);
    }

    internal static async Task<Results<Ok<IReadOnlyList<KernelProtocolVersionResponse>>, ProblemHttpResult>> ListProtocolVersionsAsync(
        string studyRef,
        HttpContext context,
        IKernelNavigator navigator,
        IProtocolVersionReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(studyRef, out var node))
        {
            return InvalidNodeRef(studyRef);
        }

        if (node.Kind != NodeKinds.Study)
        {
            return WrongKind(node, NodeKinds.Study);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } study)
        {
            return NotFound(node);
        }

        // Listing a hidden study's versions would describe the study, so the list is refused, not masked.
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [study.Ref], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(study.Ref) is { } studyMask)
        {
            return TypedResults.Problem(
                title: "Not visible",
                detail: $"You can't see this study ({studyMask}), so its protocol versions are not listed.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var versions = await reader.ListForStudyAsync(tenant, StudyId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false);
        var masks = await KernelReadMasking.MaskAsync(
            visibility, actor, tenant, versions.Select(v => NodeRef.For(v.Id)), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelProtocolVersionResponse>>(
            [.. versions.Select(v => KernelProtocolVersionResponse.From(v, masks.For(NodeRef.For(v.Id))))]);
    }

    internal static async Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> GetProtocolVersionAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(versionRef, out var node))
        {
            return InvalidNodeRef(versionRef);
        }

        if (node.Kind != NodeKinds.ProtocolVersion)
        {
            return WrongKind(node, NodeKinds.ProtocolVersion);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await reader.GetAsync(tenant, ProtocolVersionId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return NotFound(node);
        }

        var versionNode = NodeRef.For(version.Id);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [versionNode], cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelProtocolVersionResponse.From(version, masks.For(versionNode)));
    }

    /// <summary>
    /// A grouping level (a country) describes the object below it, so it is masked with that object; any other level
    /// is masked when its own object is.
    /// </summary>
    private static MaskReason? MaskOfStep(IReadOnlyList<ZoomStep> path, int index, IReadOnlyDictionary<NodeRef, MaskReason> masks) =>
        path[index].Ref is { } own
            ? masks.For(own)
            : masks.For(path.Skip(index + 1).Select(s => s.Ref).FirstOrDefault(r => r is not null));

    private static ProblemHttpResult InvalidNodeRef(string text) =>
        TypedResults.Problem(
            title: "Invalid NodeRef",
            detail: $"'{text}' is not a NodeRef: expected kind:id or kind:id@version with a registered kind and a lower-case UUID.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult WrongKind(NodeRef node, string expected) =>
        TypedResults.Problem(
            title: "Wrong kind",
            detail: $"{node} is a {node.Kind}; this endpoint takes a {expected}.",
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFound(NodeRef node) =>
        TypedResults.Problem(
            title: "Not found",
            detail: $"{node} does not exist in your tenant.",
            statusCode: StatusCodes.Status404NotFound);
}
