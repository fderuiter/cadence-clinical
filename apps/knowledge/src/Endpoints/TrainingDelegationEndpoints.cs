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

namespace Cadence.Apps.Knowledge.Endpoints;

public static class TrainingDelegationEndpoints
{
    public const string DelegationSignOffAction = "/api/v1/training/delegations/bulk-signoff";
    public const string DelegationSignOffSemanticAction = "knowledge.delegation.sign_off";

    public static IEndpointRouteBuilder MapTrainingDelegationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/training")
            .WithTags("Study Training & Gating");

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

    private static SigTokenBinding? DelegationSignOffBinding(EndpointFilterInvocationContext context) =>
        context.Arguments.OfType<BulkDelegationSignOffRequest>().FirstOrDefault() is { DelegationIds.Count: > 0 } request
            ? new SigTokenBinding(DelegationSignOffAction, DelegationSignOffSemanticAction, request.BatchBindingId())
            : null;

    private static Task<IResult> ListDelegationsAsync(
        string? study_id,
        string? site_id,
        string? status,
        [FromServices] ITrainingDelegationService delegationService,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        DateTimeOffset? valid_at = null,
        DateTimeOffset? known_at = null) =>
        delegationService.ListDelegationsAsync(study_id, site_id, status, httpContext, cancellationToken, valid_at, known_at);

    private static Task<IResult> BulkSignOffDelegationsAsync(
        BulkDelegationSignOffRequest request,
        HttpContext httpContext,
        [FromServices] ITrainingDelegationService delegationService,
        CancellationToken cancellationToken) =>
        delegationService.BulkSignOffDelegationsAsync(request, httpContext, cancellationToken);
}
