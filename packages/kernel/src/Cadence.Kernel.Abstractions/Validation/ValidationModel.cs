using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Validation;

/// <summary>The lifecycle stage a rule validates and a run evaluates (spec §10.2). Each stage has its own blocking effect.</summary>
public enum ValidationStage
{
    /// <summary>Study design objects before they are published; <c>Error</c> blocks publishing.</summary>
    Design,

    /// <summary>Data as it is captured (edit checks); never blocks a kernel write of a clinical fact.</summary>
    Collection,

    /// <summary>Tabulation datasets such as SDTM and SEND; <c>Error</c> blocks releasing a dataset unless explained or accepted.</summary>
    Standardization,

    /// <summary>Analysis datasets such as ADaM; blocks like <see cref="Standardization"/>.</summary>
    Analysis,

    /// <summary>The submission package: Define-XML, reviewer guides, dataset files; <c>Error</c> blocks package attestation.</summary>
    Submission,
}

/// <summary>How serious a rule violation is (spec §10.1).</summary>
public enum ValidationSeverity
{
    /// <summary>Must be fixed, explained or accepted; blocks where the stage blocks.</summary>
    Error,

    /// <summary>Should be reviewed; never blocks.</summary>
    Warning,

    /// <summary>Information only.</summary>
    Notice,
}

/// <summary>
/// Where a finding stands (spec §10.3): <c>Open → Investigating → Resolved | Accepted | Explained</c>. Only a
/// validation run resolves a finding, by no longer detecting it (K-FIND-1).
/// </summary>
public enum FindingStatus
{
    /// <summary>Detected by the latest run that covers it, and nobody has acted on it yet.</summary>
    Open,

    /// <summary>Someone is looking into it.</summary>
    Investigating,

    /// <summary>The latest run that covers it no longer detects it; that run is the resolution evidence.</summary>
    Resolved,

    /// <summary>Still detected, and accepted as it is by an attested decision (K-FIND-2).</summary>
    Accepted,

    /// <summary>Still detected, and explained by an attested explanation that flows into the reviewer guide (K-FIND-2).</summary>
    Explained,
}

/// <summary>What kind of problem a finding turned out to be (spec §10.3).</summary>
public enum FindingClassification
{
    /// <summary>The source data is wrong or incomplete.</summary>
    DataIssue,

    /// <summary>The mapping specification produced the wrong representation.</summary>
    MappingIssue,

    /// <summary>Standard or study metadata is wrong.</summary>
    MetadataIssue,

    /// <summary>The rule itself is wrong or does not fit the data.</summary>
    RuleIssue,

    /// <summary>Not a problem.</summary>
    NotAnIssue,
}

/// <summary>What a finding is about.</summary>
public enum FindingTargetKind
{
    /// <summary>A record, and optionally one variable, of a dataset (an SDTM row, a form record).</summary>
    DatasetRecord,

    /// <summary>A kernel Observation; the key is its NodeRef.</summary>
    Observation,

    /// <summary>A design element such as a protocol version or collection specification; the key is its NodeRef.</summary>
    DesignElement,
}

/// <summary>
/// The thing a finding is about (spec §10.3). <see cref="Dataset"/> is the dataset, domain or record type the rule
/// checked; <see cref="Key"/> identifies the record in it (a row key for <see cref="FindingTargetKind.DatasetRecord"/>, a
/// NodeRef without a version for the other kinds, so the finding follows the object across its versions); <see cref="Variable"/> narrows the target to one variable when the caller knows it.
/// </summary>
public sealed record FindingTarget(FindingTargetKind Kind, string Dataset, string Key, string? Variable = null)
{
    /// <summary>The longest dataset or variable name.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The longest record key.</summary>
    public const int MaxKeyLength = 200;

    /// <summary>The kernel object the target is, for <see cref="FindingTargetKind.Observation"/> and <see cref="FindingTargetKind.DesignElement"/>.</summary>
    public NodeRef? Node => Kind != FindingTargetKind.DatasetRecord && NodeRef.TryParse(Key, out var node) ? node : null;

    /// <summary>The problems with this target, or an empty list when it is well formed.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Kind))
        {
            problems.Add($"Unknown target kind {(int)Kind}.");
        }

        CheckText(Dataset, "dataset", MaxNameLength, required: true, problems);
        CheckText(Key, "key", MaxKeyLength, required: true, problems);
        CheckText(Variable, "variable", MaxNameLength, required: false, problems);
        if (Kind != FindingTargetKind.DatasetRecord && Key is not null)
        {
            if (!NodeRef.TryParse(Key, out var node))
            {
                problems.Add($"A {Kind} target's key must be a NodeRef; \"{Key}\" is not.");
            }
            else if (node.Version is not null)
            {
                problems.Add($"A target names the object, not one version of it, so a finding keeps its identity when the object changes; drop \"@{node.Version}\" from \"{Key}\".");
            }
            else if ((Kind == FindingTargetKind.Observation) != (node.Kind == NodeKinds.Observation))
            {
                problems.Add(Kind == FindingTargetKind.Observation
                    ? $"An Observation target must name an {NodeKinds.Observation}; \"{Key}\" names a {node.Kind}."
                    : $"A DesignElement target must not name an {NodeKinds.Observation}.");
            }
        }

        return problems;
    }

    private static void CheckText(string? value, string name, int max, bool required, List<string> problems)
    {
        if (value is null)
        {
            if (required)
            {
                problems.Add($"The target {name} is required.");
            }

            return;
        }

        if (value.Length == 0 || value.Length > max || value.Trim().Length != value.Length)
        {
            problems.Add($"The target {name} must be 1 to {max} characters with no surrounding spaces.");
        }
    }
}

/// <summary>
/// What identifies a finding across runs (K-FIND-1): the study and stage, the rule pack's key and the rule's id, and
/// the target. The rule version is not part of it, so a revised rule that still finds the same problem names the same
/// finding; the finding records the version that detected it.
/// </summary>
public sealed record FindingIdentity(StudyId Study, ValidationStage Stage, string PackKey, string RuleId, FindingTarget Target)
{
    /// <summary>The components <see cref="ValidationFindingId.For"/> hashes, in order.</summary>
    public IReadOnlyList<string> Components(TenantId tenant)
    {
        ArgumentNullException.ThrowIfNull(Target);
        return
        [
            tenant.ToString(),
            Study.ToString(),
            Stage.ToString(),
            PackKey,
            RuleId,
            Target.Kind.ToString(),
            Target.Dataset,
            Target.Key,
            Target.Variable ?? string.Empty,
        ];
    }
}

/// <summary>A variable's value as the rule read it, as invariant text; null when the record had no value.</summary>
public sealed record ObservedVariable(string Variable, string? Value);

/// <summary>
/// One problem a run detected (spec §10.3): which rule of which pack, at what severity, about which target, what it
/// expected and what it observed. The run's rule packs must include <see cref="PackKey"/>.
/// </summary>
public sealed record ValidationDetection(
    string PackKey,
    string RuleId,
    int RuleVersion,
    ValidationSeverity Severity,
    FindingTarget Target,
    string Message,
    string? Expected,
    IReadOnlyList<ObservedVariable> Observed)
{
    /// <summary>The longest message or expectation.</summary>
    public const int MaxTextLength = 2000;

    /// <summary>The longest rule id.</summary>
    public const int MaxRuleIdLength = 64;
}

/// <summary>The stage and severity rules that decide what a finding blocks (spec §10.2).</summary>
public static class FindingBlocking
{
    /// <summary>
    /// True when a finding at <paramref name="stage"/> with <paramref name="severity"/> and <paramref name="status"/>
    /// blocks the stage's gate: publishing a design object, releasing a dataset, attesting a submission package. Only
    /// <see cref="ValidationSeverity.Error"/> findings that are <see cref="FindingStatus.Open"/> or
    /// <see cref="FindingStatus.Investigating"/> block. Collection-stage findings never block: a well-typed clinical fact
    /// is always accepted, and edit checks run after it is written (G4).
    /// </summary>
    public static bool Blocks(ValidationStage stage, ValidationSeverity severity, FindingStatus status) =>
        stage != ValidationStage.Collection
        && severity == ValidationSeverity.Error
        && status is FindingStatus.Open or FindingStatus.Investigating;
}

/// <summary>
/// The content an Accepted or Explained decision attests (K-FIND-2): the finding, the rule version and observed values
/// it was decided on, the decision and its text. The signer attests <see cref="Hash"/>, so the attestation covers the
/// exact words that go into the reviewer guide.
/// </summary>
public static class FindingDecisionContent
{
    /// <summary>The canonical JSON of a decision on <paramref name="finding"/>.</summary>
    public static string Canonical(ValidationFindingId finding, ValidationRunId detectedBy, FindingStatus decision, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var json = new JsonObject
        {
            ["decision"] = decision.ToString(),
            ["detectedBy"] = detectedBy.ToString(),
            ["finding"] = finding.ToString(),
            ["text"] = text,
        };
        return ContentHash.Canonicalize(json.ToJsonString());
    }

    /// <summary>The content hash the signer attests for a decision on <paramref name="finding"/>.</summary>
    public static string Hash(ValidationFindingId finding, ValidationRunId detectedBy, FindingStatus decision, string text) =>
        ContentHash.OfCanonical(Canonical(finding, detectedBy, decision, text));
}

/// <summary>Text limits for finding decisions.</summary>
public static class FindingText
{
    /// <summary>The longest explanation, acceptance rationale or note.</summary>
    public const int MaxLength = 4000;

    /// <summary>True when <paramref name="text"/> is 1 to <see cref="MaxLength"/> characters and not blank.</summary>
    public static bool IsValid(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxLength;
}
