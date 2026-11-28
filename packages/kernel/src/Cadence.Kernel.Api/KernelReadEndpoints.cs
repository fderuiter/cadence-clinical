using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Tenancy;

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

    internal static Task<Results<Ok<KernelBatchNodesResponse>, ProblemHttpResult>> GetNodesAsync(
        [FromQuery(Name = "ref")] string[]? refs, HttpContext context, IKernelNavigator navigator,
        INodeVisibility visibility, ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelNodeAndAuthorizationReadEndpoints.GetNodesAsync(refs, context, navigator, visibility, actor, cancellationToken);

    internal static Task<IReadOnlyDictionary<string, KernelNodeResponse>> ResolveMaskedNodesAsync(
        TenantId tenant, IReadOnlyList<(string Key, NodeRef Parsed)> requested, IKernelNavigator navigator,
        INodeVisibility visibility, ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelNodeAndAuthorizationReadEndpoints.ResolveMaskedNodesAsync(tenant, requested, navigator, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelNodeResponse>, ProblemHttpResult>> GetNodeAsync(
        string nodeRef, HttpContext context, IKernelNavigator navigator, INodeVisibility visibility,
        ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelNodeAndAuthorizationReadEndpoints.GetNodeAsync(nodeRef, context, navigator, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<KernelZoomStepResponse>>, ProblemHttpResult>> GetZoomPathAsync(
        string nodeRef, HttpContext context, IKernelNavigator navigator, INodeVisibility visibility,
        ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelNodeAndAuthorizationReadEndpoints.GetZoomPathAsync(nodeRef, context, navigator, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<KernelChildResponse>>, ProblemHttpResult>> GetChildrenAsync(
        string nodeRef, HttpContext context, IKernelNavigator navigator, INodeVisibility visibility,
        ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelNodeAndAuthorizationReadEndpoints.GetChildrenAsync(nodeRef, context, navigator, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<KernelCommandResponse>>, ProblemHttpResult>> GetCommandsAsync(
        string nodeRef, HttpContext context, IAuthorizationProbe probe, INodeVisibility visibility,
        ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelNodeAndAuthorizationReadEndpoints.GetCommandsAsync(nodeRef, context, probe, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelDecisionResponse>, ProblemHttpResult>> CanIAsync(
        string capability, string resource, HttpContext context, IAuthorizationProbe probe,
        KernelCommandCatalog catalog, INodeVisibility visibility, ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelNodeAndAuthorizationReadEndpoints.CanIAsync(capability, resource, context, probe, catalog, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<IReadOnlyList<KernelSearchHitResponse>>, ProblemHttpResult>> SearchAsync(
        string q, string[]? kind, int? limit, HttpContext context, IKernelSearch search,
        INodeVisibility visibility, ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelSearchAndScheduleReadEndpoints.SearchAsync(q, kind, limit, context, search, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelEnrollmentLookupResponse>, ProblemHttpResult>> LookupEnrollmentAsync(
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "subject_code")] string? subjectCode,
        [FromQuery(Name = "site_code")] string? siteCode,
        HttpContext context, IEnrollmentDirectory enrollments, INodeVisibility visibility,
        ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelSearchAndScheduleReadEndpoints.LookupEnrollmentAsync(
            studyCode, subjectCode, siteCode, context, enrollments, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelEnrollmentScheduleResponse>, ProblemHttpResult>> GetEnrollmentScheduleAsync(
        string enrollmentRef, HttpContext context, IEnrollmentDirectory enrollments, IProtocolVersionReader versionReader,
        INodeVisibility visibility, ICurrentActor actor, CancellationToken cancellationToken,
        [FromQuery(Name = "as_of")] DateOnly? asOf = null, [FromQuery(Name = "time_zone")] string? timeZone = null) =>
        KernelSearchAndScheduleReadEndpoints.GetEnrollmentScheduleAsync(
            enrollmentRef, context, enrollments, versionReader, visibility, actor, cancellationToken, asOf, timeZone);

    internal static Task<Results<Ok<IReadOnlyList<KernelProtocolVersionResponse>>, ProblemHttpResult>> ListProtocolVersionsAsync(
        string studyRef, HttpContext context, IKernelNavigator navigator, IProtocolVersionReader reader,
        INodeVisibility visibility, ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelSearchAndScheduleReadEndpoints.ListProtocolVersionsAsync(studyRef, context, navigator, reader, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelProtocolVersionResponse>, ProblemHttpResult>> GetProtocolVersionAsync(
        string versionRef, HttpContext context, IProtocolVersionReader reader, INodeVisibility visibility,
        ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelSearchAndScheduleReadEndpoints.GetProtocolVersionAsync(versionRef, context, reader, visibility, actor, cancellationToken);

    internal static Task<Results<Ok<KernelScheduleOfActivitiesResponse>, ProblemHttpResult>> GetScheduleOfActivitiesAsync(
        string versionRef, HttpContext context, IProtocolVersionReader reader, INodeVisibility visibility,
        ICurrentActor actor, CancellationToken cancellationToken) =>
        KernelSearchAndScheduleReadEndpoints.GetScheduleOfActivitiesAsync(versionRef, context, reader, visibility, actor, cancellationToken);
}
