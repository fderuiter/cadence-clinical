using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Evaluation.Sets;

namespace Cadence.Modules.Intelligence.Evaluation;

/// <summary>What the agent did with a case.</summary>
public enum AgentOutcome
{
    /// <summary>It drafted a Proposal.</summary>
    Proposed,

    /// <summary>It declined because the data does not support an answer.</summary>
    Abstained,

    /// <summary>It declined because the request is outside its task.</summary>
    Refused,
}

/// <summary>
/// The structured output of one agent run on one case: the draft that would become a Proposal, before the citation
/// verifier drops anything, so the evaluation sees what the model actually produced.
/// </summary>
/// <param name="Outcome">Proposed, abstained or refused.</param>
/// <param name="Fields">The output schema's fields and their values.</param>
/// <param name="Citations">The node references the output cites.</param>
/// <param name="Narrative">Free text the output carries (a query text, a narrative), if the task has one.</param>
/// <param name="Usage">Tokens, cost and latency of the run.</param>
public sealed record AgentOutput(
    AgentOutcome Outcome,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> Citations,
    string? Narrative,
    RunUsage Usage);

/// <summary>What a run cost.</summary>
/// <param name="InputTokens">Prompt tokens.</param>
/// <param name="OutputTokens">Completion tokens.</param>
/// <param name="Cost">Cost in the deployment's accounting currency.</param>
/// <param name="Latency">Wall time from request to structured output.</param>
public sealed record RunUsage(int InputTokens, int OutputTokens, decimal Cost, TimeSpan Latency)
{
    /// <summary>A run that cost nothing, as the fake provider reports.</summary>
    public static RunUsage None { get; } = new(0, 0, 0m, TimeSpan.Zero);
}

/// <summary>
/// The configuration under evaluation, as the harness runs it. The Intelligence runtime adapts a task configuration and
/// a model provider to this; tests script it. In CI the provider behind it is always the deterministic fake.
/// </summary>
public interface IEvaluationTarget
{
    /// <summary>The configuration version this target runs.</summary>
    ConfigurationRef Configuration { get; }

    /// <summary>The provider and model behind the run.</summary>
    ProviderInfo Provider { get; }

    /// <summary>Runs the configuration on one case, reading only the case's context nodes.</summary>
    Task<AgentOutput> RunAsync(EvaluationCase evaluationCase, CancellationToken cancellationToken);
}

/// <summary>
/// The exact task configuration version a run, an outcome or a signal belongs to (ADR-2631): its key, version and the
/// content hash qualification attests.
/// </summary>
/// <param name="Key">The configuration key, such as <c>data-review.query-draft</c>.</param>
/// <param name="Version">The configuration version.</param>
/// <param name="ContentHash">The kernel content hash of the configuration file.</param>
public sealed record ConfigurationRef(string Key, int Version, string ContentHash)
{
    /// <inheritdoc />
    public override string ToString() => $"{Key} v{Version}";
}

/// <summary>The model provider behind a run.</summary>
/// <param name="Name">Provider name, <c>fake</c> for the deterministic test provider.</param>
/// <param name="ModelId">The model id the provider reported.</param>
public sealed record ProviderInfo(string Name, string ModelId)
{
    /// <summary>The provider name the deterministic fake reports.</summary>
    public const string FakeName = "fake";

    /// <summary>True for the deterministic fake provider CI uses.</summary>
    public bool IsFake => string.Equals(Name, FakeName, StringComparison.Ordinal);
}
