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
        var group = app.MapGroup("/api/v1/ai/intent").RequireAuthorization();

        group.MapPost("/route", RouteIntentAsync)
            .WithName("RouteAiIntent")
            .WithSummary("Match a command-palette query against fixed patterns and return the navigation targets the caller's roles allow.")
            .Produces<IntentRouteResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    internal static IResult RouteIntentAsync(
        IntentRouteRequest request,
        ClaimsPrincipal user,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Cadence.Apps.AiGateway.IntentRouting");

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
