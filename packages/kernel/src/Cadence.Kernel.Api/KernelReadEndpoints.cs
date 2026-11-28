using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api;

/// <summary>
/// The kernel's read endpoints for the Clinical Canvas (ADR-2540): navigation (ADR-2481), dry-run authorization and
/// the command catalogue with their explanations (ADR-2482, ADR-2483), search (ADR-2484), protocol versions, and
/// lineage, dependents and amendment impact (<see cref="KernelLineageEndpoints"/>), and the activity stream
/// (<see cref="KernelActivityEndpoints"/>). They only read: nothing
/// here sends a command. Objects the caller may not see are masked (ADR-2471): kept in place in paths and child
/// lists, left out of search. Every endpoint acts in the tenant resolved from the caller's verified claims and, for
/// authorization, as the caller.
/// </summary>
public static class KernelReadEndpoints
{
    /// <summary>The maximum number of <c>ref</c> query parameters accepted by <c>GET /nodes</c> in one batch.</summary>
    public const int MaxBatchNodes = 100;

    /// <summary>Maps the read endpoints on the kernel route group (<see cref="KernelApiEndpointRouteBuilderExtensions.MapKernelApi"/>).</summary>
    public static RouteGroupBuilder MapKernelReads(this RouteGroupBuilder kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var reads = kernel.MapGroup(string.Empty).AddEndpointFilter<KernelTenantFilter>();

        reads.MapGet("/nodes", GetNodesAsync)
            .WithName("KernelGetNodes")
            .WithSummary("Resolves up to 100 NodeRefs to their summaries in one batch.")
            .WithDescription("Returns a dictionary keyed by each requested ref that exists in the caller's tenant. Unknown objects are omitted; objects the caller may not see are masked in place (ADR-2471).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

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
            .WithDescription("A node the caller may not see is refused; a child the caller may not see is masked in place (ADR-2471).")
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

        reads.MapGet("/enrollments/lookup", LookupEnrollmentAsync)
            .WithName("KernelLookupEnrollment")
            .WithSummary("Resolves a study code, subject code, and optional site code to pinned study and enrollment references.")
            .WithDescription("Reads over the tenant's unique study and subject indexes without listing sites. Returns a non-confirming 404 when the study, site, or enrollment does not exist, mismatches, or is masked for the caller (ADR-2471).")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reads.MapGet("/enrollments/{enrollmentRef}/schedule", GetEnrollmentScheduleAsync)
            .WithName("KernelGetEnrollmentSchedule")
            .WithSummary("Computes the governing Schedule of Activities and expected calendar visit windows for an enrollment.")
            .WithDescription("Resolves the governing protocol version effective at the enrollment's study site (K-TIME-8) and evaluates ExpectedSchedule.Compute over StatusSince in site calendar time (ADR-2487, K-TIME-7). Returns non-confirming 404 when absent or masked.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
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

        reads.MapGet("/protocol-versions/{versionRef}/schedule-of-activities", GetScheduleOfActivitiesAsync)
            .WithName("KernelGetScheduleOfActivities")
            .WithSummary("A protocol version's Schedule of Activities, compiled from its design.")
            .WithDescription("Planned activities as columns grouped by epoch, collection specifications as rows (ADR-2475). A version the caller may not see is refused.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        KernelLineageEndpoints.Map(reads);
        KernelActivityEndpoints.Map(reads);
        KernelSimulationEndpoints.MapSimulationEndpoints(reads);
        KernelExportEndpoints.Map(reads);
        return kernel;
    }

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

        if (refs.Length > MaxBatchNodes)
        {
            return KernelProblems.BadRequest("Too many node references", $"Specify at most {MaxBatchNodes} ref parameters per request.");
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
            return KernelProblems.BadRequest("Invalid limit", $"limit must be between 1 and {SearchRequest.MaxLimit}.");
        }

        var request = new SearchRequest(q, kind is { Length: > 0 } ? kind : null, pageSize);
        var tenant = KernelRequestTenant.Get(context);
        var hits = await search.SearchAsync(tenant, request, cancellationToken).ConfigureAwait(false);

        // A hit matched the caller's text, so a masked one would confirm the hidden object exists: it is left out.
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, hits.Select(h => h.Ref), cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok<IReadOnlyList<KernelSearchHitResponse>>(
            [.. hits.Where(h => masks.For(h.Ref) is null).Select(KernelSearchHitResponse.From)]);
    }

    internal static async Task<Results<Ok<KernelEnrollmentLookupResponse>, ProblemHttpResult>> LookupEnrollmentAsync(
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "subject_code")] string? subjectCode,
        [FromQuery(Name = "site_code")] string? siteCode,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var trimmedStudyCode = studyCode?.Trim();
        var trimmedSubjectCode = subjectCode?.Trim();
        var trimmedSiteCode = string.IsNullOrWhiteSpace(siteCode) ? null : siteCode.Trim();

        if (string.IsNullOrEmpty(trimmedStudyCode)
            || trimmedStudyCode.Length > SubjectCode.MaxLength
            || trimmedStudyCode.Any(char.IsControl)
            || (trimmedSiteCode is not null && (trimmedSiteCode.Length > SubjectCode.MaxLength || trimmedSiteCode.Any(char.IsControl)))
            || !SubjectCode.TryFrom(trimmedSubjectCode, out var parsedSubjectCode))
        {
            return KernelProblems.BadRequest(
                "Invalid lookup query",
                "Name a valid study_code, subject_code, and optional site_code.");
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await enrollments.LookupByCodeAsync(tenant, trimmedStudyCode, parsedSubjectCode, trimmedSiteCode, cancellationToken).ConfigureAwait(false)
            is not { } match)
        {
            return KernelProblems.EnrollmentLookupNotFound();
        }

        var studyRef = NodeRef.For(match.StudyId, match.StudyVersion);
        var studySiteRef = NodeRef.For(match.StudySiteId, match.StudySiteVersion);
        var enrollmentRef = NodeRef.For(match.EnrollmentId, match.EnrollmentVersion);

        // As in SearchAsync, a code lookup matches caller-supplied codes, so confirming a hidden study, study site,
        // or enrollment would reveal that it exists (ADR-2471): any masked node returns the same 404 as absent.
        var masks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            [studyRef, studySiteRef, enrollmentRef],
            cancellationToken).ConfigureAwait(false);
        if (masks.For(studyRef) is not null || masks.For(studySiteRef) is not null || masks.For(enrollmentRef) is not null)
        {
            return KernelProblems.EnrollmentLookupNotFound();
        }

        return TypedResults.Ok(KernelEnrollmentLookupResponse.From(match));
    }

    internal static async Task<Results<Ok<KernelEnrollmentScheduleResponse>, ProblemHttpResult>> GetEnrollmentScheduleAsync(
        string enrollmentRef,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        IProtocolVersionReader versionReader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken,
        [FromQuery(Name = "as_of")] DateOnly? asOf = null,
        [FromQuery(Name = "time_zone")] string? timeZone = null)
    {
        var trimmed = (enrollmentRef ?? string.Empty).Trim();
        NodeRef node;
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            if (!NodeRef.TryParse(trimmed, out var parsedNode) || parsedNode is null)
            {
                return KernelProblems.InvalidNodeRef(trimmed);
            }

            node = parsedNode;
            if (node.Kind != NodeKinds.Enrollment)
            {
                return KernelProblems.WrongKind(node, NodeKinds.Enrollment);
            }
        }
        else if (Guid.TryParse(trimmed, out var rawGuid) && rawGuid != Guid.Empty)
        {
            node = NodeRef.For(EnrollmentId.FromGuid(rawGuid));
        }
        else
        {
            return KernelProblems.InvalidNodeRef(trimmed);
        }

        TimeZoneInfo? explicitTimeZone = null;
        if (!string.IsNullOrWhiteSpace(timeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone.Trim(), out explicitTimeZone))
        {
            return KernelProblems.BadRequest("Invalid time_zone", $"Time zone '{timeZone}' is not recognized.");
        }

        var tenant = KernelRequestTenant.Get(context);
        var enrollmentId = EnrollmentId.FromGuid(node.Id);
        if (await enrollments.FindAsync(tenant, enrollmentId, cancellationToken).ConfigureAwait(false) is not { } enrollment)
        {
            return KernelProblems.NotFound(NodeRef.For(enrollmentId));
        }

        var studyNode = NodeRef.For(enrollment.StudyId);
        var studySiteNode = NodeRef.For(enrollment.StudySiteId);
        var enrollmentNode = NodeRef.For(enrollment.EnrollmentId);
        var scopeMasks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            [studyNode, studySiteNode, enrollmentNode],
            cancellationToken).ConfigureAwait(false);
        if (scopeMasks.For(studyNode) is not null
            || scopeMasks.For(studySiteNode) is not null
            || scopeMasks.For(enrollmentNode) is not null)
        {
            // Non-confirming 404 when the study, study site, or enrollment is hidden or blinded for the caller (ADR-2471).
            return KernelProblems.NotFound(enrollmentNode);
        }

        var clock = context.RequestServices?.GetService<IClock>() ?? SystemClock.Instance;
        var now = clock.UtcNow;
        var referenceDate = ToSiteLocalDate(enrollment.StatusSince, explicitTimeZone);
        var effectiveAsOf = asOf ?? ToSiteLocalNowDate(now, enrollment.StatusSince.Offset, explicitTimeZone);

        StudySiteGovernanceSummary? siteGovernance = null;
        if (context.RequestServices?.GetService<IStudyGovernanceReader>() is { } governanceReader)
        {
            siteGovernance = await governanceReader.FindStudySiteAsync(
                tenant,
                enrollment.StudySiteId,
                now,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<EffectiveVersion> effectiveVersions = siteGovernance?.EffectiveVersions ?? [];
        if (siteGovernance is null && context.RequestServices?.GetService<IEffectivityReader>() is { } effectivityReader)
        {
            effectiveVersions = await effectivityReader.EffectiveAtAsync(
                tenant,
                enrollment.StudySiteId,
                now,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        string? siteCode = siteGovernance?.SiteCode;
        string? country = siteGovernance?.Country;
        if ((siteCode is null || country is null) && context.RequestServices?.GetService<IStudyDirectory>() is { } studyDirectory)
        {
            var siteEntry = await studyDirectory.FindStudySiteAsync(tenant, enrollment.StudySiteId, cancellationToken).ConfigureAwait(false);
            siteCode ??= siteEntry?.SiteCode;
            country ??= siteEntry?.Country;
        }

        var studyVersions = await versionReader.ListForStudyAsync(tenant, enrollment.StudyId, cancellationToken).ConfigureAwait(false);
        var studyVersionMasks = studyVersions.Count == 0
            ? new Dictionary<NodeRef, MaskReason>()
            : await KernelReadMasking.MaskAsync(
                visibility,
                actor,
                tenant,
                studyVersions.Select(v => NodeRef.For(v.Id)),
                cancellationToken).ConfigureAwait(false);
        var latestVisibleVersion = studyVersions
            .FirstOrDefault(v => studyVersionMasks.For(NodeRef.For(v.Id)) is null);
        var latestStatus = latestVisibleVersion?.Status.ToString();

        if (effectiveVersions.Count == 0)
        {
            var guidance = BuildNoEffectiveVersionGuidance(latestVisibleVersion?.Status);
            return TypedResults.Ok(new KernelEnrollmentScheduleResponse(
                enrollmentNode.ToString(),
                studyNode.ToString(),
                studySiteNode.ToString(),
                siteCode,
                country,
                enrollment.Status.ToString(),
                enrollment.StatusSince,
                referenceDate,
                effectiveAsOf,
                GovernanceStatus: "NoEffectiveVersion",
                LatestStudyVersionStatus: latestStatus,
                Guidance: guidance,
                GoverningVersion: null,
                EffectiveVersions: [],
                Schedule: null,
                ExpectedVisits: []));
        }

        var governing = effectiveVersions
            .OrderByDescending(v => v.Number)
            .First();
        var governingVersionNode = NodeRef.For(governing.ProtocolVersionId);
        var versionMasks = await KernelReadMasking.MaskAsync(
            visibility,
            actor,
            tenant,
            [governingVersionNode],
            cancellationToken).ConfigureAwait(false);
        if (versionMasks.For(governingVersionNode) is not null)
        {
            return KernelProblems.NotFound(enrollmentNode);
        }

        var versionSummary = studyVersions.FirstOrDefault(v => v.Id == governing.ProtocolVersionId)
            ?? await versionReader.GetAsync(tenant, governing.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        var design = await versionReader.GetDesignAsync(tenant, governing.ProtocolVersionId, cancellationToken).ConfigureAwait(false);
        KernelScheduleOfActivitiesResponse? scheduleResponse = null;
        IReadOnlyList<KernelExpectedVisitResponse> expectedVisitResponses = [];

        if (design is not null && versionSummary is not null)
        {
            var governedElements = await versionReader
                .GetGovernedProtocolElementsAsync(tenant, governing.ProtocolVersionId, includeApprovedAncestorWhenUnapproved: false, cancellationToken)
                .ConfigureAwait(false);
            var formSnapshots = await versionReader
                .GetFormVersionSnapshotsAsync(tenant, governing.ProtocolVersionId, cancellationToken)
                .ConfigureAwait(false);
            var matrix = ScheduleOfActivities.Compile(design);
            scheduleResponse = KernelScheduleOfActivitiesResponse.From(versionSummary, matrix, governedElements, formSnapshots);
            var expected = ExpectedSchedule.Compute(design, referenceDate, effectiveAsOf);
            expectedVisitResponses = [.. expected.Select(v => KernelExpectedVisitResponse.From(v, governedElements))];
        }

        return TypedResults.Ok(new KernelEnrollmentScheduleResponse(
            enrollmentNode.ToString(),
            studyNode.ToString(),
            studySiteNode.ToString(),
            siteCode,
            country,
            enrollment.Status.ToString(),
            enrollment.StatusSince,
            referenceDate,
            effectiveAsOf,
            GovernanceStatus: "Effective",
            LatestStudyVersionStatus: latestStatus ?? versionSummary?.Status.ToString(),
            Guidance: null,
            GoverningVersion: KernelEffectiveVersionResponse.From(governing),
            EffectiveVersions: [.. effectiveVersions.OrderByDescending(v => v.Number).Select(KernelEffectiveVersionResponse.From)],
            Schedule: scheduleResponse,
            ExpectedVisits: expectedVisitResponses));
    }

    private static DateOnly ToSiteLocalDate(DateTimeOffset instant, TimeZoneInfo? explicitTimeZone) =>
        explicitTimeZone is not null
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, explicitTimeZone).DateTime)
            : DateOnly.FromDateTime(instant.DateTime);

    private static DateOnly ToSiteLocalNowDate(DateTimeOffset utcNow, TimeSpan siteOffset, TimeZoneInfo? explicitTimeZone) =>
        explicitTimeZone is not null
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, explicitTimeZone).DateTime)
            : DateOnly.FromDateTime(utcNow.ToOffset(siteOffset).DateTime);

    private static string BuildNoEffectiveVersionGuidance(ProtocolVersionStatus? latestStatus) =>
        latestStatus switch
        {
            ProtocolVersionStatus.Draft or ProtocolVersionStatus.InReview =>
                "No protocol version is effective at this study site yet. Submit and approve the protocol draft, then declare and activate site effectivity before capturing scheduled forms.",
            ProtocolVersionStatus.Approved =>
                "An approved protocol version exists for this study, but no effectivity is active at this study site yet. Declare and activate site effectivity in Governance to unlock casebook schedule columns.",
            _ =>
                "No protocol version is effective at this study site yet. Import and approve a protocol version, then activate site effectivity before capturing scheduled forms.",
        };

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
            return KernelProblems.InvalidNodeRef(studyRef);
        }

        if (node.Kind != NodeKinds.Study)
        {
            return KernelProblems.WrongKind(node, NodeKinds.Study);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await navigator.ResolveAsync(tenant, node, cancellationToken).ConfigureAwait(false) is not { } study)
        {
            return KernelProblems.NotFound(node);
        }

        // Listing a hidden study's versions would describe the study, so the list is refused, not masked.
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [study.Ref], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(study.Ref) is { } studyMask)
        {
            return KernelProblems.NotVisible($"You can't see this study ({studyMask}), so its protocol versions are not listed.");
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
            return KernelProblems.InvalidNodeRef(versionRef);
        }

        if (node.Kind != NodeKinds.ProtocolVersion)
        {
            return KernelProblems.WrongKind(node, NodeKinds.ProtocolVersion);
        }

        var tenant = KernelRequestTenant.Get(context);
        if (await reader.GetAsync(tenant, ProtocolVersionId.FromGuid(node.Id), cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelProblems.NotFound(node);
        }

        var versionNode = NodeRef.For(version.Id);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [versionNode], cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(KernelProtocolVersionResponse.From(version, masks.For(versionNode)));
    }

    internal static async Task<Results<Ok<KernelScheduleOfActivitiesResponse>, ProblemHttpResult>> GetScheduleOfActivitiesAsync(
        string versionRef,
        HttpContext context,
        IProtocolVersionReader reader,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(versionRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(versionRef);
        }

        if (node.Kind != NodeKinds.ProtocolVersion)
        {
            return KernelProblems.WrongKind(node, NodeKinds.ProtocolVersion);
        }

        var tenant = KernelRequestTenant.Get(context);
        var id = ProtocolVersionId.FromGuid(node.Id);
        if (await reader.GetAsync(tenant, id, cancellationToken).ConfigureAwait(false) is not { } version)
        {
            return KernelProblems.NotFound(node);
        }

        // The schedule is the version's content, so a version the caller may not see is refused, not masked.
        var versionNode = NodeRef.For(version.Id);
        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [versionNode], cancellationToken).ConfigureAwait(false);
        if (masks.For(versionNode) is { } mask)
        {
            return KernelProblems.NotVisible($"You can't see this protocol version ({mask}), so its Schedule of Activities is not shown.");
        }

        if (await reader.GetScheduleOfActivitiesAsync(tenant, id, cancellationToken).ConfigureAwait(false) is not { } matrix)
        {
            return KernelProblems.BadRequest("Not a typed design", $"{node} holds content that is not a typed protocol design, so it has no Schedule of Activities.");
        }

        var includeApprovedAncestor = false;
        if (version.Status != ProtocolVersionStatus.Approved && version.ParentVersionId is { } parentId)
        {
            var parentNode = NodeRef.For(parentId);
            var parentMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [parentNode], cancellationToken).ConfigureAwait(false);
            includeApprovedAncestor = parentMasks.For(parentNode) is null;
        }

        var governedElements = await reader
            .GetGovernedProtocolElementsAsync(tenant, id, includeApprovedAncestor, cancellationToken)
            .ConfigureAwait(false);
        var formSnapshots = await reader
            .GetFormVersionSnapshotsAsync(tenant, id, cancellationToken)
            .ConfigureAwait(false);

        return TypedResults.Ok(KernelScheduleOfActivitiesResponse.From(version, matrix, governedElements, formSnapshots));
    }

    /// <summary>
    /// A grouping level (a country) describes the object below it, so it is masked with that object; any other level is
    /// masked when its own object is.
    /// </summary>
    private static MaskReason? MaskOfStep(IReadOnlyList<ZoomStep> path, int index, IReadOnlyDictionary<NodeRef, MaskReason> masks) =>
        path[index].Ref is { } own
            ? masks.For(own)
            : masks.For(path.Skip(index + 1).Select(s => s.Ref).FirstOrDefault(r => r is not null));
}
