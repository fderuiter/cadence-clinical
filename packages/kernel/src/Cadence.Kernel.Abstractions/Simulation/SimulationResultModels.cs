using System;
using System.Collections.Generic;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Simulation;

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
