using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Evaluation.Runs;
using Cadence.Modules.Intelligence.Evaluation.Sets;

namespace Cadence.Modules.Intelligence.Domain;

/// <summary>How far an agent may act on its own (ADR-2630 §4.1).</summary>
public enum AutonomyLevel
{
    /// <summary>Answers, explains and summarizes with citations; writes nothing.</summary>
    Inform = 0,

    /// <summary>Writes a Proposal holding a draft a person edits, approves and executes.</summary>
    Draft = 1,

    /// <summary>Writes a Proposal holding exact commands that run as the approver when a person approves (K-AI-2).</summary>
    ApproveToExecute = 2,

    /// <summary>Acts alone inside a narrow, time-boxed standing grant, for operational commands only (K-AI-7).</summary>
    StandingGrant = 3,
}

/// <summary>
/// One version of the configuration that runs an agent task: the qualified unit (ADR-2630 §4.3, ADR-2631, K-AI-5).
/// The positional fields say how the task runs; the init-only fields say what it reads and uses, how its output is
/// judged and how much rests on it. <see cref="ContentHash"/> covers every field, so changing the prompt, the model,
/// a tool, a retrieval source, the evaluation set or the acceptance criteria makes a different configuration that
/// must be qualified again. A published version never changes: a change is a new version.
/// </summary>
/// <param name="Key">The task, lower snake case segments joined by dots, for example <c>data_review.query_draft</c>.</param>
/// <param name="Version">The configuration's version, from 1.</param>
/// <param name="Provider">The model provider's name, for example <c>anthropic</c>.</param>
/// <param name="Model">A model id, or a tier (<c>tier:deep</c>, <c>tier:balanced</c>, <c>tier:fast</c>) the provider maps.</param>
/// <param name="Instructions">The system instructions, a <see cref="PromptTemplate"/>.</param>
/// <param name="MaxOutputTokens">The most tokens a run may generate.</param>
/// <param name="Level">The highest autonomy level the task may use; its qualification caps it.</param>
public sealed partial record TaskConfiguration(
    string Key,
    int Version,
    string Provider,
    string Model,
    string Instructions,
    int MaxOutputTokens,
    AutonomyLevel Level)
{
    /// <summary>The prefix of a model tier in <see cref="Model"/>.</summary>
    public const string TierPrefix = "tier:";

    /// <summary>The tools the agent may call, by name: its allow-list (K-AI-3).</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>The sources the agent's grounding may read from, by name (ADR-2630 §4.4).</summary>
    public IReadOnlyList<string> RetrievalSources { get; init; } = [];

    /// <summary>The JSON Schema the answer must match, when the task answers in a structured form.</summary>
    public string? OutputSchema { get; init; }

    /// <summary>The pinned evaluation set the configuration is qualified against.</summary>
    public EvaluationSetRef? EvaluationSet { get; init; }

    /// <summary>What an evaluation run must show for the configuration to be qualified (ADR-2634): pass rates and limits.</summary>
    public AcceptanceCriteria? AcceptanceCriteria { get; init; }

    /// <summary>The role a person needs to approve the task's proposals; required from <see cref="AutonomyLevel.Draft"/>.</summary>
    public string? ApproverRole { get; init; }

    /// <summary>The FDA credibility-assessment fields and the risk class they give.</summary>
    public CredibilityAssessment? Credibility { get; init; }

    /// <summary>This version's identity: key, version and content hash.</summary>
    public ConfigurationRef Ref => new(Key, Version, ContentHash);

    /// <summary>
    /// The kernel content hash (<c>sha256:</c> and hex) of the configuration's canonical JSON, over every field, the
    /// same form as every other record a kernel attestation signs (<see cref="Cadence.Kernel.Trust.ContentHash"/>).
    /// </summary>
    public string ContentHash => Cadence.Kernel.Trust.ContentHash.Compute(JsonSerializer.Serialize(new
    {
        Key,
        Version,
        Provider,
        Model,
        Instructions,
        MaxOutputTokens,
        Level = (int)Level,
        Tools,
        RetrievalSources,
        OutputSchema,
        EvaluationSet,
        AcceptanceCriteria,
        ApproverRole,
        Credibility = Credibility is { } c
            ? new { c.QuestionOfInterest, c.ContextOfUse, ModelInfluence = (int)c.ModelInfluence, DecisionConsequence = (int)c.DecisionConsequence }
            : null,
    }));

    /// <summary>The reasons the configuration is malformed; empty when it is sound.</summary>
    public string[] Problems()
    {
        return [
            .. KeyPattern().IsMatch(Key ?? string.Empty) ? [] : new[] { $"'{Key}' is not a task key (lower snake case segments joined by dots)." },
            .. Version >= 1 ? [] : new[] { "The version starts at 1." },
            .. string.IsNullOrWhiteSpace(Provider) ? new[] { "Name the provider." } : [],
            .. string.IsNullOrWhiteSpace(Model) ? new[] { "Name the model or tier." } : [],
            .. string.IsNullOrWhiteSpace(Instructions) ? new[] { "Give the instructions." } : [],
            .. MaxOutputTokens is >= 1 and <= 128_000 ? [] : new[] { "Allow between 1 and 128000 output tokens." },
            .. Enum.IsDefined(Level) ? [] : new[] { $"{Level} is not an autonomy level." },
            .. (Tools ?? []).Concat(RetrievalSources ?? []).Where(n => !KeyPattern().IsMatch(n ?? string.Empty))
                .Select(n => $"'{n}' is not a tool or source name (lower snake case segments joined by dots)."),
            .. (Tools ?? []).Distinct(StringComparer.Ordinal).Count() == (Tools ?? []).Count ? [] : new[] { "A tool is listed twice." },
        ];
    }

    /// <summary>
    /// What the configuration still lacks before it can be qualified (ADR-2631): a pinned evaluation set, acceptance
    /// criteria, the credibility assessment, and an approver role when its agent writes proposals.
    /// </summary>
    public string[] QualificationGaps()
    {
        return [
            .. EvaluationSet is null ? new[] { "Pin an evaluation set." } : [],
            .. AcceptanceCriteria is null ? new[] { "State the acceptance criteria." } : [],
            .. Credibility is null ? new[] { "Complete the credibility assessment (question of interest, context of use, model influence, decision consequence)." } : Credibility.Problems(),
            .. Level >= AutonomyLevel.Draft && string.IsNullOrWhiteSpace(ApproverRole) ? new[] { "Name the role that approves the task's proposals." } : [],
        ];
    }

    /// <summary>Task, tool and source names: lower snake case segments joined by dots, at most 96 characters.</summary>
    [GeneratedRegex(@"^(?=.{1,96}\z)[a-z][a-z0-9]*(?:_[a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*)*\z", RegexOptions.CultureInvariant)]
    internal static partial Regex KeyPattern();

    /// <summary>The id an attestation targets for one configuration version: derived from its key and version.</summary>
    public static Guid AttestationTargetId(string key, int version)
    {
        ArgumentNullException.ThrowIfNull(key);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"intelligence.TaskConfiguration:{key}:v{version}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}

/// <summary>How much an AI output contributes to a decision, in the FDA credibility framework.</summary>
public enum ModelInfluence
{
    /// <summary>One input among several a person weighs.</summary>
    Low = 1,

    /// <summary>A significant input a person checks.</summary>
    Medium = 2,

    /// <summary>The main or only basis of the decision.</summary>
    High = 3,
}

/// <summary>How bad a wrong decision based on the output would be, in the FDA credibility framework.</summary>
public enum DecisionConsequence
{
    /// <summary>No effect on participant safety or the reliability of trial results.</summary>
    Low = 1,

    /// <summary>Could affect data quality or operations, and is caught downstream.</summary>
    Medium = 2,

    /// <summary>Could affect participant safety or the reliability of trial results.</summary>
    High = 3,
}

/// <summary>The model risk class (FDA, "Considerations for the Use of AI to Support Regulatory Decision-Making").</summary>
public enum ModelRiskClass
{
    /// <summary>Low model risk.</summary>
    Low = 1,

    /// <summary>Medium model risk.</summary>
    Medium = 2,

    /// <summary>High model risk.</summary>
    High = 3,
}

/// <summary>
/// The credibility-assessment fields of a task configuration (ADR-2630 §4.3, ADR-2631): the question the output
/// answers, its context of use, and the model influence and decision consequence that set its risk class. The risk
/// class decides how much evaluation evidence qualification needs.
/// </summary>
/// <param name="QuestionOfInterest">The question the AI output helps answer.</param>
/// <param name="ContextOfUse">What the output is used for, by whom and where in the workflow.</param>
/// <param name="ModelInfluence">How much the output contributes to the decision.</param>
/// <param name="DecisionConsequence">How bad a wrong decision would be.</param>
public sealed record CredibilityAssessment(
    string QuestionOfInterest,
    string ContextOfUse,
    ModelInfluence ModelInfluence,
    DecisionConsequence DecisionConsequence)
{
    /// <summary>The risk class from the influence-by-consequence matrix.</summary>
    public ModelRiskClass RiskClass => (ModelInfluence, DecisionConsequence) switch
    {
        (ModelInfluence.High, DecisionConsequence.High) or (ModelInfluence.Medium, DecisionConsequence.High)
            or (ModelInfluence.High, DecisionConsequence.Medium) => ModelRiskClass.High,
        (ModelInfluence.Low, DecisionConsequence.Low) or (ModelInfluence.Medium, DecisionConsequence.Low)
            or (ModelInfluence.Low, DecisionConsequence.Medium) => ModelRiskClass.Low,
        _ => ModelRiskClass.Medium,
    };

    /// <summary>The reasons the assessment is incomplete; empty when it is complete.</summary>
    public string[] Problems()
    {
        return [
            .. string.IsNullOrWhiteSpace(QuestionOfInterest) ? new[] { "State the question of interest." } : [],
            .. string.IsNullOrWhiteSpace(ContextOfUse) ? new[] { "State the context of use." } : [],
            .. Enum.IsDefined(ModelInfluence) ? [] : new[] { "Rate the model influence." },
            .. Enum.IsDefined(DecisionConsequence) ? [] : new[] { "Rate the decision consequence." },
        ];
    }
}
