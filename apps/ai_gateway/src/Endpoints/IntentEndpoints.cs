using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

using Cadence.Apps.AiGateway.Services;
using Cadence.Contracts;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.AiGateway.Endpoints;

/// <summary>
/// Minimal API endpoints for AI Gateway intent classification and natural language route navigation.
/// </summary>
public static class IntentEndpoints
{
    /// <summary>
    /// Maps the intent routing endpoints on the web application.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The updated route builder.</returns>
    public static IEndpointRouteBuilder MapIntentEndpoints(this IEndpointRouteBuilder app)
    {
        var groupAi = app.MapGroup("/api/ai/intent");
        var groupV1Ai = app.MapGroup("/api/v1/ai/intent");

        groupAi.MapPost("/route", RouteIntentAsync)
            .WithName("RouteAiIntent")
            .WithSummary("Classify natural language prompt queries into structured clinical intent actions.")
            .Produces<IntentRouteResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        groupV1Ai.MapPost("/route", RouteIntentAsync)
            .WithName("RouteV1AiIntent")
            .WithSummary("Classify natural language prompt queries into structured clinical intent actions (v1 API).")
            .Produces<IntentRouteResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static IResult RouteIntentAsync(
        IntentRouteRequest request,
        ClaimsPrincipal user,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Cadence.Apps.AiGateway.IntentRouting");
        if (request == null || string.IsNullOrWhiteSpace(request.Query))
        {
            return TypedResults.Ok(new IntentRouteResponse(request?.Query ?? string.Empty, Array.Empty<IntentRecommendation>()));
        }

        string userId = user.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "anonymous_user";

        // Collect user roles from ClaimsPrincipal
        var userRoles = user.FindAll(ClaimTypes.Role)
            .Concat(user.FindAll("role"))
            .Concat(user.FindAll("roles"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Also check if context supplied explicit roles
        if (request.Context != null && request.Context.TryGetValue("roles", out var contextRolesStr) && !string.IsNullOrWhiteSpace(contextRolesStr))
        {
            var ctxRoles = contextRolesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            userRoles.AddRange(ctxRoles);
            userRoles = userRoles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var response = IntentRoutingService.ClassifyIntent(request.Query, userRoles, request.Context);

        logger.LogInformation(
            "GxP Intent Routing: User {UserId} query '{Query}' resolved {Count} intent recommendations.",
            userId,
            request.Query,
            response.Recommendations.Count);

        return TypedResults.Ok(response);
    }
}
