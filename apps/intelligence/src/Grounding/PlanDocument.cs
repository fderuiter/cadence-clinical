using System.Collections.Generic;
using System.Text.Json;

namespace Cadence.Modules.Intelligence.Grounding;

/// <summary>
/// The plan a Proposal points at (ADR-2633 §4.7, ADR-2489): a canonical JSON record of what the agent proposes and
/// why, with the model and task configuration version that produced it (K-AI-5). The approver's attestation covers its
/// hash, so what a person signs is exactly what is stored. Dropped items appear by reason and position only, so text
/// the verifier refused never reaches the reviewer.
/// </summary>
public sealed record PlanDocument(
    string Schema,
    Guid RunId,
    string Agent,
    string Requester,
    string Study,
    string Intent,
    PlanModel Model,
    PlanConfiguration Configuration,
    IReadOnlyList<PlanClaim> Claims,
    IReadOnlyList<string> Affected,
    IReadOnlyList<string> RequestedCapabilities,
    IReadOnlyList<PlanDroppedItem> Dropped,
    IReadOnlyList<PlanToolCall> ToolCalls,
    DateTimeOffset CreatedAt)
{
    /// <summary>The plan schema this writer produces.</summary>
    public const string CurrentSchema = "cadence.agent-plan/1";

    /// <summary>The media type of a stored plan.</summary>
    public const string MediaType = "application/vnd.cadence.agent-plan+json";
}

/// <summary>The model that produced the output.</summary>
public sealed record PlanModel(string Id);

/// <summary>The task configuration version the run used (ADR-2631).</summary>
public sealed record PlanConfiguration(string Id, int Version, string ContentHash);

/// <summary>A verified claim with its citations and quoted values.</summary>
public sealed record PlanClaim(string Text, IReadOnlyList<string> Citations, IReadOnlyList<PlanValue> Values);

/// <summary>A verified value, as the run was shown it.</summary>
public sealed record PlanValue(string Node, JsonElement Value);

/// <summary>An item the verifier dropped, without its text.</summary>
public sealed record PlanDroppedItem(DroppedItemKind Kind, int Index, DropReason Reason);

/// <summary>A tool call the run made.</summary>
public sealed record PlanToolCall(string Tool, string? ErrorCode, DateTimeOffset StartedAt, DateTimeOffset FinishedAt);
