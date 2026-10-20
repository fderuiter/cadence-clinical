using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

using Cadence.Modules.Intelligence.Intent;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cadence.Modules.Intelligence.Endpoints;

/// <summary>
/// Minimal API endpoints for the command palette's deterministic navigation router. Requests need an
/// authenticated caller; each recommendation is filtered by the caller's verified role claims. No model
/// is called and nothing is written.
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
        // Any signed-in caller: the router reads no record and writes nothing, and filters every recommendation by the
        // caller's signed roles (ADR-2516).
        var group = app.MapGroup("/api/v1/ai/intent")
            .RequireSignedInCaller("Matches fixed patterns, reads and writes no record, and filters recommendations by the caller's signed roles.");

        group.MapPost("/route", RouteIntentAsync)
            .WithName("RouteAiIntent")
            .WithSummary("Match a command-palette query against fixed patterns and return the navigation targets the caller's roles allow.")
            .Produces<IntentRouteResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    internal static IResult RouteIntentAsync(
        IntentRouteRequest request,
        ClaimsPrincipal user,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Cadence.Modules.Intelligence.IntentRouting");

        if (user?.Identity?.IsAuthenticated != true)
        {
            return TypedResults.Unauthorized();
        }

        string? userId = user.FindFirst(CadenceClaimTypes.Subject)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return TypedResults.Unauthorized();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Query))
        {
            return TypedResults.Ok(new IntentRouteResponse(request?.Query ?? string.Empty, Array.Empty<IntentRecommendation>()));
        }

        // Collect user roles strictly from verified ClaimsPrincipal claims
        var userRoles = user.FindAll(ClaimTypes.Role)
            .Concat(user.FindAll("role"))
            .Concat(user.FindAll("roles"))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var response = RuleBasedIntentRouter.ClassifyIntent(request.Query, userRoles, request.Context);

        // The query is free text a user may fill with participant details, so only its length is logged.
        logger.LogInformation(
            "Intent routing: user {UserId} sent a {QueryLength}-character query that matched {Count} navigation recommendations.",
            userId,
            request.Query.Length,
            response.Recommendations.Count);

        return TypedResults.Ok(response);
    }
}
