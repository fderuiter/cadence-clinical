using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Application;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Knowledge.Endpoints;

/// <summary>
/// Minimal API endpoints for Protocol Curriculum Authoring, Kernel Grant Batch Assignment, and 21 CFR Part 11
/// Qualification Attestation with TMF 05.02.04 filing.
/// @req:PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
/// </summary>
public static class TrainingEndpoints
{
    public const int MaxBatchLimit = TrainingCurriculumService.MaxBatchLimit;

    /// <summary>The <c>semantic_action</c> a training completion step-up token is bound to.</summary>
    public const string TrainingCompletionSemanticAction = "training.assignment.complete";

    /// <summary>Formats the HTTP <c>action</c> path a training completion step-up token is bound to.</summary>
    public static string TrainingCompletionAction(string assignmentId) =>
        $"/api/v1/training/assignments/{assignmentId}/complete";

    public static IEndpointRouteBuilder MapTrainingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/training")
            .WithTags("Study Training & Gating");

        group.MapGet("/curricula", ListCurriculaAsync)
            .WithName("ListCurricula")
            .RequirePermission("training_log:read").RequireCallerStudyScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<List<TrainingCurriculumDto>>(StatusCodes.Status200OK);

        group.MapPost("/curricula", CreateCurriculumAsync)
            .WithName("CreateCurriculum")
            .RequirePermission("training_curriculum:assign").RequireCallerStudyScope()
            .Produces<TrainingCurriculumDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

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

        group.MapGet("/study-staff", ListStudyStaffAsync)
            .WithName("ListStudyStaff")
            .RequirePermission("training_log:read").RequireCallerStudyScope()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<List<StudyStaffGrantDto>>(StatusCodes.Status200OK);

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

        group.MapPost("/assignments/{assignment_id}/complete", CompleteAssignmentAsync)
            .WithName("CompleteTrainingAssignment")
            .RequirePermission("training_log:sign").RequireCallerStudyAndSiteScope()
            .RequireElectronicSignature(
                TrainingCompletionSemanticAction,
                http => http.Request.Path.Value ?? string.Empty)
            .Produces<CompleteTrainingAssignmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static Task<IResult> ListCurriculaAsync(
        string? study_id,
        string? protocol_version,
        [FromServices] ITrainingCurriculumService curriculumService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        curriculumService.ListCurriculaAsync(study_id, protocol_version, httpContext, cancellationToken);

    private static Task<IResult> CreateCurriculumAsync(
        CreateTrainingCurriculumRequest request,
        [FromServices] ITrainingCurriculumService curriculumService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        curriculumService.CreateCurriculumAsync(request, httpContext, cancellationToken);

    private static Task<IResult> GetCurriculumAsync(
        string curriculum_id,
        [FromServices] ITrainingCurriculumService curriculumService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        curriculumService.GetCurriculumAsync(curriculum_id, httpContext, cancellationToken);

    private static Task<IResult> BatchAssignCurriculumAsync(
        BatchAssignCurriculumRequest request,
        [FromServices] ITrainingCurriculumService curriculumService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        curriculumService.BatchAssignCurriculumAsync(request, httpContext, cancellationToken);

    private static Task<IResult> ListStudyStaffAsync(
        string study_id,
        [FromServices] ITrainingCurriculumService curriculumService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        curriculumService.ListStudyStaffAsync(study_id, httpContext, cancellationToken);

    private static Task<IResult> ListAssignmentsAsync(
        string? study_id,
        string? site_id,
        string? user_id,
        string? status,
        [FromServices] ITrainingCurriculumService curriculumService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        curriculumService.ListAssignmentsAsync(study_id, site_id, user_id, status, httpContext, cancellationToken);

    private static Task<IResult> GetAssignmentDetailAsync(
        string assignment_id,
        [FromServices] ITrainingCurriculumService curriculumService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        curriculumService.GetAssignmentDetailAsync(assignment_id, httpContext, cancellationToken);

    private static Task<IResult> CompleteAssignmentAsync(
        string assignment_id,
        CompleteTrainingAssignmentRequest request,
        [FromServices] ITrainingCurriculumService curriculumService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        curriculumService.CompleteAssignmentAsync(assignment_id, request, httpContext, cancellationToken);
}
