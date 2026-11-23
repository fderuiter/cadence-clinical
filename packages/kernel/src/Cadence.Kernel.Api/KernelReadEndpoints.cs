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
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Time;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

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

        reads.MapGet("/enrollments/{enrollmentRef}/schedule-of-activities", GetEnrollmentScheduleOfActivitiesAsync)
            .WithName("KernelGetEnrollmentScheduleOfActivities")
            .WithSummary("An enrollment's governing Schedule of Activities and re-consent lock state.")
            .WithDescription("Resolves the participant's bitemporal Applicability at validAt (defaulting to now) and returns the governing Schedule of Activities plus any pending RequiresReconsent transitions (spec §7.5, PRD-SUB-007).")
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
        string nodeRef, HttpContext context, IAuthorizationProbe probe, CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(nodeRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(nodeRef);
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
            return KernelProblems.InvalidNodeRef(resource);
        }

        // The verb class comes from the command that declares the capability, never from the caller.
        if (catalog.Entries.Select(e => e.Capability).FirstOrDefault(c => string.Equals(c.Token, capability, StringComparison.Ordinal))
            is not { } declared)
        {
            return KernelProblems.BadRequest("Unknown capability", $"No kernel command declares the capability '{capability}'.");
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

        return await reader.GetScheduleOfActivitiesAsync(tenant, id, cancellationToken).ConfigureAwait(false) is { } matrix
            ? TypedResults.Ok(KernelScheduleOfActivitiesResponse.From(version, matrix))
            : KernelProblems.BadRequest("Not a typed design", $"{node} holds content that is not a typed protocol design, so it has no Schedule of Activities.");
    }

    internal static async Task<Results<Ok<KernelEnrollmentScheduleResponse>, ProblemHttpResult>> GetEnrollmentScheduleOfActivitiesAsync(
        string enrollmentRef,
        [Microsoft.AspNetCore.Mvc.FromQuery(Name = "valid_at")] DateTimeOffset? validAt,
        HttpContext context,
        IEnrollmentDirectory enrollments,
        IApplicabilityReader applicability,
        IProtocolVersionReader protocolVersions,
        INodeVisibility visibility,
        ICurrentActor actor,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!NodeRef.TryParse(enrollmentRef, out var node))
        {
            return KernelProblems.InvalidNodeRef(enrollmentRef);
        }

        if (node.Kind != NodeKinds.Enrollment)
        {
            return KernelProblems.WrongKind(node, NodeKinds.Enrollment);
        }

        var tenant = KernelRequestTenant.Get(context);
        var enrollmentId = EnrollmentId.FromGuid(node.Id);
        if (await enrollments.FindAsync(tenant, enrollmentId, cancellationToken).ConfigureAwait(false) is null)
        {
            return KernelProblems.NotFound(node);
        }

        var enrollmentNode = NodeRef.For(enrollmentId);
        var enrollmentMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [enrollmentNode], cancellationToken).ConfigureAwait(false);
        if (enrollmentMasks.For(enrollmentNode) is { } enrollmentMask)
        {
            return KernelProblems.NotVisible($"You can't see this enrollment ({enrollmentMask}), so its governing schedule is not shown.");
        }

        var now = clock.UtcNow;
        var resolution = await applicability.ResolveAsync(
            tenant,
            enrollmentId,
            validAt ?? now,
            now,
            cancellationToken).ConfigureAwait(false);

        KernelProtocolVersionResponse? governingVersionResponse = null;
        KernelScheduleOfActivitiesResponse? soaResponse = null;
        if (resolution.ProtocolVersionId is { } governingId
            && await protocolVersions.GetAsync(tenant, governingId, cancellationToken).ConfigureAwait(false) is { } governingVersion)
        {
            var versionNode = NodeRef.For(governingVersion.Id);
            var versionMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [versionNode], cancellationToken).ConfigureAwait(false);
            var versionMask = versionMasks.For(versionNode);
            governingVersionResponse = KernelProtocolVersionResponse.From(governingVersion, versionMask);
            if (versionMask is null
                && await protocolVersions.GetScheduleOfActivitiesAsync(tenant, governingId, cancellationToken).ConfigureAwait(false) is { } matrix)
            {
                soaResponse = KernelScheduleOfActivitiesResponse.From(governingVersion, matrix);
            }
        }

        var pendingResponses = new List<KernelPendingReconsentResponse>();
        if (resolution.PendingTransitions is { Count: > 0 } pendingList)
        {
            foreach (var pending in pendingList)
            {
                var targetVersion = await protocolVersions.GetAsync(tenant, pending.TargetVersionId, cancellationToken).ConfigureAwait(false);
                var targetNode = NodeRef.For(pending.TargetVersionId);
                var targetMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [targetNode], cancellationToken).ConfigureAwait(false);
                bool targetVisible = targetMasks.For(targetNode) is null;
                pendingResponses.Add(new KernelPendingReconsentResponse(
                    targetNode.ToString(),
                    targetVisible ? (targetVersion?.Number ?? pending.TargetVersionNumber) : null,
                    targetVisible ? targetVersion?.AmendmentIntent : null,
                    NodeRef.For(pending.EffectivityId).ToString(),
                    pending.RequiredSince));
            }
        }

        return TypedResults.Ok(new KernelEnrollmentScheduleResponse(
            enrollmentNode.ToString(),
            governingVersionResponse,
            soaResponse,
            RequiresReconsent: resolution.RequiresReconsent,
            IsInconsistent: resolution.IsInconsistent,
            InconsistencyReason: resolution.InconsistencyReason,
            PendingTransitions: pendingResponses));
    }

    /// <summary>
    /// A grouping level (a country) describes the object below it, so it is masked with that object; any other level
    /// is masked when its own object is.
    /// </summary>
    private static MaskReason? MaskOfStep(IReadOnlyList<ZoomStep> path, int index, IReadOnlyDictionary<NodeRef, MaskReason> masks) =>
        path[index].Ref is { } own
            ? masks.For(own)
            : masks.For(path.Skip(index + 1).Select(s => s.Ref).FirstOrDefault(r => r is not null));
}
