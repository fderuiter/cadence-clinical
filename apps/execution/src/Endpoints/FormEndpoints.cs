using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API route definitions for eCRF form submissions, electronic signatures, and unlock workflows.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// @req:SRS-EDC-003
/// </summary>
public static class FormEndpoints
{
    /// <summary>The semantic action a PI approval's e-signature token must carry.</summary>
    public const string ApproveSemanticAction = "execution.form.approve";

    /// <summary>
    /// The concrete action a PI approval's e-signature token must carry: the canonical approval path of that
    /// submission, whichever route alias the request came in on.
    /// </summary>
    public static string ApproveAction(string submissionId) => $"/api/v1/execution/forms/{submissionId}/approve";

    public static IEndpointRouteBuilder MapFormEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/forms")
            .WithTags("Forms");

        group.MapPost("/", CreateFormSubmissionAsync)
            .WithName("CreateFormSubmission")
            .Produces<FormSubmissionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id}", GetFormSubmissionByIdAsync)
            .WithName("GetFormSubmissionById")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/subject/{studyId}/{subjectId}", ListFormSubmissionsBySubjectAsync)
            .WithName("ListFormSubmissionsBySubject")
            .Produces<IReadOnlyList<FormSubmissionResponse>>(StatusCodes.Status200OK);

        group.MapPut("/{id}", UpdateFormPayloadAsync)
            .WithName("UpdateFormPayload")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status423Locked)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{id}/complete", CompleteFormSubmissionAsync)
            .WithName("CompleteFormSubmission")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id}/approve", ApproveFormSubmissionAsync)
            .WithName("ApproveFormSubmission")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id}/unlock", UnlockFormSubmissionAsync)
            .WithName("UnlockFormSubmission")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        // Undocumented alias for legacy Python router compatibility. The gateway proxies it, so every alias carries the
        // same filters as its documented route: a change without X-Change-Reason is refused here too (#5060).
        var legacyGroup = app.MapGroup("/api/v1/forms")
            .ExcludeFromDescription();

        legacyGroup.MapPost("/", CreateFormSubmissionAsync);
        legacyGroup.MapGet("/{id}", GetFormSubmissionByIdAsync);
        legacyGroup.MapPut("/{id}", UpdateFormPayloadAsync).AddEndpointFilter<ChangeReasonHeaderFilter>();
        legacyGroup.MapPost("/{id}/complete", CompleteFormSubmissionAsync);
        legacyGroup.MapPost("/{id}/approve", ApproveFormSubmissionAsync);
        legacyGroup.MapPost("/{id}/unlock", UnlockFormSubmissionAsync).AddEndpointFilter<ChangeReasonHeaderFilter>();

        return app;
    }

    public static async Task<IResult> CreateFormSubmissionAsync(
        FormSubmissionCreateRequest request,
        IFormSubmissionApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.CreateAsync(request, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: r => Results.Created($"/api/v1/execution/forms/{r.Id}", r),
            onFailure: e => e.ToProblemResult());
    }

    public static async Task<IResult> GetFormSubmissionByIdAsync(
        string id,
        IFormSubmissionApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> ListFormSubmissionsBySubjectAsync(
        string studyId,
        string subjectId,
        IFormSubmissionApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.ListBySubjectAsync(studyId, subjectId, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> UpdateFormPayloadAsync(
        string id,
        FormPayloadUpdateRequest request,
        IFormSubmissionApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.UpdatePayloadAsync(id, request.Payload, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> CompleteFormSubmissionAsync(
        string id,
        IFormSubmissionApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.CompleteAsync(id, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> ApproveFormSubmissionAsync(
        string id,
        FormSubmissionApproveRequest request,
        HttpContext httpContext,
        IFormSubmissionApplicationService svc,
        CancellationToken ct)
    {
        var sigToken = request.SigToken;
        if (string.IsNullOrWhiteSpace(sigToken))
        {
            sigToken = httpContext.Request.Headers[GatewayHeaders.SigToken].ToString();
            if (!string.IsNullOrWhiteSpace(sigToken))
            {
                request = request with { SigToken = sigToken };
            }
        }

        var result = await svc.ApproveAsync(id, request, httpContext.User.ToAuthenticatedCaller(), ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> UnlockFormSubmissionAsync(
        string id,
        FormUnlockRequest? request,
        IFormSubmissionApplicationService svc,
        ICurrentAuditContextAccessor auditContext,
        CancellationToken ct)
    {
        string reason = !string.IsNullOrWhiteSpace(request?.Reason) ? request.Reason : auditContext.ChangeReason;
        var result = await svc.UnlockAsync(id, reason, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }
}

public sealed record FormPayloadUpdateRequest(
    [property: JsonPropertyName("payload")] string Payload
);

public sealed record FormUnlockRequest(
    [property: JsonPropertyName("reason")] string Reason
);
