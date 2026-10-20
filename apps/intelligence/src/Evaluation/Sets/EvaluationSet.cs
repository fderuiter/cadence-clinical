using System.Collections.Generic;

namespace Cadence.Modules.Intelligence.Evaluation.Sets;

/// <summary>
/// A versioned evaluation set (ADR-2634 §4.1): the cases one task configuration is qualified against. It is a reviewed
/// file in the repository; a published version is never edited, and a change is a new version.
/// </summary>
/// <param name="SetId">Stable kebab-case id, the directory name under <c>apps/intelligence/evaluation/sets/</c>.</param>
/// <param name="Version">Positive version number; the file is <c>v{Version}.json</c>.</param>
/// <param name="Task">The task the set measures, as the task configuration names it.</param>
/// <param name="Description">What the set covers and where its cases came from.</param>
/// <param name="Cases">The cases, each with a unique id.</param>
public sealed record EvaluationSet(
    string SetId,
    int Version,
    string Task,
    string Description,
    IReadOnlyList<EvaluationCase> Cases);

/// <summary>One case: the task input, what the agent may read, and what a correct answer looks like.</summary>
/// <param name="CaseId">Unique within the set.</param>
/// <param name="Kind">Reviewed clinical case or authored adversarial case.</param>
/// <param name="Request">The task input, as the requesting person would give it.</param>
/// <param name="Context">The kernel nodes the agent may read in this case, with their de-identified content.</param>
/// <param name="Expectation">What a correct answer does and must never do.</param>
/// <param name="Provenance">Where the case came from and who reviewed it.</param>
/// <param name="Category">Required for adversarial cases: the attack the case makes.</param>
public sealed record EvaluationCase(
    string CaseId,
    CaseKind Kind,
    string Request,
    IReadOnlyList<ContextNode> Context,
    CaseExpectation Expectation,
    CaseProvenance Provenance,
    AdversarialCategory? Category = null);

/// <summary>Where a case came from (ADR-2634 §4.1).</summary>
public enum CaseKind
{
    /// <summary>A real, de-identified case whose correct answer a qualified person confirmed.</summary>
    Reviewed,

    /// <summary>A case authored to attack the agent.</summary>
    Adversarial,
}

/// <summary>The attack an adversarial case makes.</summary>
public enum AdversarialCategory
{
    /// <summary>Context text carries an instruction to the agent (with a canary the agent must not repeat).</summary>
    PromptInjection,

    /// <summary>The data the task needs is absent; the agent should abstain rather than guess.</summary>
    MissingData,

    /// <summary>Two sources disagree; the agent must cite both or abstain, never pick silently.</summary>
    ConflictingSources,

    /// <summary>The request is outside the task's context of use; the agent should refuse.</summary>
    OutOfScope,

    /// <summary>The context tempts the agent to reveal blinded information.</summary>
    Blinding,
}

/// <summary>A kernel node the agent may read in a case.</summary>
/// <param name="Ref">The kernel node reference (<c>kind:id</c>), which citations name.</param>
/// <param name="Content">The node's de-identified content as the agent's tool would return it.</param>
public sealed record ContextNode(string Ref, string Content);

/// <summary>What a correct answer to a case looks like.</summary>
/// <param name="Outcome">Whether the agent should propose, abstain or refuse.</param>
/// <param name="Fields">Expected output fields; an empty list checks no field values.</param>
/// <param name="RequiredCitations">Node references the output must cite.</param>
/// <param name="Forbidden">Text that must never appear in the output (an injection canary, a blinded arm).</param>
/// <param name="AllowedFields">
/// Every field the output schema allows; an output field outside it fails the schema check. Empty means the expected
/// fields are the whole schema.
/// </param>
public sealed record CaseExpectation(
    ExpectedOutcome Outcome,
    IReadOnlyList<FieldExpectation> Fields,
    IReadOnlyList<string> RequiredCitations,
    IReadOnlyList<string> Forbidden,
    IReadOnlyList<string>? AllowedFields = null);

/// <summary>What the agent should do with a case.</summary>
public enum ExpectedOutcome
{
    /// <summary>Write a Proposal.</summary>
    Propose,

    /// <summary>Decline because the data does not support an answer.</summary>
    Abstain,

    /// <summary>Decline because the request is outside the task.</summary>
    Refuse,
}

/// <summary>An expected output field.</summary>
/// <param name="Name">The field name in the output schema.</param>
/// <param name="Value">The expected value.</param>
/// <param name="Match">How the value is compared.</param>
/// <param name="Tolerance">Absolute tolerance for <see cref="FieldMatch.Numeric"/>.</param>
public sealed record FieldExpectation(string Name, string Value, FieldMatch Match = FieldMatch.Exact, double Tolerance = 0);

/// <summary>How an expected field value is compared with the output.</summary>
public enum FieldMatch
{
    /// <summary>Ordinal equality after trimming.</summary>
    Exact,

    /// <summary>Case-insensitive equality after trimming and collapsing whitespace.</summary>
    Normalized,

    /// <summary>Both parse as numbers and differ by at most the tolerance.</summary>
    Numeric,

    /// <summary>The output contains the expected text, case-insensitively.</summary>
    Contains,
}

/// <summary>Where a case came from and who vouches for it (ADR-2634 §4.1).</summary>
/// <param name="Source">Free text: the workflow or authoring note the case came from, never an identifier of a person.</param>
/// <param name="Deidentification">How PHI was removed; required for reviewed cases.</param>
/// <param name="ReviewedBy">The role or staff code of the reviewer who confirmed the answer.</param>
/// <param name="ReviewedOn">The review date, ISO 8601.</param>
public sealed record CaseProvenance(
    string Source,
    DeidentificationMethod? Deidentification = null,
    string? ReviewedBy = null,
    string? ReviewedOn = null);

/// <summary>How a case's content was de-identified.</summary>
public enum DeidentificationMethod
{
    /// <summary>HIPAA Safe Harbor: the 18 identifiers removed.</summary>
    SafeHarbor,

    /// <summary>HIPAA expert determination.</summary>
    ExpertDetermination,

    /// <summary>The content was written synthetic from the start.</summary>
    Synthetic,
}

/// <summary>The identity a task configuration pins a set by: id, version and content hash.</summary>
/// <param name="SetId">The set id.</param>
/// <param name="Version">The set version.</param>
/// <param name="ContentHash">The kernel content hash of the set file's canonical JSON.</param>
public sealed record EvaluationSetRef(string SetId, int Version, string ContentHash);
