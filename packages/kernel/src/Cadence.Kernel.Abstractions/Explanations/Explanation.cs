using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.References;

namespace Cadence.Kernel.Explanations;

/// <summary>
/// Why something holds: why an action is permitted or denied, why an object is in its state, why a finding was raised
/// or why a metric has its value (ADR-2482, spec §5.5, Canvas D3). The component that owns the rule produces it;
/// applications and the UI render it and never compose one themselves (K-EXP-1).
/// </summary>
/// <param name="Subject">The object being explained, or null when the explanation is about an action on no object yet.</param>
/// <param name="Claim">The conclusion, as one sentence ("Signing eligibility for 017-004 is not permitted").</param>
/// <param name="Producer">The component and rule version that produced it (for example <c>kernel.authorization</c>).</param>
/// <param name="Steps">The chain of reasoning, each claim with the evidence behind it.</param>
/// <param name="Conditions">Every condition the producer evaluated, met and unmet, each with its evidence.</param>
/// <param name="NextActions">What would change the outcome, when the producer knows.</param>
public sealed record Explanation(
    NodeRef? Subject,
    string Claim,
    string Producer,
    IReadOnlyList<ExplanationStep> Steps,
    IReadOnlyList<EvaluatedCondition> Conditions,
    IReadOnlyList<NextAction> NextActions)
{
    /// <summary>The conditions that were not met; empty when the outcome is not blocked by any.</summary>
    public IReadOnlyList<EvaluatedCondition> UnmetConditions => [.. Conditions.Where(c => !c.Met)];

    /// <summary>Record equality compares list contents, so the same evidence gives an equal explanation (K-EXP-3).</summary>
    public bool Equals(Explanation? other) =>
        other is not null
        && Equals(Subject, other.Subject)
        && string.Equals(Claim, other.Claim, StringComparison.Ordinal)
        && string.Equals(Producer, other.Producer, StringComparison.Ordinal)
        && Steps.SequenceEqual(other.Steps)
        && Conditions.SequenceEqual(other.Conditions)
        && NextActions.SequenceEqual(other.NextActions);

    public override int GetHashCode() => HashCode.Combine(Subject, Claim, Producer, Steps.Count, Conditions.Count, NextActions.Count);
}

/// <summary>One claim in an explanation's chain, with the objects that support it.</summary>
/// <param name="Claim">What this step establishes.</param>
/// <param name="Evidence">Objects the reader can open to check the claim.</param>
public sealed record ExplanationStep(string Claim, IReadOnlyList<NodeRef> Evidence)
{
    public bool Equals(ExplanationStep? other) =>
        other is not null && string.Equals(Claim, other.Claim, StringComparison.Ordinal) && Evidence.SequenceEqual(other.Evidence);

    public override int GetHashCode() => HashCode.Combine(Claim, Evidence.Count);
}

/// <summary>
/// A condition a rule evaluated, whether it was met, and the evidence it was decided on (K-AUTH-6). Unmet conditions
/// name what is missing; met conditions let the UI show a checklist rather than only the failure.
/// </summary>
/// <param name="Code">Stable machine code for the condition (for example <c>qualification.current</c>), for localisation and tests.</param>
/// <param name="Description">What the condition requires, in words a user can act on.</param>
/// <param name="Met">Whether the condition held.</param>
/// <param name="Evidence">The objects the decision rests on: the grant, the training record, the lock. Empty when the evidence is absent.</param>
public sealed record EvaluatedCondition(string Code, string Description, bool Met, IReadOnlyList<NodeRef> Evidence)
{
    public static EvaluatedCondition Satisfied(string code, string description, params NodeRef[] evidence) =>
        new(code, description, Met: true, evidence);

    public static EvaluatedCondition Unsatisfied(string code, string description, params NodeRef[] evidence) =>
        new(code, description, Met: false, evidence);

    public bool Equals(EvaluatedCondition? other) =>
        other is not null
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && string.Equals(Description, other.Description, StringComparison.Ordinal)
        && Met == other.Met
        && Evidence.SequenceEqual(other.Evidence);

    public override int GetHashCode() => HashCode.Combine(Code, Met, Evidence.Count);
}

/// <summary>Something the reader can do to change the outcome.</summary>
/// <param name="Description">The action in words ("Complete protocol v3 training").</param>
/// <param name="Capability">The capability the action needs, when it is a kernel command.</param>
/// <param name="Target">The object the action applies to, when there is one.</param>
public sealed record NextAction(string Description, string? Capability = null, NodeRef? Target = null);
