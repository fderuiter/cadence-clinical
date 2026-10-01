using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Locks;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API route definitions for 6-tier data lock governance and automated pre-lock discrepancy validation.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-007
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// @req:SRS-EDC-004
/// </summary>
public static class LockEndpoints
{
    public static IEndpointRouteBuilder MapLockEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/locks")
            .WithTags("Data Lock Governance");

        // Pre-flight check endpoint
        group.MapPost("/pre-flight", RunPreflightCheckAsync)
            .WithName("PreflightLockCheck")
            .Produces<PreflightCheckResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/preflight", RunPreflightCheckAsync)
            .ExcludeFromDescription();

        // Main lock/freeze execution endpoint
        group.MapPost("/lock", ExecuteLockAsync)
            .WithName("LockData")
            .Produces<DataLockRecordResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Main unlock execution endpoint
        group.MapPost("/unlock", ExecuteUnlockAsync)
            .WithName("UnlockData")
            .Produces<DataLockRecordResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // List active data locks
        group.MapGet("/", ListLocksAsync)
            .WithName("ListLocks")
            .Produces<IReadOnlyList<DataLockRecordResponse>>(StatusCodes.Status200OK);

        // Tier-specific shortcut routes
        // Site
        group.MapPost("/site/{site_id}/freeze", (string site_id, DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("SITE", site_id, "FREEZE", req, ctx, svc, ct));
        group.MapPost("/site/{site_id}/lock", (string site_id, DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("SITE", site_id, "HARD_LOCK", req, ctx, svc, ct));
        group.MapPost("/site/{site_id}/unfreeze", (string site_id, DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("SITE", site_id, req, ctx, svc, ct));
        group.MapPost("/site/{site_id}/unlock", (string site_id, DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("SITE", site_id, req, ctx, svc, ct));

        // Visit
        group.MapPost("/visit/{visit_id}/freeze", (string visit_id, DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("VISIT", visit_id, "FREEZE", req, ctx, svc, ct));
        group.MapPost("/visit/{visit_id}/lock", (string visit_id, DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("VISIT", visit_id, "HARD_LOCK", req, ctx, svc, ct));
        group.MapPost("/visit/{visit_id}/unfreeze", (string visit_id, DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("VISIT", visit_id, req, ctx, svc, ct));
        group.MapPost("/visit/{visit_id}/unlock", (string visit_id, DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("VISIT", visit_id, req, ctx, svc, ct));

        // Form
        group.MapPost("/form/{form_id}/freeze", (string form_id, DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("FORM", form_id, "FREEZE", req, ctx, svc, ct));
        group.MapPost("/form/{form_id}/lock", (string form_id, DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("FORM", form_id, "HARD_LOCK", req, ctx, svc, ct));
        group.MapPost("/form/{form_id}/unfreeze", (string form_id, DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("FORM", form_id, req, ctx, svc, ct));
        group.MapPost("/form/{form_id}/unlock", (string form_id, DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("FORM", form_id, req, ctx, svc, ct));

        // Subject
        group.MapPost("/subject/{subject_id}/freeze", (string subject_id, DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("SUBJECT", subject_id, "FREEZE", req, ctx, svc, ct));
        group.MapPost("/subject/{subject_id}/lock", (string subject_id, DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("SUBJECT", subject_id, "HARD_LOCK", req, ctx, svc, ct));
        group.MapPost("/subject/{subject_id}/unfreeze", (string subject_id, DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("SUBJECT", subject_id, req, ctx, svc, ct));
        group.MapPost("/subject/{subject_id}/unlock", (string subject_id, DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("SUBJECT", subject_id, req, ctx, svc, ct));

        // Trial / Study
        group.MapPost("/trial/freeze", (DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("STUDY", req?.StudyId ?? "STUDY-001", "FREEZE", req, ctx, svc, ct));
        group.MapPost("/trial/lock", (DataLockExecuteRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutLockAsync("STUDY", req?.StudyId ?? "STUDY-001", "HARD_LOCK", req, ctx, svc, ct));
        group.MapPost("/trial/unfreeze", (DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("STUDY", req?.ScopeId ?? "STUDY-001", req, ctx, svc, ct));
        group.MapPost("/trial/unlock", (DataLockUnlockRequest? req, HttpContext ctx, IDataLockApplicationService svc, CancellationToken ct) =>
            ExecuteShortcutUnlockAsync("STUDY", req?.ScopeId ?? "STUDY-001", req, ctx, svc, ct));

        return app;
    }

    public static async Task<IResult> RunPreflightCheckAsync(
        PreflightCheckRequest request,
        IDataLockApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.PreflightCheckAsync(request, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> ExecuteLockAsync(
        DataLockExecuteRequest request,
        HttpContext httpContext,
        IDataLockApplicationService svc,
        CancellationToken ct)
    {
        var callerUserId = GetCallerUserId(httpContext);
        var sigTokenHeader = httpContext.Request.Headers["X-Sig-Token"].FirstOrDefault();

        var effectiveReq = request with { SigToken = request.SigToken ?? sigTokenHeader };
        var result = await svc.LockAsync(effectiveReq, callerUserId, ct).ConfigureAwait(false);

        return result.Match(
            onSuccess: r => Results.Ok(new { status = "SUCCESS", lock_id = r.LockId, record = r }),
            onFailure: e => e.ToProblemResult());
    }

    public static async Task<IResult> ExecuteUnlockAsync(
        DataLockUnlockRequest request,
        HttpContext httpContext,
        IDataLockApplicationService svc,
        CancellationToken ct)
    {
        var callerUserId = GetCallerUserId(httpContext);
        var result = await svc.UnlockAsync(request, callerUserId, ct).ConfigureAwait(false);

        return result.Match(
            onSuccess: r => Results.Ok(new { status = "SUCCESS", lock_id = r.LockId, record = r }),
            onFailure: e => e.ToProblemResult());
    }

    public static async Task<IResult> ListLocksAsync(
        string? study_id,
        string? scope_type,
        string? scope_id,
        IDataLockApplicationService svc,
        CancellationToken ct)
    {
        var locks = await svc.ListLocksAsync(study_id, scope_type, scope_id, ct).ConfigureAwait(false);
        return Results.Ok(locks);
    }

    private static async Task<IResult> ExecuteShortcutLockAsync(
        string scopeType,
        string scopeId,
        string defaultAction,
        DataLockExecuteRequest? request,
        HttpContext httpContext,
        IDataLockApplicationService svc,
        CancellationToken ct)
    {
        var callerUserId = GetCallerUserId(httpContext);
        var sigTokenHeader = httpContext.Request.Headers["X-Sig-Token"].FirstOrDefault();

        var effectiveReq = request ?? new DataLockExecuteRequest(
            ScopeType: scopeType,
            ScopeId: scopeId,
            StudyId: "STUDY-001",
            Action: defaultAction,
            LockType: defaultAction,
            ReasonForChange: $"Lock operation for {scopeType}:{scopeId}"
        );

        if (effectiveReq.ScopeType != scopeType || effectiveReq.ScopeId != scopeId)
        {
            effectiveReq = effectiveReq with { ScopeType = scopeType, ScopeId = scopeId };
        }

        if (string.IsNullOrWhiteSpace(effectiveReq.SigToken) && !string.IsNullOrWhiteSpace(sigTokenHeader))
        {
            effectiveReq = effectiveReq with { SigToken = sigTokenHeader };
        }

        var result = await svc.LockAsync(effectiveReq, callerUserId, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: r => Results.Ok(new { status = "SUCCESS", lock_id = r.LockId, record = r }),
            onFailure: e => e.ToProblemResult());
    }

    private static async Task<IResult> ExecuteShortcutUnlockAsync(
        string scopeType,
        string scopeId,
        DataLockUnlockRequest? request,
        HttpContext httpContext,
        IDataLockApplicationService svc,
        CancellationToken ct)
    {
        var callerUserId = GetCallerUserId(httpContext);

        var effectiveReq = request ?? new DataLockUnlockRequest(
            LockId: null,
            ScopeType: scopeType,
            ScopeId: scopeId,
            Justification: "Mandatory override justification required for unlocking data scope.",
            ReasonForChange: "Unlock override requested"
        );

        if (effectiveReq.ScopeType != scopeType || effectiveReq.ScopeId != scopeId)
        {
            effectiveReq = effectiveReq with { ScopeType = scopeType, ScopeId = scopeId };
        }

        var result = await svc.UnlockAsync(effectiveReq, callerUserId, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: r => Results.Ok(new { status = "SUCCESS", lock_id = r.LockId, record = r }),
            onFailure: e => e.ToProblemResult());
    }

    private static string GetCallerUserId(HttpContext httpContext) =>
        httpContext.User.ToAuthenticatedCaller()?.UserId ?? "Data Manager";
}
