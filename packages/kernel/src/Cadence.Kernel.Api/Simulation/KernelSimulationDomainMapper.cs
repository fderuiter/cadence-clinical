using System;
using System.Collections.Generic;

using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Api;

internal static class KernelSimulationDomainMapper
{
    internal static bool TryToDomain(
        KernelSimulationResponse response,
        out SimulationResult? result,
        out string? error)
    {
        result = null;
        error = null;
        if (!NodeRef.TryParse(response.Scope, out var scope) || scope is null)
        {
            error = $"Invalid simulation scope '{response.Scope}'.";
            return false;
        }

        if (response.AsOf is null || response.Candidate is null)
        {
            error = "Simulation AsOf and Candidate are required.";
            return false;
        }

        if (!TryParseEnum(response.Candidate.Kind, "candidate.kind", out CandidateKind kind, out error))
        {
            return false;
        }

        if (!TryParseOptionalNodeRef(response.Candidate.Version, "candidate.version", out var candidateVersion, out error)
            || !TryParseOptionalNodeRef(response.Candidate.BaseVersion, "candidate.base_version", out var baseVersion, out error))
        {
            return false;
        }

        var engines = new List<EngineVersion>();
        foreach (var engine in response.EngineVersions ?? [])
        {
            if (engine is null || string.IsNullOrWhiteSpace(engine.Name) || string.IsNullOrWhiteSpace(engine.Version))
            {
                error = "Engine version entries require name and version.";
                return false;
            }

            engines.Add(new EngineVersion(engine.Name, engine.Version));
        }

        var structuralDiff = new List<ElementChange>();
        foreach (var item in response.StructuralDiff ?? [])
        {
            if (item is null || !TryParseEnum(item.Change, "structural_diff.change", out ElementChangeKind changeKind, out error))
            {
                error ??= "Structural diff entry is invalid.";
                return false;
            }

            structuralDiff.Add(new ElementChange(item.ElementId ?? string.Empty, item.InstanceType, changeKind, item.Properties ?? []));
        }

        var scheduleChanges = new List<ScheduleChange>();
        foreach (var sc in response.ScheduleChanges ?? [])
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
        foreach (var mp in response.Masked ?? [])
        {
            if (mp is null || !TryParseEnum(mp.Reason, "masked.reason", out MaskReason maskReason, out error))
            {
                error ??= "Masked part entry is invalid.";
                return false;
            }

            maskedParts.Add(new MaskedPart(mp.Part ?? string.Empty, maskReason));
        }

        DependentsReport? dependents = null;
        if (response.Dependents is not null)
        {
            var groups = new List<DependentGroup>();
            foreach (var group in response.Dependents.Groups ?? [])
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
                response.Dependents.Start ?? string.Empty,
                response.Dependents.Depth,
                response.Dependents.ValidAt,
                response.Dependents.KnownAt,
                groups,
                response.Dependents.Total,
                response.Dependents.Truncated);
        }

        IReadOnlyList<MatrixGapFinding>? matrixGaps = null;
        if (kind == CandidateKind.ProtocolVersion || response.MatrixGaps is { Count: > 0 })
        {
            var gapList = new List<MatrixGapFinding>();
            foreach (var gap in response.MatrixGaps ?? [])
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
        if (response.OperationalImpact is not null && !TryToDomainOperationalImpact(response.OperationalImpact, out operationalImpact, out error))
        {
            return false;
        }

        IReadOnlyList<ProjectionDatasetChange>? projectionChanges = null;
        if (response.ProjectionChanges is not null && !TryToDomainProjectionChanges(response.ProjectionChanges, out projectionChanges, out error))
        {
            return false;
        }

        ValidationDeltaSummary? validationDelta = null;
        if (response.ValidationDelta is not null)
        {
            if (!TryToDomainFindings(response.ValidationDelta.Opening, out var opening, out error)
                || !TryToDomainFindings(response.ValidationDelta.Resolving, out var resolving, out error))
            {
                return false;
            }

            validationDelta = new ValidationDeltaSummary(opening, resolving, response.ValidationDelta.UnchangedCount);
        }

        error = null;
        result = new SimulationResult(
            scope,
            new SimulationAsOf(response.AsOf.ValidAt, response.AsOf.KnownAt),
            new CandidateSummary(kind, candidateVersion, baseVersion, response.Candidate.ContentHash, response.Candidate.BaseContentHash),
            engines,
            response.InputHash ?? string.Empty,
            response.ResultHash ?? string.Empty,
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

    private static bool TryToDomainOperationalImpact(
        KernelOperationalImpactResponse operational,
        out OperationalImpactSummary? summary,
        out string? error)
    {
        summary = null;
        error = null;
        var participantImpacts = new List<ParticipantSimulationImpact>();
        foreach (var participant in operational.ParticipantImpacts ?? [])
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
        if (operational.Hidden is not null)
        {
            foreach (var reasonText in operational.Hidden.Reasons ?? [])
            {
                if (!TryParseEnum(reasonText, "operational_impact.hidden.reasons", out MaskReason hiddenReason, out error))
                {
                    return false;
                }

                hiddenReasons.Add(hiddenReason);
            }
        }

        var hidden = operational.Hidden is { MoreExist: true } || hiddenReasons.Count > 0
            ? new HiddenObjects(operational.Hidden?.MoreExist ?? true, hiddenReasons)
            : HiddenObjects.None;

        summary = new OperationalImpactSummary(
            operational.ActiveSiteCount,
            operational.PendingSiteCount,
            operational.TotalParticipantCount,
            operational.AffectedParticipantCount,
            operational.ReconsentParticipantCount,
            participantImpacts,
            hidden);
        return true;
    }

    private static bool TryToDomainProjectionChanges(
        IReadOnlyList<KernelProjectionDatasetChangeResponse> changes,
        out IReadOnlyList<ProjectionDatasetChange>? result,
        out string? error)
    {
        result = null;
        error = null;
        var datasetList = new List<ProjectionDatasetChange>();
        foreach (var dataset in changes)
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

        result = datasetList;
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
