using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Api;

/// <summary>The pinned evaluation window of a simulation run (K-SIM-2).</summary>
/// <param name="ValidAt">The valid time baseline and candidate were evaluated at.</param>
/// <param name="KnownAt">The transaction time baseline and candidate were evaluated at.</param>
public sealed record KernelSimulationAsOfResponse(DateTimeOffset ValidAt, DateTimeOffset KnownAt)
{
    internal static KernelSimulationAsOfResponse From(SimulationAsOf asOf) =>
        new(asOf.ValidAt, asOf.KnownAt);
}

/// <summary>The candidate specification a simulation result describes.</summary>
/// <param name="Kind">The candidate specification kind (<c>ProtocolVersion</c>, <c>MappingSpecification</c>, or <c>RulePack</c>).</param>
/// <param name="Version">The stored candidate's pinned NodeRef, or null for unsaved content or when masked.</param>
/// <param name="BaseVersion">The base version's pinned NodeRef, or null when there is none or when masked.</param>
/// <param name="ContentHash">The candidate content's hash, or null when masked.</param>
/// <param name="BaseContentHash">The base content's hash, or null when there is no base or when masked.</param>
public sealed record KernelCandidateSummaryResponse(
    string Kind,
    string? Version,
    string? BaseVersion,
    string? ContentHash,
    string? BaseContentHash)
{
    internal static KernelCandidateSummaryResponse From(CandidateSummary summary) =>
        new(
            summary.Kind.ToString(),
            summary.Version?.ToString(),
            summary.BaseVersion?.ToString(),
            summary.ContentHash,
            summary.BaseContentHash);
}

/// <summary>A pure evaluation function and version sealed into a simulation run (K-SIM-2, K-SIM-3).</summary>
/// <param name="Name">The function identifier, for example <c>protocol.structural_diff</c>.</param>
/// <param name="Version">The function's version string.</param>
public sealed record KernelEngineVersionResponse(string Name, string Version)
{
    internal static KernelEngineVersionResponse From(EngineVersion engine) =>
        new(engine.Name, engine.Version);
}

/// <summary>A planned visit on the study-day axis (K-TIME-7, ADR-2487).</summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Name">The activity's name.</param>
/// <param name="EpochId">The epoch it belongs to.</param>
/// <param name="EpochOrder">The epoch's order in the design.</param>
/// <param name="StudyDay">The planned study day.</param>
/// <param name="WindowBefore">Days allowed before the planned day.</param>
/// <param name="WindowAfter">Days allowed after the planned day.</param>
/// <param name="ProtocolElement">Optional pinned <c>protocol_element</c> NodeRef for <paramref name="ActivityId"/> when governed.</param>
/// <param name="EpochProtocolElement">Optional pinned <c>protocol_element</c> NodeRef for <paramref name="EpochId"/> when governed.</param>
public sealed record KernelPlannedVisitResponse(
    string ActivityId,
    string Name,
    string EpochId,
    int EpochOrder,
    int StudyDay,
    int WindowBefore,
    int WindowAfter,
    string? ProtocolElement = null,
    string? EpochProtocolElement = null)
{
    internal static KernelPlannedVisitResponse? From(
        PlannedVisit? visit,
        IReadOnlyDictionary<string, NodeRef>? governedElements = null) =>
        visit is null
            ? null
            : new(
                visit.ActivityId,
                visit.Name,
                visit.EpochId,
                visit.EpochOrder,
                visit.StudyDay,
                visit.WindowBefore,
                visit.WindowAfter,
                KernelElementNodeRefs.Resolve(governedElements, visit.ActivityId),
                KernelElementNodeRefs.Resolve(governedElements, visit.EpochId));
}

/// <summary>A timed planned activity whose expected schedule differs between the base and candidate (K-TIME-7).</summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Change"><c>Added</c>, <c>Removed</c>, or <c>Changed</c>.</param>
/// <param name="Before">Its planned visit in the base, or null when added.</param>
/// <param name="After">Its planned visit in the candidate, or null when removed.</param>
/// <param name="ProtocolElement">Optional pinned <c>protocol_element</c> NodeRef when governed by an approved protocol version.</param>
public sealed record KernelScheduleChangeResponse(
    string ActivityId,
    string Change,
    KernelPlannedVisitResponse? Before,
    KernelPlannedVisitResponse? After,
    string? ProtocolElement = null)
{
    internal static KernelScheduleChangeResponse From(
        ScheduleChange change,
        IReadOnlyDictionary<string, NodeRef>? governedElements = null) =>
        new(
            change.ActivityId,
            change.Change.ToString(),
            KernelPlannedVisitResponse.From(change.Before, governedElements),
            KernelPlannedVisitResponse.From(change.After, governedElements),
            KernelElementNodeRefs.Resolve(governedElements, change.ActivityId));
}

/// <summary>A simulation section withheld from the caller, and why (K-SIM-4, ADR-2471).</summary>
/// <param name="Part">The withheld section name, for example <c>structural_diff</c> or <c>matrix_gaps</c>.</param>
/// <param name="Reason">Why it is withheld (<c>NotPermitted</c>, <c>Blinded</c>, <c>IdentityVault</c>).</param>
public sealed record KernelMaskedPartResponse(string Part, string Reason)
{
    internal static KernelMaskedPartResponse From(MaskedPart masked) =>
        new(masked.Part, masked.Reason.ToString());
}

/// <summary>A Schedule of Activities matrix gap or structural protocol finding.</summary>
/// <param name="ElementId">The stable element identifier associated with the finding.</param>
/// <param name="Kind">The category of gap detected.</param>
/// <param name="Severity"><c>Error</c>, <c>Warning</c>, or <c>Info</c>.</param>
/// <param name="JsonPointerPath">The JSON Pointer path to the element or property in the protocol design.</param>
/// <param name="Description">Human-readable description of the defect or gap.</param>
/// <param name="Recommendation">Actionable guidance on how to resolve the gap.</param>
/// <param name="ProtocolElement">Optional pinned <c>protocol_element</c> NodeRef for <paramref name="ElementId"/> when governed.</param>
public sealed record KernelMatrixGapFindingResponse(
    string ElementId,
    string Kind,
    string Severity,
    string JsonPointerPath,
    string Description,
    string? Recommendation,
    string? ProtocolElement = null)
{
    internal static KernelMatrixGapFindingResponse From(
        MatrixGapFinding finding,
        IReadOnlyDictionary<string, NodeRef>? governedElements = null) =>
        new(
            finding.ElementId,
            finding.Kind.ToString(),
            finding.Severity.ToString(),
            finding.JsonPointerPath,
            finding.Description,
            finding.Recommendation,
            KernelElementNodeRefs.Resolve(governedElements, finding.ElementId));
}

/// <summary>Signals whether restricted sites, enrollments, or blinded facts were omitted from simulation counts (ADR-2590).</summary>
/// <param name="MoreExist">True when at least one object was omitted from the caller's counts.</param>
/// <param name="Reasons">Distinct string mask reasons (<c>NotPermitted</c>, <c>Blinded</c>, <c>IdentityVault</c>).</param>
public sealed record KernelHiddenObjectsResponse(bool MoreExist, IReadOnlyList<string> Reasons)
{
    internal static KernelHiddenObjectsResponse From(HiddenObjects hidden) =>
        new(hidden.MoreExist, [.. hidden.Reasons.Select(r => r.ToString())]);
}

/// <summary>One calendar-dated visit window on a participant's anchored expected schedule (K-TIME-7).</summary>
public sealed record KernelParticipantPlannedVisitResponse(
    string VisitId,
    DateOnly TargetDate,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    string? ProtocolElement = null)
{
    internal static KernelParticipantPlannedVisitResponse? From(
        ParticipantPlannedVisit? visit,
        IReadOnlyDictionary<string, NodeRef>? governedElements = null) =>
        visit is null
            ? null
            : new(
                visit.VisitId,
                visit.TargetDate,
                visit.WindowStart,
                visit.WindowEnd,
                KernelElementNodeRefs.Resolve(governedElements, visit.VisitId));
}

/// <summary>A calendar-anchored schedule shift for one participant (K-TIME-7).</summary>
public sealed record KernelParticipantScheduleShiftResponse(
    string ActivityId,
    string Change,
    KernelParticipantPlannedVisitResponse? Before,
    KernelParticipantPlannedVisitResponse? After,
    string? ProtocolElement = null)
{
    internal static KernelParticipantScheduleShiftResponse From(
        ParticipantScheduleShift shift,
        IReadOnlyDictionary<string, NodeRef>? governedElements = null) =>
        new(
            shift.ActivityId,
            shift.Change.ToString(),
            KernelParticipantPlannedVisitResponse.From(shift.Before, governedElements),
            KernelParticipantPlannedVisitResponse.From(shift.After, governedElements),
            KernelElementNodeRefs.Resolve(governedElements, shift.ActivityId));
}

/// <summary>Per-participant operational impact summary (K-TIME-7, K-IDN-1).</summary>
public sealed record KernelParticipantSimulationImpactResponse(
    string Enrollment,
    string SubjectCode,
    string Status,
    string Cohort,
    bool RequiresReconsent,
    IReadOnlyList<KernelParticipantScheduleShiftResponse> ScheduleShifts)
{
    internal static KernelParticipantSimulationImpactResponse From(
        ParticipantSimulationImpact impact,
        IReadOnlyDictionary<string, NodeRef>? governedElements = null) =>
        new(
            impact.Enrollment.ToString(),
            impact.SubjectCode,
            impact.Status.ToString(),
            impact.Cohort.ToString(),
            impact.RequiresReconsent,
            [.. impact.ScheduleShifts.Select(s => KernelParticipantScheduleShiftResponse.From(s, governedElements))]);
}

/// <summary>Operational site and participant impact counts within the reader's visible scope (CLINICAL_CANVAS §4.2, ADR-2590).</summary>
public sealed record KernelOperationalImpactResponse(
    int ActiveSiteCount,
    int PendingSiteCount,
    int TotalParticipantCount,
    int AffectedParticipantCount,
    int ReconsentParticipantCount,
    IReadOnlyList<KernelParticipantSimulationImpactResponse> ParticipantImpacts,
    KernelHiddenObjectsResponse Hidden)
{
    internal static KernelOperationalImpactResponse From(
        OperationalImpactSummary impact,
        IReadOnlyDictionary<string, NodeRef>? governedElements = null) =>
        new(
            impact.ActiveSiteCount,
            impact.PendingSiteCount,
            impact.TotalParticipantCount,
            impact.AffectedParticipantCount,
            impact.ReconsentParticipantCount,
            [.. impact.ParticipantImpacts.Select(p => KernelParticipantSimulationImpactResponse.From(p, governedElements))],
            KernelHiddenObjectsResponse.From(impact.Hidden));
}

/// <summary>One bounded sample of a projected dataset record difference (ADR-2480 §4.1, K-PROJ-2).</summary>
public sealed record KernelProjectionRecordChangeSampleResponse(
    string RecordKey,
    string Change,
    IReadOnlyDictionary<string, string?>? Before,
    IReadOnlyDictionary<string, string?>? After)
{
    internal static KernelProjectionRecordChangeSampleResponse From(ProjectionRecordChangeSample sample) =>
        new(sample.RecordKey, sample.Change.ToString(), sample.Before, sample.After);
}

/// <summary>Per-dataset projection change summary produced through <c>IMappingEngine</c> (ADR-2480 §4.1, K-PROJ-2).</summary>
public sealed record KernelProjectionDatasetChangeResponse(
    string Dataset,
    int AddedCount,
    int RemovedCount,
    int ChangedCount,
    IReadOnlyList<KernelProjectionRecordChangeSampleResponse> Samples)
{
    internal static KernelProjectionDatasetChangeResponse From(ProjectionDatasetChange change) =>
        new(
            change.Dataset,
            change.AddedCount,
            change.RemovedCount,
            change.ChangedCount,
            [.. change.Samples.Select(KernelProjectionRecordChangeSampleResponse.From)]);
}

/// <summary>One edit-check finding opening or resolving in a simulation delta (ADR-2480 §4.1, K-RULE-4, K-FIND-1).</summary>
public sealed record KernelSimulatedValidationFindingResponse(
    string RuleId,
    int RuleVersion,
    string Severity,
    string Stage,
    string Dataset,
    string Subject,
    string Explanation)
{
    internal static KernelSimulatedValidationFindingResponse From(SimulatedValidationFinding finding) =>
        new(
            finding.RuleId,
            finding.RuleVersion,
            finding.Severity.ToString(),
            finding.Stage.ToString(),
            finding.Dataset,
            finding.Subject,
            finding.Explanation);
}

/// <summary>Rule validation finding delta between baseline and candidate evaluations (ADR-2480 §4.1, K-RULE-4, K-FIND-1).</summary>
public sealed record KernelValidationDeltaResponse(
    IReadOnlyList<KernelSimulatedValidationFindingResponse> Opening,
    IReadOnlyList<KernelSimulatedValidationFindingResponse> Resolving,
    int UnchangedCount)
{
    internal static KernelValidationDeltaResponse From(ValidationDeltaSummary delta) =>
        new(
            [.. delta.Opening.Select(KernelSimulatedValidationFindingResponse.From)],
            [.. delta.Resolving.Select(KernelSimulatedValidationFindingResponse.From)],
            delta.UnchangedCount);
}

/// <summary>Evaluate-only simulation preview of what a candidate protocol version, mapping specification, or rule pack changes (ADR-2480).</summary>
/// <param name="Scope">The study NodeRef where the simulation ran.</param>
/// <param name="AsOf">The pinned time both evaluations read at.</param>
/// <param name="Candidate">The candidate and its base.</param>
/// <param name="EngineVersions">The pure functions evaluated, sorted by name.</param>
/// <param name="InputHash">Hash of the inputs (K-SIM-3).</param>
/// <param name="ResultHash">Hash of this result without itself (K-SIM-3).</param>
/// <param name="StructuralDiff">Elements added, removed, or changed, sorted by element id.</param>
/// <param name="ScheduleChanges">Timed activities added, removed, or moved on the expected schedule.</param>
/// <param name="Masked">Sections withheld from this caller (K-SIM-4).</param>
/// <param name="Dependents">What depends on the base version's changed elements, or null when unchanged, unbased, or masked.</param>
/// <param name="MatrixGaps">Schedule matrix gaps and structural findings detected in the candidate design.</param>
/// <param name="IsEmpty">True when the candidate changes nothing this caller may see.</param>
/// <param name="OperationalImpact">Bitemporal site and participant operational impact counts and cohorts within the reader's scope.</param>
/// <param name="ProjectionChanges">Per-dataset projection changes produced through <c>IMappingEngine</c>.</param>
/// <param name="ValidationDelta">Edit-check finding delta produced through <c>IRuleEvaluator</c>.</param>
public sealed record KernelSimulationResponse(
    string Scope,
    KernelSimulationAsOfResponse AsOf,
    KernelCandidateSummaryResponse Candidate,
    IReadOnlyList<KernelEngineVersionResponse> EngineVersions,
    string InputHash,
    string ResultHash,
    IReadOnlyList<KernelElementChangeResponse> StructuralDiff,
    IReadOnlyList<KernelScheduleChangeResponse> ScheduleChanges,
    IReadOnlyList<KernelMaskedPartResponse> Masked,
    KernelDependentsResponse? Dependents,
    IReadOnlyList<KernelMatrixGapFindingResponse> MatrixGaps,
    bool IsEmpty,
    KernelOperationalImpactResponse? OperationalImpact = null,
    IReadOnlyList<KernelProjectionDatasetChangeResponse>? ProjectionChanges = null,
    KernelValidationDeltaResponse? ValidationDelta = null)
{
    internal static KernelSimulationResponse From(
        SimulationResult result,
        IReadOnlyDictionary<string, NodeRef>? governedElements = null) =>
        new(
            result.Scope.ToString(),
            KernelSimulationAsOfResponse.From(result.AsOf),
            KernelCandidateSummaryResponse.From(result.Candidate),
            [.. result.EngineVersions.Select(KernelEngineVersionResponse.From)],
            result.InputHash,
            result.ResultHash,
            [.. result.StructuralDiff.Select(c => new KernelElementChangeResponse(
                c.ElementId,
                c.InstanceType,
                c.Change.ToString(),
                c.Properties,
                KernelElementNodeRefs.Resolve(governedElements, c.ElementId)))],
            [.. result.ScheduleChanges.Select(sc => KernelScheduleChangeResponse.From(sc, governedElements))],
            [.. result.Masked.Select(KernelMaskedPartResponse.From)],
            result.Dependents is { } dependents ? KernelDependentsResponse.From(dependents) : null,
            [.. (result.MatrixGaps ?? []).Select(g => KernelMatrixGapFindingResponse.From(g, governedElements))],
            result.IsEmpty,
            result.OperationalImpact is { } operational ? KernelOperationalImpactResponse.From(operational, governedElements) : null,
            result.ProjectionChanges is { } projections ? [.. projections.Select(KernelProjectionDatasetChangeResponse.From)] : null,
            result.ValidationDelta is { } validation ? KernelValidationDeltaResponse.From(validation) : null);

    internal bool TryToDomain(out SimulationResult? result, out string? error)
    {
        result = null;
        error = null;
        if (!NodeRef.TryParse(Scope, out var scope) || scope is null)
        {
            error = $"Invalid simulation scope '{Scope}'.";
            return false;
        }

        if (AsOf is null || Candidate is null)
        {
            error = "Simulation AsOf and Candidate are required.";
            return false;
        }

        if (!TryParseEnum(Candidate.Kind, "candidate.kind", out CandidateKind kind, out error))
        {
            return false;
        }

        if (!TryParseOptionalNodeRef(Candidate.Version, "candidate.version", out var candidateVersion, out error)
            || !TryParseOptionalNodeRef(Candidate.BaseVersion, "candidate.base_version", out var baseVersion, out error))
        {
            return false;
        }

        var engines = new List<EngineVersion>();
        foreach (var engine in EngineVersions ?? [])
        {
            if (engine is null || string.IsNullOrWhiteSpace(engine.Name) || string.IsNullOrWhiteSpace(engine.Version))
            {
                error = "Engine version entries require name and version.";
                return false;
            }

            engines.Add(new EngineVersion(engine.Name, engine.Version));
        }

        var structuralDiff = new List<ElementChange>();
        foreach (var item in StructuralDiff ?? [])
        {
            if (item is null || !TryParseEnum(item.Change, "structural_diff.change", out ElementChangeKind changeKind, out error))
            {
                error ??= "Structural diff entry is invalid.";
                return false;
            }

            structuralDiff.Add(new ElementChange(item.ElementId ?? string.Empty, item.InstanceType, changeKind, item.Properties ?? []));
        }

        var scheduleChanges = new List<ScheduleChange>();
        foreach (var sc in ScheduleChanges ?? [])
        {
            if (sc is null || !TryParseEnum(sc.Change, "schedule_changes.change", out ElementChangeKind scKind, out error))
            {
                error ??= "Schedule change entry is invalid.";
                return false;
            }

            scheduleChanges.Add(new ScheduleChange(
                sc.ActivityId ?? string.Empty,
                scKind,
                ToDomainPlannedVisit(sc.Before),
                ToDomainPlannedVisit(sc.After)));
        }

        var maskedParts = new List<MaskedPart>();
        foreach (var mp in Masked ?? [])
        {
            if (mp is null || !TryParseEnum(mp.Reason, "masked.reason", out MaskReason maskReason, out error))
            {
                error ??= "Masked part entry is invalid.";
                return false;
            }

            maskedParts.Add(new MaskedPart(mp.Part ?? string.Empty, maskReason));
        }

        DependentsReport? dependents = null;
        if (Dependents is not null)
        {
            var groups = new List<DependentGroup>();
            foreach (var group in Dependents.Groups ?? [])
            {
                if (group is null)
                {
                    error = "Dependent group entry is invalid.";
                    return false;
                }

                var objects = new List<LineageNode>();
                foreach (var obj in group.Objects ?? [])
                {
                    if (obj is null || !TryParseOptionalNodeRef(obj.Ref, "dependents.objects.ref", out var objRef, out error))
                    {
                        error ??= "Dependent lineage node is invalid.";
                        return false;
                    }

                    MaskReason? objMask = null;
                    if (!string.IsNullOrEmpty(obj.Mask))
                    {
                        if (!TryParseEnum(obj.Mask, "dependents.objects.mask", out MaskReason parsedMask, out error))
                        {
                            return false;
                        }

                        objMask = parsedMask;
                    }

                    objects.Add(new LineageNode(obj.Handle ?? string.Empty, obj.Kind ?? string.Empty, objRef, objMask, obj.Distance));
                }

                groups.Add(new DependentGroup(group.Kind ?? string.Empty, group.Count, objects));
            }

            dependents = new DependentsReport(
                Dependents.Start ?? string.Empty,
                Dependents.Depth,
                Dependents.ValidAt,
                Dependents.KnownAt,
                groups,
                Dependents.Total,
                Dependents.Truncated);
        }

        IReadOnlyList<MatrixGapFinding>? matrixGaps = null;
        if (kind == CandidateKind.ProtocolVersion || MatrixGaps is { Count: > 0 })
        {
            var gapList = new List<MatrixGapFinding>();
            foreach (var gap in MatrixGaps ?? [])
            {
                if (gap is null
                    || !TryParseEnum(gap.Kind, "matrix_gaps.kind", out MatrixGapKind gapKind, out error)
                    || !TryParseEnum(gap.Severity, "matrix_gaps.severity", out MatrixGapSeverity gapSeverity, out error))
                {
                    error ??= "Matrix gap finding is invalid.";
                    return false;
                }

                gapList.Add(new MatrixGapFinding(
                    gap.ElementId ?? string.Empty,
                    gapKind,
                    gapSeverity,
                    gap.JsonPointerPath ?? string.Empty,
                    gap.Description ?? string.Empty,
                    gap.Recommendation));
            }

            matrixGaps = gapList;
        }

        OperationalImpactSummary? operationalImpact = null;
        if (OperationalImpact is not null)
        {
            var participantImpacts = new List<ParticipantSimulationImpact>();
            foreach (var participant in OperationalImpact.ParticipantImpacts ?? [])
            {
                if (participant is null
                    || !NodeRef.TryParse(participant.Enrollment, out var enrollmentRef)
                    || enrollmentRef is null
                    || !TryParseEnum(participant.Status, "operational_impact.participant_impacts.status", out EnrollmentStatus status, out error)
                    || !TryParseEnum(participant.Cohort, "operational_impact.participant_impacts.cohort", out ParticipantImpactCohort cohort, out error))
                {
                    error ??= "Participant simulation impact is invalid.";
                    return false;
                }

                var shifts = new List<ParticipantScheduleShift>();
                foreach (var shift in participant.ScheduleShifts ?? [])
                {
                    if (shift is null || !TryParseEnum(shift.Change, "schedule_shifts.change", out ElementChangeKind shiftKind, out error))
                    {
                        error ??= "Participant schedule shift is invalid.";
                        return false;
                    }

                    shifts.Add(new ParticipantScheduleShift(
                        shift.ActivityId ?? string.Empty,
                        shiftKind,
                        ToDomainParticipantVisit(shift.Before),
                        ToDomainParticipantVisit(shift.After)));
                }

                participantImpacts.Add(new ParticipantSimulationImpact(
                    enrollmentRef,
                    participant.SubjectCode ?? string.Empty,
                    status,
                    cohort,
                    participant.RequiresReconsent,
                    shifts));
            }

            var hiddenReasons = new List<MaskReason>();
            if (OperationalImpact.Hidden is not null)
            {
                foreach (var reasonText in OperationalImpact.Hidden.Reasons ?? [])
                {
                    if (!TryParseEnum(reasonText, "operational_impact.hidden.reasons", out MaskReason hiddenReason, out error))
                    {
                        return false;
                    }

                    hiddenReasons.Add(hiddenReason);
                }
            }

            var hidden = OperationalImpact.Hidden is { MoreExist: true } || hiddenReasons.Count > 0
                ? new HiddenObjects(OperationalImpact.Hidden?.MoreExist ?? true, hiddenReasons)
                : HiddenObjects.None;

            operationalImpact = new OperationalImpactSummary(
                OperationalImpact.ActiveSiteCount,
                OperationalImpact.PendingSiteCount,
                OperationalImpact.TotalParticipantCount,
                OperationalImpact.AffectedParticipantCount,
                OperationalImpact.ReconsentParticipantCount,
                participantImpacts,
                hidden);
        }

        IReadOnlyList<ProjectionDatasetChange>? projectionChanges = null;
        if (ProjectionChanges is not null)
        {
            var datasetList = new List<ProjectionDatasetChange>();
            foreach (var dataset in ProjectionChanges)
            {
                if (dataset is null)
                {
                    error = "Projection dataset change entry is invalid.";
                    return false;
                }

                var samples = new List<ProjectionRecordChangeSample>();
                foreach (var sample in dataset.Samples ?? [])
                {
                    if (sample is null || !TryParseEnum(sample.Change, "projection_changes.samples.change", out ElementChangeKind sampleKind, out error))
                    {
                        error ??= "Projection record sample is invalid.";
                        return false;
                    }

                    samples.Add(new ProjectionRecordChangeSample(
                        sample.RecordKey ?? string.Empty,
                        sampleKind,
                        sample.Before,
                        sample.After));
                }

                datasetList.Add(new ProjectionDatasetChange(
                    dataset.Dataset ?? string.Empty,
                    dataset.AddedCount,
                    dataset.RemovedCount,
                    dataset.ChangedCount,
                    samples));
            }

            projectionChanges = datasetList;
        }

        ValidationDeltaSummary? validationDelta = null;
        if (ValidationDelta is not null)
        {
            if (!TryToDomainFindings(ValidationDelta.Opening, out var opening, out error)
                || !TryToDomainFindings(ValidationDelta.Resolving, out var resolving, out error))
            {
                return false;
            }

            validationDelta = new ValidationDeltaSummary(opening, resolving, ValidationDelta.UnchangedCount);
        }

        error = null;
        result = new SimulationResult(
            scope,
            new SimulationAsOf(AsOf.ValidAt, AsOf.KnownAt),
            new CandidateSummary(kind, candidateVersion, baseVersion, Candidate.ContentHash, Candidate.BaseContentHash),
            engines,
            InputHash ?? string.Empty,
            ResultHash ?? string.Empty,
            structuralDiff,
            scheduleChanges,
            maskedParts,
            dependents,
            matrixGaps,
            operationalImpact,
            projectionChanges,
            validationDelta);
        return true;
    }

    private static PlannedVisit? ToDomainPlannedVisit(KernelPlannedVisitResponse? visit) =>
        visit is null
            ? null
            : new PlannedVisit(
                visit.ActivityId ?? string.Empty,
                visit.Name ?? string.Empty,
                visit.EpochId ?? string.Empty,
                visit.EpochOrder,
                visit.StudyDay,
                visit.WindowBefore,
                visit.WindowAfter);

    private static ParticipantPlannedVisit? ToDomainParticipantVisit(KernelParticipantPlannedVisitResponse? visit) =>
        visit is null
            ? null
            : new ParticipantPlannedVisit(visit.VisitId ?? string.Empty, visit.TargetDate, visit.WindowStart, visit.WindowEnd);

    private static bool TryToDomainFindings(
        IReadOnlyList<KernelSimulatedValidationFindingResponse>? items,
        out IReadOnlyList<SimulatedValidationFinding> findings,
        out string? error)
    {
        error = null;
        var list = new List<SimulatedValidationFinding>();
        foreach (var item in items ?? [])
        {
            if (item is null
                || !TryParseEnum(item.Severity, "validation_delta.severity", out ValidationSeverity severity, out error)
                || !TryParseEnum(item.Stage, "validation_delta.stage", out ValidationStage stage, out error))
            {
                findings = [];
                error ??= "Simulated validation finding is invalid.";
                return false;
            }

            list.Add(new SimulatedValidationFinding(
                item.RuleId ?? string.Empty,
                item.RuleVersion,
                severity,
                stage,
                item.Dataset ?? string.Empty,
                item.Subject ?? string.Empty,
                item.Explanation ?? string.Empty));
        }

        findings = list;
        error = null;
        return true;
    }

    private static bool TryParseOptionalNodeRef(string? raw, string fieldName, out NodeRef? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (string.IsNullOrEmpty(raw))
        {
            return true;
        }

        if (!NodeRef.TryParse(raw, out parsed) || parsed is null)
        {
            error = $"Invalid NodeRef '{raw}' in {fieldName}.";
            return false;
        }

        return true;
    }

    private static bool TryParseEnum<TEnum>(string? raw, string fieldName, out TEnum value, out string? error)
        where TEnum : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse(raw, ignoreCase: false, out value)
            && Enum.IsDefined(value))
        {
            error = null;
            return true;
        }

        value = default;
        error = $"Invalid value '{raw}' for {fieldName}.";
        return false;
    }
}

/// <summary>Request body for <c>POST /api/v1/kernel/artifact-versions/{artifactVersionRef}/review</c> (21 CFR Part 11, K-SIM-5).</summary>
/// <param name="Meaning">Attestation meaning (defaults to <c>Reviewed</c>).</param>
/// <param name="ExpectedContentHash">Optional immutable artifact-version content hash expected by the signer (21 CFR 11.70).</param>
/// <param name="AttestationId">Optional caller-assigned attestation UUID; derived deterministically when omitted.</param>
public sealed record KernelReviewArtifactVersionRequest(
    string? Meaning = null,
    string? ExpectedContentHash = null,
    Guid? AttestationId = null);

/// <summary>Recorded <c>Reviewed</c> attestation returned by <c>POST /api/v1/kernel/artifact-versions/{artifactVersionRef}/review</c>.</summary>
/// <param name="AttestationRef">Canonical NodeRef of the recorded attestation (<c>attestation:&lt;uuid&gt;</c>).</param>
/// <param name="AttestationId">Recorded attestation UUID.</param>
/// <param name="ArtifactRef">Canonical NodeRef of the owning artifact (<c>artifact:&lt;uuid&gt;</c>).</param>
/// <param name="ArtifactVersionRef">Canonical NodeRef of the attested artifact version (<c>artifact_version:&lt;uuid&gt;</c>).</param>
/// <param name="StudyRef">Canonical NodeRef of the owning study when study-scoped, otherwise null.</param>
/// <param name="Meaning">Attestation meaning (<c>Reviewed</c>).</param>
/// <param name="ContentHash">Canonical <c>sha256:</c> digest bound to the attestation.</param>
/// <param name="RecordedAt">Timestamp when the review signature was recorded.</param>
public sealed record KernelArtifactReviewAttestationResponse(
    string AttestationRef,
    Guid AttestationId,
    string ArtifactRef,
    string ArtifactVersionRef,
    string? StudyRef,
    string Meaning,
    string ContentHash,
    DateTimeOffset RecordedAt);
