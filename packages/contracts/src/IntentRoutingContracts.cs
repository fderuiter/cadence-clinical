using System;
using System.Collections.Generic;

namespace Cadence.Contracts;

/// <summary>
/// Request payload for routing natural language prompts to structured clinical intent actions.
/// </summary>
/// <param name="Query">The natural language prompt submitted by the user.</param>
/// <param name="Context">Optional contextual key-value metadata (e.g. current route, study ID, site ID).</param>
public sealed record IntentRouteRequest(
    string Query,
    IReadOnlyDictionary<string, string>? Context = null
);

/// <summary>
/// Structured intent recommendation returned by the AI Gateway intent router.
/// </summary>
/// <param name="Id">Unique identifier of the intent recommendation.</param>
/// <param name="Name">Short title or name of the action/destination.</param>
/// <param name="Description">Detailed description of what selecting this item achieves.</param>
/// <param name="Category">Category label badge (e.g. "[AI Copilot]").</param>
/// <param name="Icon">Visual icon or symbol representing the action.</param>
/// <param name="TargetRoute">Target router navigation path with optional query parameters.</param>
/// <param name="Action">Optional modal or client action identifier.</param>
/// <param name="Confidence">Confidence score between 0.0 and 1.0.</param>
/// <param name="Roles">List of user roles authorized to view or execute this intent.</param>
public sealed record IntentRecommendation(
    string Id,
    string Name,
    string Description,
    string Category,
    string Icon,
    string TargetRoute,
    string? Action,
    double Confidence,
    IReadOnlyList<string> Roles
);

/// <summary>
/// Response payload from the AI Gateway intent router service.
/// </summary>
/// <param name="Query">The query that was evaluated.</param>
/// <param name="Recommendations">List of classified intent recommendations.</param>
public sealed record IntentRouteResponse(
    string Query,
    IReadOnlyList<IntentRecommendation> Recommendations
);
