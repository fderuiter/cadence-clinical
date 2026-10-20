using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
/// One version of the configuration that runs an agent task (ADR-2630 §4.3, K-AI-5): the provider and model (or a
/// model tier the provider maps), the instructions template and the output limit. The qualified unit is this whole
/// configuration, never the model id: <see cref="ContentHash"/> changes when any field changes. Versioned files,
/// evaluation sets and qualification come with #5385 (ADR-2631).
/// </summary>
/// <param name="Key">The task, lower snake case segments joined by dots, for example <c>data_review.query_draft</c>.</param>
/// <param name="Version">The configuration's version, from 1.</param>
/// <param name="Provider">The model provider's name, for example <c>anthropic</c>.</param>
/// <param name="Model">A model id, or a tier (<c>tier:deep</c>, <c>tier:balanced</c>, <c>tier:fast</c>) the provider maps.</param>
/// <param name="Instructions">The system instructions, a <see cref="PromptTemplate"/>.</param>
/// <param name="MaxOutputTokens">The most tokens a run may generate.</param>
/// <param name="Level">The highest autonomy level the task may use.</param>
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

    /// <summary>The SHA-256 of the configuration's canonical JSON, lower-case hex.</summary>
    public string ContentHash => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        new { Key, Version, Provider, Model, Instructions, MaxOutputTokens, Level = (int)Level }))));

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
        ];
    }

    /// <summary>Task keys: lower snake case segments joined by dots, at most 96 characters.</summary>
    [GeneratedRegex(@"^(?=.{1,96}\z)[a-z][a-z0-9]*(?:_[a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*)*\z", RegexOptions.CultureInvariant)]
    internal static partial Regex KeyPattern();
}
