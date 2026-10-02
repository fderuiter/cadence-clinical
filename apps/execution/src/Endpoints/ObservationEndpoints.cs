using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints.Filters;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Execution.Endpoints;

/// <summary>
/// Minimal API route definitions for discrete clinical observations and SDV operations.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-042
/// @req:PRD-SYS-121
/// @req:SRS-EDC-001
/// </summary>
public static class ObservationEndpoints
{
    public static IEndpointRouteBuilder MapObservationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/execution/observations")
            .WithTags("Observations");

        group.MapPost("/", CreateObservationAsync)
            .WithName("CreateClinicalObservation")
            .Produces<ObservationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id}", GetObservationByIdAsync)
            .WithName("GetObservationById")
            .Produces<ObservationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/subject/{studyId}/{subjectId}", ListObservationsBySubjectAsync)
            .WithName("ListObservationsBySubject")
            .Produces<IReadOnlyList<ObservationResponse>>(StatusCodes.Status200OK);

        // SDV is refused (403) unless the caller holds a verification role, and carries a reason for change.
        group.MapPost("/{id}/sdv", VerifyObservationSdvAsync)
            .WithName("VerifyObservationSdv")
            .Produces<ObservationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        group.MapPost("/{id}/sdv/flag", FlagObservationSdvAsync)
            .WithName("FlagObservationSdv")
            .Produces<ObservationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AddEndpointFilter<ChangeReasonHeaderFilter>();

        // Undocumented alias for legacy Python router compatibility
        var legacyGroup = app.MapGroup("/api/v1/observations")
            .ExcludeFromDescription();

        legacyGroup.MapPost("/", CreateObservationAsync);
        legacyGroup.MapGet("/{id}", GetObservationByIdAsync);
        legacyGroup.MapPatch("/{id}/sdv", VerifyObservationSdvAsync).AddEndpointFilter<ChangeReasonHeaderFilter>();
        legacyGroup.MapPost("/{id}/sdv", VerifyObservationSdvAsync).AddEndpointFilter<ChangeReasonHeaderFilter>();

        return app;
    }

    public static async Task<IResult> CreateObservationAsync(
        ObservationCreateRequest request,
        IObservationApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.CreateAsync(request, ct).ConfigureAwait(false);
        return result.Match(
            onSuccess: r => Results.Created($"/api/v1/execution/observations/{r.Id}", r),
            onFailure: e => e.ToProblemResult());
    }

    public static async Task<IResult> GetObservationByIdAsync(
        string id,
        IObservationApplicationService svc,
        CancellationToken ct,
        [FromQuery(Name = "include_inactive")] bool includeInactive = false)
    {
        var result = await svc.GetByIdAsync(id, includeInactive, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> ListObservationsBySubjectAsync(
        string studyId,
        string subjectId,
        IObservationApplicationService svc,
        CancellationToken ct,
        [FromQuery(Name = "include_inactive")] bool includeInactive = false)
    {
        var result = await svc.ListBySubjectAsync(studyId, subjectId, includeInactive, ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> VerifyObservationSdvAsync(
        string id,
        HttpContext httpContext,
        IObservationApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.VerifySdvAsync(id, httpContext.User.ToAuthenticatedCaller(), ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }

    public static async Task<IResult> FlagObservationSdvAsync(
        string id,
        FlagObservationRequest request,
        HttpContext httpContext,
        IObservationApplicationService svc,
        CancellationToken ct)
    {
        var result = await svc.FlagSdvAsync(id, request.Reason, httpContext.User.ToAuthenticatedCaller(), ct).ConfigureAwait(false);
        return result.Match(Results.Ok, e => e.ToProblemResult());
    }
}

public sealed record FlagObservationRequest(string Reason);
