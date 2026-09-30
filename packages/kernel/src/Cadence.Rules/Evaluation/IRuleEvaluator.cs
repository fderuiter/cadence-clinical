using System;
using System.Collections.Generic;

using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Evaluation;

/// <summary>
/// Evaluates rule packs over records (spec §10.1, K-RULE-3). This is the one evaluation port: EDC edit checks,
/// standards conformance, ADaM checks and quality indicators all call it, so a replacement engine (the Rust runtime
/// seam) changes one registration and no caller. An implementation must reproduce the semantics documented on
/// <see cref="RuleExpressionCompiler"/> and must be deterministic: the same rule versions over the same records in the
/// same context give the same violations in the same order (K-RULE-4).
/// </summary>
public interface IRuleEvaluator
{
    /// <summary>
    /// Checks every record against every rule of <paramref name="pack"/> that applies in <paramref name="context"/>.
    /// Violations come out in record order, then in the pack's rule order.
    /// </summary>
    IReadOnlyList<RuleViolation> Evaluate(RulePack pack, RuleEvaluationContext context, IEnumerable<RuleRecord> records);
}

/// <summary>
/// What an evaluation is for. A rule applies when its stage equals <see cref="Stage"/>, its effective period contains
/// <see cref="AsOf"/>, and each of its standard, standard version and agency is null or equal to the context's. The
/// caller supplies <see cref="AsOf"/>; evaluation never reads the clock.
/// </summary>
public sealed record RuleEvaluationContext(RuleStage Stage, DateOnly AsOf)
{
    /// <summary>The standard being checked (for example <c>SDTMIG</c>); rules naming another standard do not apply.</summary>
    public string? Standard { get; init; }

    /// <summary>The standard version being checked; rules naming another version do not apply.</summary>
    public string? StandardVersion { get; init; }

    /// <summary>The agency the data is prepared for (for example <c>FDA</c>); rules naming another agency do not apply.</summary>
    public string? Agency { get; init; }
}

/// <summary>
/// One record to check: the dataset (or domain, form or record type) it belongs to, a key that identifies it in the
/// caller's terms (a row number, an observation id, a design element reference), and its variables.
/// </summary>
public sealed class RuleRecord(string dataset, string key, IReadOnlyDictionary<string, RuleValue> values) : IRuleVariables
{
    /// <summary>The dataset, domain, form or record type; compared with a rule's dataset.</summary>
    public string Dataset { get; } = dataset ?? throw new ArgumentNullException(nameof(dataset));

    /// <summary>Identifies the record in the caller's terms; copied to each violation.</summary>
    public string Key { get; } = key ?? throw new ArgumentNullException(nameof(key));

    /// <summary>The record's variables by name.</summary>
    public IReadOnlyDictionary<string, RuleValue> Values { get; } = values ?? throw new ArgumentNullException(nameof(values));

    /// <summary>The value of <paramref name="name"/>, or <see cref="RuleValue.Null"/> when the record has no such variable.</summary>
    public RuleValue this[string name] => Values.TryGetValue(name, out var value) ? value : RuleValue.Null;
}

/// <summary>A record's value of a variable the violated rule reads.</summary>
public sealed record ObservedValue(string Variable, RuleValue Value);

/// <summary>
/// A record that violates a rule: which rule and version, the record, and the values the rule read, so a finding can
/// show what was observed. <see cref="Explanation"/> is the rule's own text.
/// </summary>
public sealed record RuleViolation(
    string RuleId,
    int RuleVersion,
    RuleSeverity Severity,
    string Dataset,
    string RecordKey,
    string Explanation,
    IReadOnlyList<ObservedValue> Observed);
