using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Routes for asynchronous CSV subject roster batch ingestion and pre-enrollment validation queue.
/// @req:PRD-SUB-001
/// </summary>
public static class SubjectBatchEndpoints
{
    public static IEndpointRouteBuilder MapSubjectBatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/subjects/batch")
            .WithTags("SubjectBatchIngestion");

        group.MapPost("/upload", UploadBatchAsync)
            .WithName("UploadSubjectBatch")
            .WithSummary("Uploads a CSV subject roster file for background pre-enrollment validation.")
            .Produces<SubjectBatchUploadResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .DisableAntiforgery();

        group.MapGet("/{batchId}", GetBatchDetailAsync)
            .WithName("GetSubjectBatchDetail")
            .WithSummary("Retrieves status and row-level validation errors for a subject batch import job.")
            .Produces<SubjectBatchDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{batchId}/commit", CommitBatchAsync)
            .WithName("CommitSubjectBatch")
            .WithSummary("Transactionally commits valid subjects from a validated import batch.")
            .Produces<SubjectBatchCommitResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public static async Task<IResult> UploadBatchAsync(
        [FromQuery(Name = "study_id")] string? studyIdQuery,
        [FromForm(Name = "study_id")] string? studyIdForm,
        IFormFile? file,
        HttpContext http,
        ISubjectBatchApplicationService svc,
        CancellationToken ct)
    {
        if (SubjectAuthority.AuthorizeParticipantChange(http.User.ToAuthenticatedCaller()) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        string studyId = studyIdQuery ?? studyIdForm ?? string.Empty;
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Missing Study ID",
                Detail = "A 'study_id' query or form parameter is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        Stream csvStream;
        string fileName;

        if (file is not null && file.Length > 0)
        {
            csvStream = file.OpenReadStream();
            fileName = file.FileName;
        }
        else if (http.Request.HasFormContentType && http.Request.Form.Files.Count > 0)
        {
            var uploadedFile = http.Request.Form.Files[0];
            csvStream = uploadedFile.OpenReadStream();
            fileName = uploadedFile.FileName;
        }
        else if (http.Request.Body is not null)
        {
            csvStream = http.Request.Body;
            fileName = "batch_roster.csv";
        }
        else
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Missing CSV File",
                Detail = "A CSV roster file upload is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        await using (csvStream.ConfigureAwait(false))
        {
            var result = await svc.UploadBatchAsync(studyId, csvStream, fileName, ct).ConfigureAwait(false);
            return result.Match(
                onSuccess: r => Results.Accepted($"/api/v1/execution/subjects/batch/{r.BatchId}", r),
                onFailure: e => e.ToProblemResult());
        }
    }

    public static async Task<IResult> GetBatchDetailAsync(
        string batchId,
        ISubjectBatchApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.GetBatchDetailAsync(batchId, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> CommitBatchAsync(
        string batchId,
        HttpContext http,
        ISubjectBatchApplicationService svc,
        CancellationToken ct)
    {
        if (SubjectAuthority.AuthorizeParticipantChange(http.User.ToAuthenticatedCaller()) is { } forbidden)
        {
            return forbidden.ToProblemResult();
        }

        string changeReason = ChangeReasonHeader.Read(http.Request) ?? "Batch enrollment commit";

        var result = await svc.CommitBatchAsync(batchId, changeReason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }
}
