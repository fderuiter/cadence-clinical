using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API route definitions for clinical discrepancy query lifecycle and workflow operations.
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-004
/// </summary>
public static class QueryEndpoints
{
    // Route permissions (ADR-2516). Monitors and data managers raise queries and site staff answer them; closing,
    // reopening and cancelling one is the CRA's or data manager's decision (the service also checks the role).
    private const string ReadQueries = "query_lifecycle:read";
    private const string RaiseQuery = "query_lifecycle:create";
    private const string AnswerQuery = "query_lifecycle:update";
    private const string ManageQuery = "query_lifecycle:manage";

    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/queries")
            .WithTags("Queries");

        group.MapPost("/", CreateQueryAsync)
            .WithName("CreateClinicalQuery")
            .Produces<ClinicalQueryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(RaiseQuery);

        group.MapGet("/{id}", GetQueryByIdAsync)
            .WithName("GetClinicalQueryById")
            .Produces<ClinicalQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ReadQueries);

        group.MapGet("/", ListQueriesAsync)
            .WithName("ListClinicalQueries")
            .Produces<IReadOnlyList<ClinicalQueryResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ReadQueries);

        group.MapPost("/{id}/respond", AnswerQueryAsync)
            .WithName("RespondClinicalQuery")
            .Produces<ClinicalQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(AnswerQuery)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{id}/answer", AnswerQueryAsync)
            .WithName("AnswerClinicalQuery")
            .ExcludeFromDescription()
            .RequirePermission(AnswerQuery)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{id}/close", CloseQueryAsync)
            .WithName("CloseClinicalQuery")
            .Produces<ClinicalQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ManageQuery)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{id}/reopen", ReopenQueryAsync)
            .WithName("ReopenClinicalQuery")
            .Produces<ClinicalQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ManageQuery)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{id}/cancel", CancelQueryAsync)
            .WithName("CancelClinicalQuery")
            .Produces<ClinicalQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(ManageQuery)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/batch-close", BatchCloseQueryAsync)
            .WithName("BatchCloseClinicalQueries")
            .Produces<BatchQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ManageQuery)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/batch-cancel", BatchCancelQueryAsync)
            .WithName("BatchCancelClinicalQueries")
            .Produces<BatchQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(ManageQuery)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/batch-respond", BatchRespondQueryAsync)
            .WithName("BatchRespondClinicalQueries")
            .Produces<BatchQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequirePermission(AnswerQuery)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        // Undocumented alias for legacy Python router compatibility. The gateway proxies it, so every alias carries the
        // same filters as its documented route: a change without X-Change-Reason is refused here too (#5060).
        var legacyGroup = app.MapGroup("/api/v1/queries")
            .ExcludeFromDescription();

        legacyGroup.MapPost("/", CreateQueryAsync).RequirePermission(RaiseQuery);
        legacyGroup.MapGet("/{id}", GetQueryByIdAsync).RequirePermission(ReadQueries);
        legacyGroup.MapGet("/", ListQueriesAsync).RequirePermission(ReadQueries);
        legacyGroup.MapPost("/{id}/respond", AnswerQueryAsync).RequirePermission(AnswerQuery).AddEndpointFilter<ChangeReasonHeaderFilter>();
        legacyGroup.MapPost("/{id}/close", CloseQueryAsync).RequirePermission(ManageQuery).AddEndpointFilter<ChangeReasonHeaderFilter>();
        legacyGroup.MapPost("/{id}/reopen", ReopenQueryAsync).RequirePermission(ManageQuery).AddEndpointFilter<ChangeReasonHeaderFilter>();
        legacyGroup.MapPost("/{id}/cancel", CancelQueryAsync).RequirePermission(ManageQuery).AddEndpointFilter<ChangeReasonHeaderFilter>();
        legacyGroup.MapPost("/batch-close", BatchCloseQueryAsync).RequirePermission(ManageQuery).AddEndpointFilter<ChangeReasonHeaderFilter>();
        legacyGroup.MapPost("/batch-cancel", BatchCancelQueryAsync).RequirePermission(ManageQuery).AddEndpointFilter<ChangeReasonHeaderFilter>();
        legacyGroup.MapPost("/batch-respond", BatchRespondQueryAsync).RequirePermission(AnswerQuery).AddEndpointFilter<ChangeReasonHeaderFilter>();

        return app;
    }

    public static async Task<IResult> CreateQueryAsync(
        QueryCreateRequest request,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.CreateAsync(request, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: r => Results.Created($"/api/v1/execution/queries/{r.Id}", r),
            onFailure: e => e.ToProblemResult());
    }

    public static async Task<IResult> GetQueryByIdAsync(
        string id,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> ListQueriesAsync(
        string? study_id,
        string? subject_id,
        string? visit_id,
        string? status,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.ListAsync(study_id, subject_id, visit_id, status, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> AnswerQueryAsync(
        string id,
        QueryRespondRequest request,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.AnswerAsync(id, request, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> CloseQueryAsync(
        string id,
        HttpContext httpContext,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var role = GetCallerRole(httpContext);
        var result = await svc.CloseAsync(id, ct, role).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> ReopenQueryAsync(
        string id,
        QueryReopenRequest request,
        HttpContext httpContext,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var role = GetCallerRole(httpContext);
        var result = await svc.ReopenAsync(id, request, ct, role).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> CancelQueryAsync(
        string id,
        QueryCancelRequest request,
        HttpContext httpContext,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var role = GetCallerRole(httpContext);
        var result = await svc.CancelAsync(id, request, ct, role).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> BatchCloseQueryAsync(
        BatchQueryRequest request,
        HttpContext httpContext,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var role = GetCallerRole(httpContext);
        var result = await svc.BatchCloseAsync(request, ct, role).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> BatchCancelQueryAsync(
        BatchQueryRequest request,
        HttpContext httpContext,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var role = GetCallerRole(httpContext);
        var result = await svc.BatchCancelAsync(request, ct, role).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> BatchRespondQueryAsync(
        BatchQueryRespondRequest request,
        IClinicalQueryApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.BatchRespondAsync(request, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    /// <summary>
    /// The role the caller acts in: the first of their signed roles that may manage query lifecycle, or
    /// their first role when none can (so the denial names a real role). Never read from a request header.
    /// </summary>
    private static string? GetCallerRole(HttpContext httpContext) =>
        httpContext.User.ToAuthenticatedCaller() is { } caller
            ? caller.FirstRoleMatching(ClinicalQueryApplicationService.CanManageQueryLifecycle) ?? caller.Roles.FirstOrDefault()
            : null;
}
