using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

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

    /// <summary>
    /// The concrete action a batch sign-off's e-signature token must carry, on either route alias. The token is also
    /// bound to the batch (<see cref="BatchSignOffRequest.BatchBindingId"/>) under <see cref="ApproveSemanticAction"/>.
    /// </summary>
    public const string BatchSignOffAction = "/api/v1/execution/batch-sign-off";

    // Route permissions (ADR-2516). Site staff enter and correct form data; only an investigator signs (the service
    // also checks the signer's role); unlocking a signed form for correction is a data-management action.
    private const string ReadData = "ecrf_data_entry:read";
    private const string EnterData = "ecrf_data_entry:create";
    private const string UpdateData = "ecrf_data_entry:update";
    private const string SignForm = "ecrf_form:sign";
    private const string UnlockForm = "ecrf_form:unlock";

    public static IEndpointRouteBuilder MapFormEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/forms")
            .WithTags("Forms");

        group.MapPost("/", CreateFormSubmissionAsync)
            .WithName("CreateFormSubmission")
            .Produces<FormSubmissionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(EnterData);

        group.MapGet("/{id}", GetFormSubmissionByIdAsync)
            .WithName("GetFormSubmissionById")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadData);

        group.MapGet("/subject/{studyId}/{subjectId}", ListFormSubmissionsBySubjectAsync)
            .WithName("ListFormSubmissionsBySubject")
            .Produces<IReadOnlyList<FormSubmissionResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadData)
            .RequireStudyScope("studyId");

        group.MapPut("/{id}", UpdateFormPayloadAsync)
            .WithName("UpdateFormPayload")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status423Locked)
            .RequirePermission(UpdateData)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{id}/complete", CompleteFormSubmissionAsync)
            .WithName("CompleteFormSubmission")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(UpdateData);

        group.MapPost("/{id}/approve", ApproveFormSubmissionAsync)
            .WithName("ApproveFormSubmission")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(SignForm)
            .RequireElectronicSignature(ApproveSemanticAction, ApproveActionFor);

        group.MapPost("/{id}/unlock", UnlockFormSubmissionAsync)
            .WithName("UnlockFormSubmission")
            .Produces<FormSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(UnlockForm)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        app.MapPost("/api/v1/execution/batch-sign-off", BatchSignOffAsync)
            .WithName("BatchSignOff")
            .WithTags("Forms")
            .Produces<BatchSignOffResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status423Locked)
            .RequirePermission(SignForm)
            .RequireElectronicSignature(BatchSignOffBinding);

        app.MapPost("/api/v1/execution/signatures/batch-sign-off", BatchSignOffAsync)
            .ExcludeFromDescription()
            .RequirePermission(SignForm)
            .RequireElectronicSignature(BatchSignOffBinding);

        // Undocumented alias for legacy Python router compatibility. The gateway proxies it, so every alias carries the
        // same filters as its documented route: a change without X-Change-Reason is refused here too (#5060).
        var legacyGroup = app.MapGroup("/api/v1/forms")
            .ExcludeFromDescription();

        legacyGroup.MapPost("/", CreateFormSubmissionAsync).RequirePermission(EnterData);
        legacyGroup.MapGet("/{id}", GetFormSubmissionByIdAsync).RequirePermission(ReadData);
        legacyGroup.MapPut("/{id}", UpdateFormPayloadAsync).RequirePermission(UpdateData).AddEndpointFilter<ChangeReasonHeaderFilter>();
        legacyGroup.MapPost("/{id}/complete", CompleteFormSubmissionAsync).RequirePermission(UpdateData);
        legacyGroup.MapPost("/{id}/approve", ApproveFormSubmissionAsync)
            .RequirePermission(SignForm)
            .RequireElectronicSignature(ApproveSemanticAction, ApproveActionFor);
        legacyGroup.MapPost("/{id}/unlock", UnlockFormSubmissionAsync).RequirePermission(UnlockForm).AddEndpointFilter<ChangeReasonHeaderFilter>();

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
        var result = await svc.UpdatePayloadAsync(id, request.Payload, request.FormHash, ct).ConfigureAwait(false);
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
        var result = await svc.ApproveAsync(id, request, httpContext.User.ToAuthenticatedCaller(), ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    private static SigTokenBinding? BatchSignOffBinding(EndpointFilterInvocationContext context) =>
        context.Arguments.OfType<BatchSignOffRequest>().FirstOrDefault() is { } request
            ? new SigTokenBinding(BatchSignOffAction, ApproveSemanticAction, request.BatchBindingId())
            : null;

    private static string ApproveActionFor(HttpContext httpContext) =>
        ApproveAction(httpContext.Request.RouteValues["id"]?.ToString() ?? string.Empty);

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

    public static async Task<IResult> BatchSignOffAsync(
        BatchSignOffRequest request,
        HttpContext httpContext,
        IFormSubmissionApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.BatchSignOffAsync(request, httpContext.User.ToAuthenticatedCaller(), httpContext.VerifiedSigToken(), ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }
}

public sealed record FormPayloadUpdateRequest(
    [property: JsonPropertyName("payload")] string Payload,
    [property: JsonPropertyName("form_hash")] string? FormHash = null
);

public sealed record FormUnlockRequest(
    [property: JsonPropertyName("reason")] string Reason
);
