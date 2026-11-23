using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Simulation;

/// <summary>
/// Answers "what happens if I change this?" by evaluating a candidate version against its base, never by writing
/// (ADR-2480, spec §11.1). A run is a read: it needs <see cref="SimulationCapabilities.Run"/> at the scope, sees only
/// what the actor may read (K-SIM-4), and changes no clinical state (K-SIM-1).
/// </summary>
public interface ISimulationEngine
{
    /// <summary>
    /// Evaluates <paramref name="request"/> for the current actor. Fails with a <see cref="KernelError"/> when a
    /// version is unknown, the candidate is invalid or the actor may not run simulations at the scope.
    /// </summary>
    Task<CommandResult<SimulationResult>> SimulateAsync(TenantId tenant, SimulationRequest request, CancellationToken cancellationToken);
}

/// <summary>The capabilities simulation checks (ADR-2480 §4.3).</summary>
public static class SimulationCapabilities
{
    /// <summary>Running a simulation at a scope. It is a read, and it is never written to the audit chain.</summary>
    public static readonly Capability Run = new("simulation.run", VerbClass.Read);

    /// <summary>Saving an unmasked simulation result as an attestable <c>SimulationReport</c> artifact (K-SIM-5).</summary>
    public static readonly Capability SaveReport = new("simulation.save_report", VerbClass.Execute);
}

/// <summary>What kind of versioned specification a candidate is (ADR-2480 §4.1).</summary>
public enum CandidateKind
{
    /// <summary>A protocol version (spec §6.2). Its scope is its study.</summary>
    ProtocolVersion,

    /// <summary>A concept-to-variable mapping specification library item (ADR-2463). Evaluated within a study scope.</summary>
    MappingSpecification,

    /// <summary>A declarative validation rule pack library item (spec §10.1). Evaluated within a study scope.</summary>
    RulePack,
}

/// <summary>
/// The proposed change: a candidate version of a versioned specification, compared with its base. Create one with
/// <see cref="StoredProtocolVersion"/>, <see cref="UnsavedProtocolVersion"/>,
/// <see cref="StoredMappingSpecification"/>, <see cref="UnsavedMappingSpecification"/>,
/// <see cref="StoredRulePack"/>, or <see cref="UnsavedRulePack"/>.
/// </summary>
public sealed record SimulationCandidate
{
    private SimulationCandidate(
        CandidateKind kind,
        StudyId? studyId,
        ProtocolVersionId? versionId,
        ProtocolVersionId? baseVersionId,
        LibraryItemId? libraryItemId,
        LibraryItemId? baseLibraryItemId,
        StandardVersionId? standardVersionId,
        string? content)
    {
        Kind = kind;
        StudyId = studyId;
        VersionId = versionId;
        BaseVersionId = baseVersionId;
        LibraryItemId = libraryItemId;
        BaseLibraryItemId = baseLibraryItemId;
        StandardVersionId = standardVersionId;
        Content = content;
    }

    /// <summary>The kind of specification.</summary>
    public CandidateKind Kind { get; }

    /// <summary>The study scope for library-backed candidates (mapping specifications and rule packs); null when inferred from a protocol version.</summary>
    public StudyId? StudyId { get; }

    /// <summary>A stored candidate protocol version; null for unsaved content or library-item candidates.</summary>
    public ProtocolVersionId? VersionId { get; }

    /// <summary>The base protocol version of unsaved protocol content; null for stored protocol candidates or library-item candidates.</summary>
    public ProtocolVersionId? BaseVersionId { get; }

    /// <summary>A stored library-item candidate (mapping specification or rule pack); null for unsaved content or protocol candidates.</summary>
    public LibraryItemId? LibraryItemId { get; }

    /// <summary>Optional base library item to compare a mapping specification or rule pack candidate against.</summary>
    public LibraryItemId? BaseLibraryItemId { get; }

    /// <summary>Optional target standard version for an unsaved mapping specification candidate.</summary>
    public StandardVersionId? StandardVersionId { get; }

    /// <summary>Unsaved candidate JSON content; null for a stored candidate.</summary>
    public string? Content { get; }

    /// <summary>
    /// A stored protocol version, usually a draft, compared with the version it was forked from. A study's first
    /// version has no parent, so all of its elements are added.
    /// </summary>
    public static SimulationCandidate StoredProtocolVersion(ProtocolVersionId version)
    {
        if (version.IsEmpty)
        {
            throw new ArgumentException("Name the protocol version to simulate.", nameof(version));
        }

        return new SimulationCandidate(CandidateKind.ProtocolVersion, null, version, null, null, null, null, null);
    }

    /// <summary>
    /// Protocol content that is not saved anywhere, compared with <paramref name="baseVersion"/>, which must have
    /// frozen content (in review or later) so the comparison is reproducible. The content must read as a protocol
    /// design, as a stored draft's must (ADR-2472).
    /// </summary>
    public static SimulationCandidate UnsavedProtocolVersion(ProtocolVersionId baseVersion, string content)
    {
        if (baseVersion.IsEmpty)
        {
            throw new ArgumentException("Name the protocol version the content is compared with.", nameof(baseVersion));
        }

        ArgumentNullException.ThrowIfNull(content);
        return new SimulationCandidate(CandidateKind.ProtocolVersion, null, null, baseVersion, null, null, null, content);
    }

    /// <summary>
    /// A stored mapping specification library item (draft or published) evaluated in <paramref name="studyId"/>
    /// against <paramref name="baseLibraryItem"/> (or its declared <c>extends</c> base when omitted).
    /// </summary>
    public static SimulationCandidate StoredMappingSpecification(
        StudyId studyId,
        LibraryItemId libraryItem,
        LibraryItemId? baseLibraryItem = null)
    {
        if (studyId.IsEmpty)
        {
            throw new ArgumentException("Name the study scope to simulate in.", nameof(studyId));
        }

        if (libraryItem.IsEmpty)
        {
            throw new ArgumentException("Name the mapping specification library item to simulate.", nameof(libraryItem));
        }

        if (baseLibraryItem is { IsEmpty: true })
        {
            throw new ArgumentException("The base library item cannot be empty.", nameof(baseLibraryItem));
        }

        return new SimulationCandidate(CandidateKind.MappingSpecification, studyId, null, null, libraryItem, baseLibraryItem, null, null);
    }

    /// <summary>
    /// Unsaved mapping specification JSON content evaluated in <paramref name="studyId"/> against an optional frozen
    /// <paramref name="baseLibraryItem"/> (or its declared <c>extends</c> base).
    /// </summary>
    public static SimulationCandidate UnsavedMappingSpecification(
        StudyId studyId,
        string content,
        LibraryItemId? baseLibraryItem = null,
        StandardVersionId? standardVersion = null)
    {
        if (studyId.IsEmpty)
        {
            throw new ArgumentException("Name the study scope to simulate in.", nameof(studyId));
        }

        ArgumentNullException.ThrowIfNull(content);
        if (baseLibraryItem is { IsEmpty: true })
        {
            throw new ArgumentException("The base library item cannot be empty.", nameof(baseLibraryItem));
        }

        if (standardVersion is { IsEmpty: true })
        {
            throw new ArgumentException("The standard version cannot be empty.", nameof(standardVersion));
        }

        return new SimulationCandidate(CandidateKind.MappingSpecification, studyId, null, null, null, baseLibraryItem, standardVersion, content);
    }

    /// <summary>
    /// A stored rule pack library item (draft or published) evaluated in <paramref name="studyId"/> against
    /// <paramref name="baseLibraryItem"/> (or the study's pinned rule packs when omitted).
    /// </summary>
    public static SimulationCandidate StoredRulePack(
        StudyId studyId,
        LibraryItemId libraryItem,
        LibraryItemId? baseLibraryItem = null)
    {
        if (studyId.IsEmpty)
        {
            throw new ArgumentException("Name the study scope to simulate in.", nameof(studyId));
        }

        if (libraryItem.IsEmpty)
        {
            throw new ArgumentException("Name the rule pack library item to simulate.", nameof(libraryItem));
        }

        if (baseLibraryItem is { IsEmpty: true })
        {
            throw new ArgumentException("The base library item cannot be empty.", nameof(baseLibraryItem));
        }

        return new SimulationCandidate(CandidateKind.RulePack, studyId, null, null, libraryItem, baseLibraryItem, null, null);
    }

    /// <summary>
    /// Unsaved rule pack JSON content evaluated in <paramref name="studyId"/> against an optional frozen
    /// <paramref name="baseLibraryItem"/> (or the study's pinned rule packs when omitted).
    /// </summary>
    public static SimulationCandidate UnsavedRulePack(
        StudyId studyId,
        string content,
        LibraryItemId? baseLibraryItem = null)
    {
        if (studyId.IsEmpty)
        {
            throw new ArgumentException("Name the study scope to simulate in.", nameof(studyId));
        }

        ArgumentNullException.ThrowIfNull(content);
        if (baseLibraryItem is { IsEmpty: true })
        {
            throw new ArgumentException("The base library item cannot be empty.", nameof(baseLibraryItem));
        }

        return new SimulationCandidate(CandidateKind.RulePack, studyId, null, null, null, baseLibraryItem, null, content);
    }
}

/// <summary>A simulation to run.</summary>
/// <param name="Candidate">The proposed change.</param>
/// <param name="ValidAt">Valid time both evaluations read at; null for now.</param>
/// <param name="KnownAt">Knowledge time both evaluations read at; null for now.</param>
public sealed record SimulationRequest(SimulationCandidate Candidate, DateTimeOffset? ValidAt = null, DateTimeOffset? KnownAt = null);

/// <summary>The one point in time the baseline and the candidate are both evaluated at (K-SIM-2).</summary>
public sealed record SimulationAsOf(DateTimeOffset ValidAt, DateTimeOffset KnownAt);

/// <summary>The candidate a result describes.</summary>
/// <param name="Kind">The kind of specification.</param>
/// <param name="Version">The stored candidate, pinned to the version read; null for unsaved content or when masked.</param>
/// <param name="BaseVersion">The base, pinned to the version read; null when there is none or when masked.</param>
/// <param name="ContentHash">The candidate content's hash; null when masked.</param>
/// <param name="BaseContentHash">The base content's hash; null when there is no base or when masked.</param>
public sealed record CandidateSummary(
    CandidateKind Kind,
    NodeRef? Version,
    NodeRef? BaseVersion,
    string? ContentHash,
    string? BaseContentHash);

/// <summary>A pure function a result was computed with, and its version (K-SIM-2, K-SIM-3).</summary>
public sealed record EngineVersion(string Name, string Version);

/// <summary>How an element differs between the base and the candidate.</summary>
public enum ElementChangeKind
{
    /// <summary>Only the candidate has the element.</summary>
    Added,

    /// <summary>Only the base has the element.</summary>
    Removed,

    /// <summary>Both have it, with different content.</summary>
    Changed,
}

/// <summary>One protocol element that differs, identified by its stable element id (K-PROT-1).</summary>
/// <param name="ElementId">The element's <c>id</c>, stable across versions.</param>
/// <param name="InstanceType">Its <c>instanceType</c> (for example <c>StudyArm</c>), when the content names one.</param>
/// <param name="Change">Added, removed or changed.</param>
/// <param name="Properties">
/// For a changed element, the JSON Pointers of the differing values inside it, sorted. A nested element's own changes
/// are reported on that element; the parent reports only that its list of children changed. Empty otherwise.
/// </param>
public sealed record ElementChange(string ElementId, string? InstanceType, ElementChangeKind Change, IReadOnlyList<string> Properties);

/// <summary>
/// A timed planned activity whose place on the expected schedule differs between the base and the candidate (K-TIME-7,
/// ADR-2487), on the study-day axis, so it holds for every participant whatever their reference start.
/// </summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Change">Added to the schedule (new, or newly timed), removed from it, or moved.</param>
/// <param name="Before">Its planned visit in the base; null when added.</param>
/// <param name="After">Its planned visit in the candidate; null when removed.</param>
public sealed record ScheduleChange(string ActivityId, ElementChangeKind Change, PlannedVisit? Before, PlannedVisit? After);

/// <summary>A part of a result withheld from this actor, and why (Canvas D1, ADR-2471).</summary>
/// <param name="Part">The withheld section, for example <c>structural_diff</c>.</param>
/// <param name="Reason">Why it is withheld.</param>
public sealed record MaskedPart(string Part, MaskReason Reason);

/// <summary>How a participant transitions under a simulated protocol amendment (spec §7.5, K-PROT-2).</summary>
public enum ParticipantImpactCohort
{
    /// <summary>The participant is terminal or has no upcoming schedule, consent, projection or validation impact.</summary>
    Unaffected,

    /// <summary>The participant transitions automatically with updated upcoming schedule expectations.</summary>
    AutomaticTransition,

    /// <summary>The participant must complete re-consent before the amended version governs their visits.</summary>
    RequiresReconsent,

    /// <summary>The participant is in screening while eligibility criteria changed.</summary>
    EligibilityReview,

    /// <summary>The participant transitions once their study site activates the amended version.</summary>
    SiteActivated,

    /// <summary>Existing enrolled participants stay on the parent version while new enrollments follow the amendment.</summary>
    CohortSplit,
}

/// <summary>One calendar-dated visit window on a participant's anchored expected schedule (K-TIME-7).</summary>
/// <param name="VisitId">The planned visit's element id.</param>
/// <param name="TargetDate">Target calendar date anchored from the enrollment's bitemporal reference start.</param>
/// <param name="WindowStart">Earliest compliant calendar date.</param>
/// <param name="WindowEnd">Latest compliant calendar date.</param>
public sealed record ParticipantPlannedVisit(
    string VisitId,
    DateOnly TargetDate,
    DateOnly WindowStart,
    DateOnly WindowEnd);

/// <summary>A calendar-anchored schedule shift for one participant (K-TIME-7).</summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Change">Added, removed, or moved/re-windowed.</param>
/// <param name="Before">Planned calendar window under the baseline version; null when added.</param>
/// <param name="After">Planned calendar window under the candidate version; null when removed.</param>
public sealed record ParticipantScheduleShift(
    string ActivityId,
    ElementChangeKind Change,
    ParticipantPlannedVisit? Before,
    ParticipantPlannedVisit? After);

/// <summary>
/// Per-participant operational impact summary (K-TIME-7, K-IDN-1). Identifies participants solely by
/// <see cref="Enrollment"/> and <see cref="SubjectCode"/>, never touching <c>IdentityVault</c> PII.
/// </summary>
/// <param name="Enrollment">The enrollment reference.</param>
/// <param name="SubjectCode">The study-scoped pseudonymous subject code.</param>
/// <param name="Status">The enrollment's bitemporal status at the simulation's pinned <c>(valid_at, known_at)</c>.</param>
/// <param name="Cohort">The transition or re-consent cohort assigned to the participant.</param>
/// <param name="RequiresReconsent">True when the participant requires re-consent under the candidate amendment.</param>
/// <param name="ScheduleShifts">Upcoming calendar-dated schedule shifts for this participant, sorted by activity id.</param>
public sealed record ParticipantSimulationImpact(
    NodeRef Enrollment,
    string SubjectCode,
    EnrollmentStatus Status,
    ParticipantImpactCohort Cohort,
    bool RequiresReconsent,
    IReadOnlyList<ParticipantScheduleShift> ScheduleShifts);

/// <summary>
/// Operational site and participant impact counts within the reader's visible scope (CLINICAL_CANVAS §4.2, ADR-2590).
/// </summary>
/// <param name="ActiveSiteCount">Visible study sites with an effective protocol version (or active status before effectivities) at <c>as_of</c>.</param>
/// <param name="PendingSiteCount">Visible study sites pending activation at <c>as_of</c>.</param>
/// <param name="TotalParticipantCount">Visible participants with an enrollment status at <c>as_of</c>.</param>
/// <param name="AffectedParticipantCount">Visible participants impacted by schedule shifts, re-consent, projection diffs or rule deltas.</param>
/// <param name="ReconsentParticipantCount">Visible participants requiring re-consent.</param>
/// <param name="ParticipantImpacts">Per-participant cohort and schedule shift breakdown for visible participants, sorted by subject code.</param>
/// <param name="Hidden">Whether additional restricted sites, enrollments or blinded records exist beyond the reader's scope (ADR-2590).</param>
public sealed record OperationalImpactSummary(
    int ActiveSiteCount,
    int PendingSiteCount,
    int TotalParticipantCount,
    int AffectedParticipantCount,
    int ReconsentParticipantCount,
    IReadOnlyList<ParticipantSimulationImpact> ParticipantImpacts,
    HiddenObjects Hidden);

/// <summary>One bounded sample of a projected dataset record difference (ADR-2480 §4.1, K-PROJ-2).</summary>
/// <param name="RecordKey">Stable key of the source observation or record.</param>
/// <param name="Change">Added, removed, or changed.</param>
/// <param name="Before">Projected variable values under the baseline; null when added.</param>
/// <param name="After">Projected variable values under the candidate; null when removed.</param>
public sealed record ProjectionRecordChangeSample(
    string RecordKey,
    ElementChangeKind Change,
    IReadOnlyDictionary<string, string?>? Before,
    IReadOnlyDictionary<string, string?>? After);

/// <summary>Per-dataset projection change summary produced through <c>IMappingEngine</c> (ADR-2480 §4.1, K-PROJ-2).</summary>
/// <param name="Dataset">Target dataset name from the mapping specification.</param>
/// <param name="AddedCount">Projected records added in the candidate.</param>
/// <param name="RemovedCount">Projected records removed in the candidate.</param>
/// <param name="ChangedCount">Projected records whose mapped variables changed in the candidate.</param>
/// <param name="Samples">Bounded deterministic sample of differing records.</param>
public sealed record ProjectionDatasetChange(
    string Dataset,
    int AddedCount,
    int RemovedCount,
    int ChangedCount,
    IReadOnlyList<ProjectionRecordChangeSample> Samples);

/// <summary>One edit-check finding opening or resolving in a simulation delta (ADR-2480 §4.1, K-RULE-4, K-FIND-1).</summary>
/// <param name="RuleId">Stable rule identifier.</param>
/// <param name="RuleVersion">Version of the rule in the evaluated pack.</param>
/// <param name="Severity">Finding severity.</param>
/// <param name="Stage">Validation stage.</param>
/// <param name="Dataset">Target dataset or collection specification code.</param>
/// <param name="Subject">Deterministic subject/record key.</param>
/// <param name="Explanation">Human-readable rule message.</param>
public sealed record SimulatedValidationFinding(
    string RuleId,
    int RuleVersion,
    ValidationSeverity Severity,
    ValidationStage Stage,
    string Dataset,
    string Subject,
    string Explanation);

/// <summary>Rule validation finding delta between baseline and candidate evaluations (ADR-2480 §4.1, K-RULE-4, K-FIND-1).</summary>
/// <param name="Opening">Findings absent in baseline that open under the candidate, sorted by rule id and subject.</param>
/// <param name="Resolving">Findings present in baseline that resolve under the candidate, sorted by rule id and subject.</param>
/// <param name="UnchangedCount">Count of findings present in both baseline and candidate evaluations.</param>
public sealed record ValidationDeltaSummary(
    IReadOnlyList<SimulatedValidationFinding> Opening,
    IReadOnlyList<SimulatedValidationFinding> Resolving,
    int UnchangedCount);

/// <summary>
/// What a candidate would change (ADR-2480 §4.1). Sections are added as the pure functions behind them exist:
/// <see cref="EngineVersions"/> names the ones this result was computed with.
/// </summary>
/// <param name="Scope">Where the simulation ran: the study scope.</param>
/// <param name="AsOf">The pinned time both evaluations read at.</param>
/// <param name="Candidate">The candidate and its base.</param>
/// <param name="EngineVersions">The functions evaluated, sorted by name.</param>
/// <param name="InputHash">Hash of the inputs: the as-of, the base and candidate content hashes and the engine versions.</param>
/// <param name="ResultHash">Hash of this result without itself. The same inputs always give the same hash (K-SIM-3).</param>
/// <param name="StructuralDiff">Elements added, removed or changed, sorted by element id (K-PROT-1).</param>
/// <param name="ScheduleChanges">
/// Timed activities added to, removed from or moved on the expected schedule, sorted by activity id (K-TIME-7). A
/// changed name or collection alone is in <paramref name="StructuralDiff"/>, not here.
/// </param>
/// <param name="Masked">Sections withheld from this actor (K-SIM-4).</param>
/// <param name="Dependents">
/// What depends on the base version, at <paramref name="AsOf"/>, through the one dependents read (Canvas D2); objects
/// the actor may not see are omitted from counts and signalled via <c>Hidden</c> (ADR-2590). Null when the candidate
/// changes nothing, has no base, or is masked.
/// </param>
/// <param name="MatrixGaps">Schedule matrix gaps and structural findings detected in the candidate protocol graph.</param>
/// <param name="OperationalImpact">Bitemporal site and participant operational impact counts and cohorts within the reader's scope.</param>
/// <param name="ProjectionChanges">Per-dataset projection changes produced through <c>IMappingEngine</c>.</param>
/// <param name="ValidationDelta">Edit-check finding delta produced through <c>IRuleEvaluator</c>.</param>
public sealed record SimulationResult(
    NodeRef Scope,
    SimulationAsOf AsOf,
    CandidateSummary Candidate,
    IReadOnlyList<EngineVersion> EngineVersions,
    string InputHash,
    string ResultHash,
    IReadOnlyList<ElementChange> StructuralDiff,
    IReadOnlyList<ScheduleChange> ScheduleChanges,
    IReadOnlyList<MaskedPart> Masked,
    DependentsReport? Dependents = null,
    IReadOnlyList<MatrixGapFinding>? MatrixGaps = null,
    OperationalImpactSummary? OperationalImpact = null,
    IReadOnlyList<ProjectionDatasetChange>? ProjectionChanges = null,
    ValidationDeltaSummary? ValidationDelta = null)
{
    /// <summary>True when the candidate changes nothing this actor may see.</summary>
    public bool IsEmpty =>
        StructuralDiff.Count == 0
        && ScheduleChanges.Count == 0
        && Masked.Count == 0
        && (ProjectionChanges is null || ProjectionChanges.Count == 0)
        && (ValidationDelta is null || (ValidationDelta.Opening.Count == 0 && ValidationDelta.Resolving.Count == 0));
}

/// <summary>
/// Canonical JSON serialization and SHA-256 content hashing for <see cref="SimulationResult"/> (K-SIM-3, K-SIM-5),
/// shared by simulation evaluation and saved simulation report verification.
/// </summary>
public static class SimulationResultHashing
{
    /// <summary>Computes the deterministic <c>sha256:...</c> result hash of <paramref name="result"/> (excluding <see cref="SimulationResult.ResultHash"/> itself).</summary>
    public static string ComputeResultHash(SimulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ContentHash.Compute(ToCanonicalJson(result));
    }

    /// <summary>Serializes <paramref name="result"/> (without <see cref="SimulationResult.ResultHash"/>) to deterministic canonical JSON.</summary>
    public static string ToCanonicalJson(SimulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var document = new JsonObject
        {
            ["scope"] = result.Scope.ToString(),
            ["asOf"] = InstantPair(result.AsOf),
            ["candidate"] = CandidateNode(result.Candidate),
            ["engineVersions"] = EnginesNode(result.EngineVersions),
            ["inputHash"] = result.InputHash,
            ["structuralDiff"] = new JsonArray([.. result.StructuralDiff.Select(ChangeNode)]),
            ["scheduleChanges"] = new JsonArray([.. result.ScheduleChanges.Select(ScheduleChangeNode)]),
            ["dependents"] = DependentsNode(result.Dependents),
            ["matrixGaps"] = MatrixGapsNode(result.MatrixGaps),
            ["operationalImpact"] = OperationalNode(result.OperationalImpact),
            ["projectionChanges"] = ProjectionChangesNode(result.ProjectionChanges),
            ["validationDelta"] = ValidationDeltaNode(result.ValidationDelta),
            ["masked"] = MaskedNode(result.Masked),
        };
        return document.ToJsonString();
    }

    /// <summary>Formats <paramref name="asOf"/> at kernel stored microsecond precision.</summary>
    public static JsonObject InstantPair(SimulationAsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(asOf);
        return new JsonObject
        {
            ["validAt"] = Instant(asOf.ValidAt),
            ["knownAt"] = Instant(asOf.KnownAt),
        };
    }

    /// <summary>Formats <paramref name="candidate"/> as a canonical JSON object.</summary>
    public static JsonObject CandidateNode(CandidateSummary candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new JsonObject
        {
            ["kind"] = candidate.Kind.ToString(),
            ["version"] = candidate.Version?.ToString(),
            ["baseVersion"] = candidate.BaseVersion?.ToString(),
            ["contentHash"] = candidate.ContentHash,
            ["baseContentHash"] = candidate.BaseContentHash,
        };
    }

    /// <summary>Formats <paramref name="engines"/> as a canonical JSON array.</summary>
    public static JsonArray EnginesNode(IReadOnlyList<EngineVersion> engines) =>
        new([.. engines.Select(e => (JsonNode?)new JsonObject { ["name"] = e.Name, ["version"] = e.Version })]);

    /// <summary>Formats <paramref name="masked"/> as a canonical JSON array.</summary>
    public static JsonArray MaskedNode(IReadOnlyList<MaskedPart> masked) =>
        new([.. masked.Select(m => (JsonNode?)new JsonObject { ["part"] = m.Part, ["reason"] = m.Reason.ToString() })]);

    private static JsonNode ChangeNode(ElementChange change) => new JsonObject
    {
        ["elementId"] = change.ElementId,
        ["instanceType"] = change.InstanceType,
        ["change"] = change.Change.ToString(),
        ["properties"] = new JsonArray([.. change.Properties.Select(p => (JsonNode?)JsonValue.Create(p))]),
    };

    private static JsonNode ScheduleChangeNode(ScheduleChange change) => new JsonObject
    {
        ["activityId"] = change.ActivityId,
        ["change"] = change.Change.ToString(),
        ["before"] = VisitNode(change.Before),
        ["after"] = VisitNode(change.After),
    };

    private static JsonNode? VisitNode(PlannedVisit? visit) => visit is null
        ? null
        : new JsonObject
        {
            ["epochId"] = visit.EpochId,
            ["studyDay"] = visit.StudyDay,
            ["windowBefore"] = visit.WindowBefore,
            ["windowAfter"] = visit.WindowAfter,
        };

    private static JsonNode? DependentsNode(DependentsReport? dependents) => dependents is null
        ? null
        : new JsonObject
        {
            ["start"] = dependents.Start,
            ["depth"] = dependents.Depth,
            ["total"] = dependents.Total,
            ["truncated"] = dependents.Truncated,
            ["groups"] = new JsonArray([.. dependents.Groups.Select(GroupNode)]),
        };

    private static JsonObject HiddenNode(HiddenObjects hidden) => new()
    {
        ["moreExist"] = hidden.MoreExist,
        ["reasons"] = new JsonArray([.. hidden.Reasons.Select(r => (JsonNode?)JsonValue.Create(r.ToString()))]),
    };

    private static JsonNode? MatrixGapsNode(IReadOnlyList<MatrixGapFinding>? gaps) => gaps is null
        ? null
        : new JsonArray([.. gaps.Select(g => (JsonNode?)new JsonObject
        {
            ["elementId"] = g.ElementId,
            ["kind"] = g.Kind.ToString(),
            ["severity"] = g.Severity.ToString(),
            ["jsonPointerPath"] = g.JsonPointerPath,
            ["description"] = g.Description,
            ["recommendation"] = g.Recommendation,
        })]);

    private static JsonNode GroupNode(DependentGroup group) => new JsonObject
    {
        ["kind"] = group.Kind,
        ["count"] = group.Count,
        ["objects"] = new JsonArray([.. group.Objects.Select(o => (JsonNode?)new JsonObject
        {
            ["handle"] = o.Handle,
            ["mask"] = o.Mask?.ToString(),
            ["distance"] = o.Distance,
        })]),
    };

    private static JsonNode? OperationalNode(OperationalImpactSummary? impact) => impact is null
        ? null
        : new JsonObject
        {
            ["activeSiteCount"] = impact.ActiveSiteCount,
            ["pendingSiteCount"] = impact.PendingSiteCount,
            ["totalParticipantCount"] = impact.TotalParticipantCount,
            ["affectedParticipantCount"] = impact.AffectedParticipantCount,
            ["reconsentParticipantCount"] = impact.ReconsentParticipantCount,
            ["participants"] = new JsonArray([.. impact.ParticipantImpacts.Select(ParticipantImpactNode)]),
            ["hidden"] = HiddenNode(impact.Hidden),
        };

    private static JsonNode ParticipantImpactNode(ParticipantSimulationImpact participant) => new JsonObject
    {
        ["enrollment"] = participant.Enrollment.ToString(),
        ["subjectCode"] = participant.SubjectCode,
        ["status"] = participant.Status.ToString(),
        ["cohort"] = participant.Cohort.ToString(),
        ["requiresReconsent"] = participant.RequiresReconsent,
        ["scheduleShifts"] = new JsonArray([.. participant.ScheduleShifts.Select(ParticipantShiftNode)]),
    };

    private static JsonNode ParticipantShiftNode(ParticipantScheduleShift shift) => new JsonObject
    {
        ["activityId"] = shift.ActivityId,
        ["change"] = shift.Change.ToString(),
        ["before"] = ParticipantVisitNode(shift.Before),
        ["after"] = ParticipantVisitNode(shift.After),
    };

    private static JsonNode? ParticipantVisitNode(ParticipantPlannedVisit? visit) => visit is null
        ? null
        : new JsonObject
        {
            ["visitId"] = visit.VisitId,
            ["targetDate"] = visit.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["windowStart"] = visit.WindowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["windowEnd"] = visit.WindowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

    private static JsonNode? ProjectionChangesNode(IReadOnlyList<ProjectionDatasetChange>? changes) => changes is null
        ? null
        : new JsonArray([.. changes.Select(c => (JsonNode?)new JsonObject
        {
            ["dataset"] = c.Dataset,
            ["addedCount"] = c.AddedCount,
            ["removedCount"] = c.RemovedCount,
            ["changedCount"] = c.ChangedCount,
            ["samples"] = new JsonArray([.. c.Samples.Select(SampleNode)]),
        })]);

    private static JsonNode SampleNode(ProjectionRecordChangeSample sample) => new JsonObject
    {
        ["recordKey"] = sample.RecordKey,
        ["change"] = sample.Change.ToString(),
        ["before"] = DictionaryNode(sample.Before),
        ["after"] = DictionaryNode(sample.After),
    };

    private static JsonNode? DictionaryNode(IReadOnlyDictionary<string, string?>? values)
    {
        if (values is null)
        {
            return null;
        }

        var obj = new JsonObject();
        foreach (var (key, value) in values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            obj[key] = value;
        }

        return obj;
    }

    private static JsonNode? ValidationDeltaNode(ValidationDeltaSummary? delta) => delta is null
        ? null
        : new JsonObject
        {
            ["opening"] = new JsonArray([.. delta.Opening.Select(FindingNode)]),
            ["resolving"] = new JsonArray([.. delta.Resolving.Select(FindingNode)]),
            ["unchangedCount"] = delta.UnchangedCount,
        };

    private static JsonNode FindingNode(SimulatedValidationFinding finding) => new JsonObject
    {
        ["ruleId"] = finding.RuleId,
        ["ruleVersion"] = finding.RuleVersion,
        ["severity"] = finding.Severity.ToString(),
        ["stage"] = finding.Stage.ToString(),
        ["dataset"] = finding.Dataset,
        ["subject"] = finding.Subject,
        ["explanation"] = finding.Explanation,
    };

    private static string Instant(DateTimeOffset value) =>
        KernelTimestamps.ToStoredPrecision(value).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// Canonical metadata binding a saved <see cref="Evidence.ArtifactKind.SimulationReport"/> artifact version to the
/// candidate and base specifications it evaluated (K-SIM-5, K-PROT-2).
/// </summary>
/// <param name="CandidateKind">The kind of candidate specification.</param>
/// <param name="CandidateVersion">The stored candidate reference, when saved.</param>
/// <param name="CandidateContentHash">The exact content hash of the candidate specification.</param>
/// <param name="BaseVersion">The base specification reference, when present.</param>
/// <param name="BaseContentHash">The exact content hash of the base specification, when present.</param>
/// <param name="InputHash">The simulation's <see cref="SimulationResult.InputHash"/>.</param>
/// <param name="ResultHash">The simulation's <see cref="SimulationResult.ResultHash"/>.</param>
public sealed record SimulationArtifactBinding(
    CandidateKind CandidateKind,
    NodeRef? CandidateVersion,
    string CandidateContentHash,
    NodeRef? BaseVersion,
    string? BaseContentHash,
    string InputHash,
    string ResultHash)
{
    /// <summary>Canonical media type of a saved simulation report artifact.</summary>
    public const string MediaType = "application/vnd.cadence.simulation-result+json";

    /// <summary>URI prefix used in <c>ArtifactContent.StorageRef</c> to record the verified simulation binding.</summary>
    public const string StoragePrefix = "urn:cadence:simulation-result:v1?";

    /// <summary>Extracts the artifact binding from an unmasked <paramref name="result"/>, or null when masked.</summary>
    public static SimulationArtifactBinding? FromResult(SimulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Candidate.ContentHash is not { } candidateHash || !ContentHash.IsWellFormed(candidateHash))
        {
            return null;
        }

        if (!ContentHash.IsWellFormed(result.InputHash) || !ContentHash.IsWellFormed(result.ResultHash))
        {
            return null;
        }

        return new SimulationArtifactBinding(
            result.Candidate.Kind,
            result.Candidate.Version is { } v ? new NodeRef(v.Kind, v.Id) : null,
            candidateHash,
            result.Candidate.BaseVersion is { } bv ? new NodeRef(bv.Kind, bv.Id) : null,
            result.Candidate.BaseContentHash,
            result.InputHash,
            result.ResultHash);
    }

    /// <summary>Builds a deterministic <see cref="Evidence.ArtifactContent"/> for <paramref name="result"/>.</summary>
    public static Evidence.ArtifactContent? ToArtifactContent(SimulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (FromResult(result) is not { } binding)
        {
            return null;
        }

        var canonicalJson = SimulationResultHashing.ToCanonicalJson(result);
        var computedHash = ContentHash.Compute(canonicalJson);
        if (!string.Equals(computedHash, result.ResultHash, StringComparison.Ordinal))
        {
            return null;
        }

        var sizeBytes = Encoding.UTF8.GetByteCount(canonicalJson);
        return new Evidence.ArtifactContent(result.ResultHash, sizeBytes, MediaType, binding.ToStorageRef());
    }

    /// <summary>Formats this binding as a deterministic <c>StorageRef</c> URI.</summary>
    public string ToStorageRef()
    {
        var parts = new List<string>(7)
        {
            $"kind={Uri.EscapeDataString(CandidateKind.ToString())}",
            $"candidateHash={Uri.EscapeDataString(CandidateContentHash)}",
            $"inputHash={Uri.EscapeDataString(InputHash)}",
            $"resultHash={Uri.EscapeDataString(ResultHash)}",
        };
        if (CandidateVersion is not null)
        {
            parts.Add($"candidate={Uri.EscapeDataString(new NodeRef(CandidateVersion.Kind, CandidateVersion.Id).ToString())}");
        }

        if (BaseVersion is not null)
        {
            parts.Add($"base={Uri.EscapeDataString(new NodeRef(BaseVersion.Kind, BaseVersion.Id).ToString())}");
        }

        if (!string.IsNullOrEmpty(BaseContentHash))
        {
            parts.Add($"baseHash={Uri.EscapeDataString(BaseContentHash)}");
        }

        return StoragePrefix + string.Join("&", parts);
    }

    /// <summary>Parses a <see cref="SimulationArtifactBinding"/> from <paramref name="storageRef"/>.</summary>
    public static bool TryParseStorageRef(string? storageRef, out SimulationArtifactBinding? binding)
    {
        binding = null;
        if (string.IsNullOrWhiteSpace(storageRef) || !storageRef.StartsWith(StoragePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in storageRef[StoragePrefix.Length..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0 || eq == segment.Length - 1)
            {
                return false;
            }

            values[segment[..eq]] = Uri.UnescapeDataString(segment[(eq + 1)..]);
        }

        if (!values.TryGetValue("kind", out var kindText)
            || !Enum.TryParse<CandidateKind>(kindText, ignoreCase: false, out var kind)
            || !values.TryGetValue("candidateHash", out var candidateHash)
            || !ContentHash.IsWellFormed(candidateHash)
            || !values.TryGetValue("inputHash", out var inputHash)
            || !ContentHash.IsWellFormed(inputHash)
            || !values.TryGetValue("resultHash", out var resultHash)
            || !ContentHash.IsWellFormed(resultHash))
        {
            return false;
        }

        NodeRef? candidateRef = null;
        if (values.TryGetValue("candidate", out var candidateText) && !NodeRef.TryParse(candidateText, out candidateRef))
        {
            return false;
        }

        NodeRef? baseRef = null;
        if (values.TryGetValue("base", out var baseText) && !NodeRef.TryParse(baseText, out baseRef))
        {
            return false;
        }

        string? baseHash = null;
        if (values.TryGetValue("baseHash", out var parsedBaseHash))
        {
            if (!ContentHash.IsWellFormed(parsedBaseHash))
            {
                return false;
            }

            baseHash = parsedBaseHash;
        }

        binding = new SimulationArtifactBinding(kind, candidateRef, candidateHash, baseRef, baseHash, inputHash, resultHash);
        return true;
    }

    /// <summary>
    /// True when this simulation binding proves an evaluation of <paramref name="amendedId"/> (at
    /// <paramref name="amendedContentHash"/>) forked from <paramref name="parentId"/> (at <paramref name="parentContentHash"/>).
    /// </summary>
    public bool MatchesProtocolAmendment(
        ProtocolVersionId parentId,
        string parentContentHash,
        ProtocolVersionId amendedId,
        string amendedContentHash) =>
        CandidateKind == CandidateKind.ProtocolVersion
        && CandidateVersion is not null
        && string.Equals(CandidateVersion.Kind, NodeKinds.ProtocolVersion, StringComparison.Ordinal)
        && CandidateVersion.Id == amendedId.Value
        && string.Equals(CandidateContentHash, amendedContentHash, StringComparison.Ordinal)
        && BaseVersion is not null
        && string.Equals(BaseVersion.Kind, NodeKinds.ProtocolVersion, StringComparison.Ordinal)
        && BaseVersion.Id == parentId.Value
        && string.Equals(BaseContentHash, parentContentHash, StringComparison.Ordinal);
}

/// <summary>Errors raised by simulation (ADR-2480).</summary>
public static class SimulationErrors
{
    public static readonly KernelError VersionUnknown =
        new("K.Simulation.VersionUnknown", 404, "The protocol version does not exist.");

    public static readonly KernelError StudyUnknown =
        new("K.Simulation.StudyUnknown", 404, "The study does not exist.");

    public static readonly KernelError LibraryItemUnknown =
        new("K.Simulation.LibraryItemUnknown", 404, "The library item does not exist or has a different kind.");

    public static readonly KernelError BaseNotFrozen =
        new("K.Simulation.BaseNotFrozen", 422, "The base version is still a draft; compare with a version whose content is frozen.");

    public static readonly KernelError ContentInvalid =
        new("K.Simulation.ContentInvalid", 422, "The candidate content must be a protocol design (ADR-2472).");

    public static readonly KernelError MappingSpecificationInvalid =
        new("K.Simulation.MappingSpecificationInvalid", 422, "The candidate content must be a valid mapping specification (ADR-2463).");

    public static readonly KernelError RulePackInvalid =
        new("K.Simulation.RulePackInvalid", 422, "The candidate content must be a valid rule pack (spec §10.1).");

    public static readonly KernelError ElementIdDuplicated =
        new("K.Simulation.ElementIdDuplicated", 422, "Two protocol elements share an id; element ids must be unique within a version (K-PROT-1).");
}
