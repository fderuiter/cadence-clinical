using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Interop.Endpoints;

public static class EsourceReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapEsourceReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/interop/esource")
            .WithTags("eSource Reconciliation & SMART on FHIR");

        group.MapGet("/candidates", ListCandidatesAsync)
            .WithName("ListCandidates")
            .RequirePermission("esource_candidate:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrCandidateListResponse>(StatusCodes.Status200OK);

        group.MapGet("/candidates/{candidate_id}", GetCandidateByIdAsync)
            .WithName("GetCandidate")
            .RequirePermission("esource_candidate:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrCandidateItemDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/reconcile/accept", AcceptCandidateAsync)
            .WithName("AcceptCandidate")
            .RequirePermission("esource_candidate:reconcile").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature("interop.esource.accept", http => http.Request.Path.Value ?? string.Empty);

        group.MapPost("/reconcile/batch-accept", BatchAcceptCandidatesAsync)
            .WithName("BatchAcceptCandidates")
            .RequirePermission("esource_candidate:reconcile").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileBatchActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature("interop.esource.batch_accept", http => http.Request.Path.Value ?? string.Empty);

        group.MapPost("/reconcile/reject", RejectCandidateAsync)
            .WithName("RejectCandidate")
            .RequirePermission("esource_candidate:reconcile").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature("interop.esource.reject", http => http.Request.Path.Value ?? string.Empty);

        group.MapPost("/reconcile/batch-reject", BatchRejectCandidatesAsync)
            .WithName("BatchRejectCandidates")
            .RequirePermission("esource_candidate:reconcile").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileBatchActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireElectronicSignature("interop.esource.batch_reject", http => http.Request.Path.Value ?? string.Empty);

        group.MapPost("/reconcile/query", RaiseClinicalQueryAsync)
            .WithName("RaiseClinicalQuery")
            .RequirePermission("esource_candidate:query").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrReconcileActionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/provenance/{candidate_id}", GetProvenanceAuditAsync)
            .WithName("GetProvenanceAudit")
            .RequirePermission("esource_candidate:read").RequireCallerStudyAndSiteScope()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces<EhrProvenanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public static Task<IResult> ListCandidatesAsync(
        string? study_id,
        string? subject_id,
        string? site_id,
        string? domain,
        string? status,
        int skip,
        int limit,
        EsourceIngestionAndQueryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.ListCandidatesAsync(study_id, subject_id, site_id, domain, status, skip, limit, httpContext, ct);

    public static Task<IResult> GetCandidateByIdAsync(
        string candidate_id,
        EsourceIngestionAndQueryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetCandidateByIdAsync(candidate_id, httpContext, ct);

    public static Task<IResult> AcceptCandidateAsync(
        EhrReconcileAcceptRequest request,
        EsourceReconciliationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.AcceptCandidateAsync(request, httpContext, ct);

    public static Task<IResult> BatchAcceptCandidatesAsync(
        EhrReconcileBatchAcceptRequest request,
        EsourceReconciliationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.BatchAcceptCandidatesAsync(request, httpContext, ct);

    public static Task<IResult> RejectCandidateAsync(
        EhrReconcileRejectRequest request,
        EsourceReconciliationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.RejectCandidateAsync(request, httpContext, ct);

    public static Task<IResult> BatchRejectCandidatesAsync(
        EhrReconcileBatchRejectRequest request,
        EsourceReconciliationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.BatchRejectCandidatesAsync(request, httpContext, ct);

    public static Task<IResult> RaiseClinicalQueryAsync(
        EhrReconcileQueryRequest request,
        EsourceReconciliationService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.RaiseClinicalQueryAsync(request, httpContext, ct);

    public static Task<IResult> GetProvenanceAuditAsync(
        string candidate_id,
        EsourceIngestionAndQueryService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        service.GetProvenanceAuditAsync(candidate_id, httpContext, ct);
}
