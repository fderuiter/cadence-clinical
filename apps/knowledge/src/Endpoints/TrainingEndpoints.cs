using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Application;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Knowledge.Endpoints;

/// <summary>
/// Minimal API endpoints for Batch Curriculum Assignment and Bulk Delegation Sign-Off.
/// @req:PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
/// </summary>
public static class TrainingEndpoints
{
    public const int MaxBatchLimit = TrainingApplicationService.MaxBatchLimit;

    /// <summary>The <c>action</c> a delegation sign-off token is bound to.</summary>
    public const string DelegationSignOffAction = "/api/v1/training/delegations/bulk-signoff";

    /// <summary>The <c>semantic_action</c> a delegation sign-off token is bound to.</summary>
    public const string DelegationSignOffSemanticAction = "knowledge.delegation.sign_off";

    public static IEndpointRouteBuilder MapTrainingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/training")
            .WithTags("Study Training & Gating");

        group.MapGet("/curricula", ListCurriculaAsync)
            .WithName("ListCurricula")
            .RequirePermission("training_log:read").RequireCallerStudyScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<List<TrainingCurriculumDto>>(StatusCodes.Status200OK);

        group.MapGet("/curricula/{curriculum_id}", GetCurriculumAsync)
            .WithName("GetCurriculum")
            .RequirePermission("training_log:read").RequireCallerStudyScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<TrainingCurriculumDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/curricula/batch-assign", BatchAssignCurriculumAsync)
            .WithName("BatchAssignCurriculum")
            .RequirePermission("training_curriculum:assign").RequireCallerStudyScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<BatchAssignmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/assignments", ListAssignmentsAsync)
            .WithName("ListAssignments")
            .RequirePermission("training_log:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<List<TrainingAssignmentDto>>(StatusCodes.Status200OK);

        group.MapGet("/assignments/{assignment_id}", GetAssignmentDetailAsync)
            .WithName("GetAssignmentDetail")
            .RequirePermission("training_log:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<TrainingAssignmentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/delegations", ListDelegationsAsync)
            .WithName("ListDelegations")
            .RequirePermission("delegation_log:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<List<DelegationSummaryDto>>(StatusCodes.Status200OK);

        group.MapPost("/delegations/bulk-signoff", BulkSignOffDelegationsAsync)
            .WithName("BulkSignOffDelegations")
            .RequirePermission("delegation_log:sign").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(DelegationSignOffBinding)
            .Produces<BulkDelegationSignOffResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static Task<IResult> ListCurriculaAsync(
        string? study_id,
        string? protocol_version,
        [FromServices] TrainingApplicationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        service.ListCurriculaAsync(study_id, protocol_version, httpContext, cancellationToken);

    private static Task<IResult> GetCurriculumAsync(
        string curriculum_id,
        [FromServices] TrainingApplicationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        service.GetCurriculumAsync(curriculum_id, httpContext, cancellationToken);

    private static Task<IResult> BatchAssignCurriculumAsync(
        BatchAssignCurriculumRequest request,
        [FromServices] TrainingApplicationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        service.BatchAssignCurriculumAsync(request, httpContext, cancellationToken);

    private static Task<IResult> ListAssignmentsAsync(
        string? study_id,
        string? site_id,
        string? user_id,
        string? status,
        [FromServices] TrainingApplicationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        service.ListAssignmentsAsync(study_id, site_id, user_id, status, httpContext, cancellationToken);

    private static Task<IResult> GetAssignmentDetailAsync(
        string assignment_id,
        [FromServices] TrainingApplicationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        service.GetAssignmentDetailAsync(assignment_id, httpContext, cancellationToken);

    private static Task<IResult> ListDelegationsAsync(
        string? study_id,
        string? site_id,
        string? status,
        [FromServices] TrainingApplicationService service,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        DateTimeOffset? valid_at = null,
        DateTimeOffset? known_at = null) =>
        service.ListDelegationsAsync(study_id, site_id, status, httpContext, cancellationToken, valid_at, known_at);

    private static SigTokenBinding? DelegationSignOffBinding(EndpointFilterInvocationContext context) =>
        context.Arguments.OfType<BulkDelegationSignOffRequest>().FirstOrDefault() is { DelegationIds.Count: > 0 } request
            ? new SigTokenBinding(DelegationSignOffAction, DelegationSignOffSemanticAction, request.BatchBindingId())
            : null;

    private static Task<IResult> BulkSignOffDelegationsAsync(
        BulkDelegationSignOffRequest request,
        HttpContext httpContext,
        [FromServices] TrainingApplicationService service,
        IOptionsMonitor<GatewayAuthenticationOptions> gatewayAuthentication,
        CancellationToken cancellationToken) =>
        service.BulkSignOffDelegationsAsync(request, httpContext, gatewayAuthentication, cancellationToken);
}
