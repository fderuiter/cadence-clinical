using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        long? version,
        ProtocolVersionId? baseVersionId,
        long? baseVersion,
        LibraryItemId? libraryItemId,
        LibraryItemId? baseLibraryItemId,
        StandardVersionId? standardVersionId,
        string? content)
    {
        Kind = kind;
        StudyId = studyId;
        VersionId = versionId;
        Version = version;
        BaseVersionId = baseVersionId;
        BaseVersion = baseVersion;
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

    /// <summary>Optional audit version pin for <see cref="VersionId"/>; null evaluates the latest stored version.</summary>
    public long? Version { get; }

    /// <summary>The base protocol version of unsaved protocol content; null for stored protocol candidates or library-item candidates.</summary>
    public ProtocolVersionId? BaseVersionId { get; }

    /// <summary>Optional audit version pin for <see cref="BaseVersionId"/>; null evaluates the latest stored base version.</summary>
    public long? BaseVersion { get; }

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
    public static SimulationCandidate StoredProtocolVersion(ProtocolVersionId version, long? expectedVersion = null)
    {
        if (version.IsEmpty)
        {
            throw new ArgumentException("Name the protocol version to simulate.", nameof(version));
        }

        if (expectedVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "A pinned version must be at least 1.");
        }

        return new SimulationCandidate(CandidateKind.ProtocolVersion, null, version, expectedVersion, null, null, null, null, null, null);
    }

    /// <summary>
    /// Protocol content that is not saved anywhere, compared with <paramref name="baseVersion"/>, which must have
    /// frozen content (in review or later) so the comparison is reproducible. The content must read as a protocol
    /// design, as a stored draft's must (ADR-2472).
    /// </summary>
    public static SimulationCandidate UnsavedProtocolVersion(ProtocolVersionId baseVersion, string content, long? expectedBaseVersion = null)
    {
        if (baseVersion.IsEmpty)
        {
            throw new ArgumentException("Name the protocol version the content is compared with.", nameof(baseVersion));
        }

        if (expectedBaseVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedBaseVersion), expectedBaseVersion, "A pinned version must be at least 1.");
        }

        ArgumentNullException.ThrowIfNull(content);
        return new SimulationCandidate(CandidateKind.ProtocolVersion, null, null, null, baseVersion, expectedBaseVersion, null, null, null, content);
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

        return new SimulationCandidate(CandidateKind.MappingSpecification, studyId, null, null, null, null, libraryItem, baseLibraryItem, null, null);
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

        return new SimulationCandidate(CandidateKind.MappingSpecification, studyId, null, null, null, null, null, baseLibraryItem, standardVersion, content);
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

        return new SimulationCandidate(CandidateKind.RulePack, studyId, null, null, null, null, libraryItem, baseLibraryItem, null, null);
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

        return new SimulationCandidate(CandidateKind.RulePack, studyId, null, null, null, null, null, baseLibraryItem, null, content);
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
        return ContentHash.ComputeStreaming(writer => WriteCanonicalResult(writer, result));
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
            ["studyDay"] = ExpectedSchedule.NormalizeStudyDay(visit.StudyDay),
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

    private static void WriteCanonicalResult(Utf8JsonWriter writer, SimulationResult result)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("asOf"u8);
        writer.WriteStartObject();
        writer.WriteString("knownAt"u8, Instant(result.AsOf.KnownAt));
        writer.WriteString("validAt"u8, Instant(result.AsOf.ValidAt));
        writer.WriteEndObject();

        writer.WritePropertyName("candidate"u8);
        writer.WriteStartObject();
        writer.WriteString("baseContentHash"u8, result.Candidate.BaseContentHash);
        writer.WriteString("baseVersion"u8, result.Candidate.BaseVersion?.ToString());
        writer.WriteString("contentHash"u8, result.Candidate.ContentHash);
        writer.WriteString("kind"u8, result.Candidate.Kind.ToString());
        writer.WriteString("version"u8, result.Candidate.Version?.ToString());
        writer.WriteEndObject();

        WriteDependents(writer, result.Dependents);
        WriteEngines(writer, result.EngineVersions);
        writer.WriteString("inputHash"u8, result.InputHash);
        WriteMasked(writer, result.Masked);
        WriteMatrixGaps(writer, result.MatrixGaps);
        WriteOperationalImpact(writer, result.OperationalImpact);
        WriteProjectionChanges(writer, result.ProjectionChanges);
        WriteScheduleChanges(writer, result.ScheduleChanges);
        writer.WriteString("scope"u8, result.Scope.ToString());
        WriteStructuralDiff(writer, result.StructuralDiff);
        WriteValidationDelta(writer, result.ValidationDelta);

        writer.WriteEndObject();
    }

    private static void WriteDependents(Utf8JsonWriter writer, DependentsReport? dependents)
    {
        if (dependents is null)
        {
            writer.WriteNull("dependents"u8);
            return;
        }

        writer.WritePropertyName("dependents"u8);
        writer.WriteStartObject();
        writer.WriteNumber("depth"u8, dependents.Depth);
        writer.WritePropertyName("groups"u8);
        writer.WriteStartArray();
        for (var i = 0; i < dependents.Groups.Count; i++)
        {
            var group = dependents.Groups[i];
            writer.WriteStartObject();
            writer.WriteNumber("count"u8, group.Count);
            writer.WriteString("kind"u8, group.Kind);
            writer.WritePropertyName("objects"u8);
            writer.WriteStartArray();
            for (var j = 0; j < group.Objects.Count; j++)
            {
                var obj = group.Objects[j];
                writer.WriteStartObject();
                writer.WriteNumber("distance"u8, obj.Distance);
                writer.WriteString("handle"u8, obj.Handle);
                writer.WriteString("mask"u8, obj.Mask?.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("start"u8, dependents.Start);
        writer.WriteNumber("total"u8, dependents.Total);
        writer.WriteBoolean("truncated"u8, dependents.Truncated);
        writer.WriteEndObject();
    }

    private static void WriteEngines(Utf8JsonWriter writer, IReadOnlyList<EngineVersion> engines)
    {
        writer.WritePropertyName("engineVersions"u8);
        writer.WriteStartArray();
        for (var i = 0; i < engines.Count; i++)
        {
            var engine = engines[i];
            writer.WriteStartObject();
            writer.WriteString("name"u8, engine.Name);
            writer.WriteString("version"u8, engine.Version);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteMasked(Utf8JsonWriter writer, IReadOnlyList<MaskedPart> masked)
    {
        writer.WritePropertyName("masked"u8);
        writer.WriteStartArray();
        for (var i = 0; i < masked.Count; i++)
        {
            var item = masked[i];
            writer.WriteStartObject();
            writer.WriteString("part"u8, item.Part);
            writer.WriteString("reason"u8, item.Reason.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteMatrixGaps(Utf8JsonWriter writer, IReadOnlyList<MatrixGapFinding>? gaps)
    {
        if (gaps is null)
        {
            writer.WriteNull("matrixGaps"u8);
            return;
        }

        writer.WritePropertyName("matrixGaps"u8);
        writer.WriteStartArray();
        for (var i = 0; i < gaps.Count; i++)
        {
            var gap = gaps[i];
            writer.WriteStartObject();
            writer.WriteString("description"u8, gap.Description);
            writer.WriteString("elementId"u8, gap.ElementId);
            writer.WriteString("jsonPointerPath"u8, gap.JsonPointerPath);
            writer.WriteString("kind"u8, gap.Kind.ToString());
            writer.WriteString("recommendation"u8, gap.Recommendation);
            writer.WriteString("severity"u8, gap.Severity.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteOperationalImpact(Utf8JsonWriter writer, OperationalImpactSummary? impact)
    {
        if (impact is null)
        {
            writer.WriteNull("operationalImpact"u8);
            return;
        }

        writer.WritePropertyName("operationalImpact"u8);
        writer.WriteStartObject();
        writer.WriteNumber("activeSiteCount"u8, impact.ActiveSiteCount);
        writer.WriteNumber("affectedParticipantCount"u8, impact.AffectedParticipantCount);

        writer.WritePropertyName("hidden"u8);
        writer.WriteStartObject();
        writer.WriteBoolean("moreExist"u8, impact.Hidden.MoreExist);
        writer.WritePropertyName("reasons"u8);
        writer.WriteStartArray();
        for (var i = 0; i < impact.Hidden.Reasons.Count; i++)
        {
            writer.WriteStringValue(impact.Hidden.Reasons[i].ToString());
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("participants"u8);
        writer.WriteStartArray();
        Span<char> dateChars = stackalloc char[10];
        for (var i = 0; i < impact.ParticipantImpacts.Count; i++)
        {
            var participant = impact.ParticipantImpacts[i];
            writer.WriteStartObject();
            writer.WriteString("cohort"u8, participant.Cohort.ToString());
            writer.WriteString("enrollment"u8, participant.Enrollment.ToString());
            writer.WriteBoolean("requiresReconsent"u8, participant.RequiresReconsent);
            writer.WritePropertyName("scheduleShifts"u8);
            writer.WriteStartArray();
            for (var j = 0; j < participant.ScheduleShifts.Count; j++)
            {
                var shift = participant.ScheduleShifts[j];
                writer.WriteStartObject();
                writer.WriteString("activityId"u8, shift.ActivityId);
                WriteParticipantVisit(writer, "after"u8, shift.After, dateChars);
                WriteParticipantVisit(writer, "before"u8, shift.Before, dateChars);
                writer.WriteString("change"u8, shift.Change.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("status"u8, participant.Status.ToString());
            writer.WriteString("subjectCode"u8, participant.SubjectCode);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("pendingSiteCount"u8, impact.PendingSiteCount);
        writer.WriteNumber("reconsentParticipantCount"u8, impact.ReconsentParticipantCount);
        writer.WriteNumber("totalParticipantCount"u8, impact.TotalParticipantCount);
        writer.WriteEndObject();
    }

    private static void WriteParticipantVisit(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        ParticipantPlannedVisit? visit,
        Span<char> dateChars)
    {
        if (visit is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        _ = visit.TargetDate.TryFormat(dateChars, out var targetLen, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        writer.WriteString("targetDate"u8, dateChars[..targetLen]);
        writer.WriteString("visitId"u8, visit.VisitId);
        _ = visit.WindowEnd.TryFormat(dateChars, out var endLen, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        writer.WriteString("windowEnd"u8, dateChars[..endLen]);
        _ = visit.WindowStart.TryFormat(dateChars, out var startLen, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        writer.WriteString("windowStart"u8, dateChars[..startLen]);
        writer.WriteEndObject();
    }

    private static void WriteProjectionChanges(Utf8JsonWriter writer, IReadOnlyList<ProjectionDatasetChange>? changes)
    {
        if (changes is null)
        {
            writer.WriteNull("projectionChanges"u8);
            return;
        }

        writer.WritePropertyName("projectionChanges"u8);
        writer.WriteStartArray();
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            writer.WriteStartObject();
            writer.WriteNumber("addedCount"u8, change.AddedCount);
            writer.WriteNumber("changedCount"u8, change.ChangedCount);
            writer.WriteString("dataset"u8, change.Dataset);
            writer.WriteNumber("removedCount"u8, change.RemovedCount);
            writer.WritePropertyName("samples"u8);
            writer.WriteStartArray();
            for (var j = 0; j < change.Samples.Count; j++)
            {
                var sample = change.Samples[j];
                writer.WriteStartObject();
                WriteSortedStringDictionary(writer, "after"u8, sample.After);
                WriteSortedStringDictionary(writer, "before"u8, sample.Before);
                writer.WriteString("change"u8, sample.Change.ToString());
                writer.WriteString("recordKey"u8, sample.RecordKey);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSortedStringDictionary(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        IReadOnlyDictionary<string, string?>? values)
    {
        if (values is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        if (values is SortedDictionary<string, string?> sorted)
        {
            foreach (var (key, value) in sorted)
            {
                writer.WriteString(key, value);
            }
        }
        else if (values.Count > 0)
        {
            var keys = new string[values.Count];
            var idx = 0;
            foreach (var kv in values)
            {
                keys[idx++] = kv.Key;
            }

            Array.Sort(keys, StringComparer.Ordinal);
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                writer.WriteString(key, values[key]);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteScheduleChanges(Utf8JsonWriter writer, IReadOnlyList<ScheduleChange> changes)
    {
        writer.WritePropertyName("scheduleChanges"u8);
        writer.WriteStartArray();
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            writer.WriteStartObject();
            writer.WriteString("activityId"u8, change.ActivityId);
            WritePlannedVisit(writer, "after"u8, change.After);
            WritePlannedVisit(writer, "before"u8, change.Before);
            writer.WriteString("change"u8, change.Change.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePlannedVisit(Utf8JsonWriter writer, ReadOnlySpan<byte> propertyName, PlannedVisit? visit)
    {
        if (visit is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("epochId"u8, visit.EpochId);
        writer.WriteNumber("studyDay"u8, ExpectedSchedule.NormalizeStudyDay(visit.StudyDay));
        writer.WriteNumber("windowAfter"u8, visit.WindowAfter);
        writer.WriteNumber("windowBefore"u8, visit.WindowBefore);
        writer.WriteEndObject();
    }

    private static void WriteStructuralDiff(Utf8JsonWriter writer, IReadOnlyList<ElementChange> diff)
    {
        writer.WritePropertyName("structuralDiff"u8);
        writer.WriteStartArray();
        for (var i = 0; i < diff.Count; i++)
        {
            var change = diff[i];
            writer.WriteStartObject();
            writer.WriteString("change"u8, change.Change.ToString());
            writer.WriteString("elementId"u8, change.ElementId);
            writer.WriteString("instanceType"u8, change.InstanceType);
            writer.WritePropertyName("properties"u8);
            writer.WriteStartArray();
            for (var j = 0; j < change.Properties.Count; j++)
            {
                writer.WriteStringValue(change.Properties[j]);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteValidationDelta(Utf8JsonWriter writer, ValidationDeltaSummary? delta)
    {
        if (delta is null)
        {
            writer.WriteNull("validationDelta"u8);
            return;
        }

        writer.WritePropertyName("validationDelta"u8);
        writer.WriteStartObject();
        WriteFindingsArray(writer, "opening"u8, delta.Opening);
        WriteFindingsArray(writer, "resolving"u8, delta.Resolving);
        writer.WriteNumber("unchangedCount"u8, delta.UnchangedCount);
        writer.WriteEndObject();
    }

    private static void WriteFindingsArray(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        IReadOnlyList<SimulatedValidationFinding> findings)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        for (var i = 0; i < findings.Count; i++)
        {
            var finding = findings[i];
            writer.WriteStartObject();
            writer.WriteString("dataset"u8, finding.Dataset);
            writer.WriteString("explanation"u8, finding.Explanation);
            writer.WriteString("ruleId"u8, finding.RuleId);
            writer.WriteNumber("ruleVersion"u8, finding.RuleVersion);
            writer.WriteString("severity"u8, finding.Severity.ToString());
            writer.WriteString("stage"u8, finding.Stage.ToString());
            writer.WriteString("subject"u8, finding.Subject);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
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

    public static readonly KernelError VersionStale =
        new("K.Simulation.VersionStale", 409, "The protocol version is not at the pinned version; reload and simulate the current version.");

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
