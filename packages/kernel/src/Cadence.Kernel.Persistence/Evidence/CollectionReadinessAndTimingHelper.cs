using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Persistence.Evidence;

internal static class CollectionReadinessAndTimingHelper
{
    internal static VisitTimingAssessment? AssessVisitTiming(
        ProtocolDesign? design,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        DateTimeOffset? enrollmentReferenceAt) =>
        design is null || string.IsNullOrWhiteSpace(plannedActivityId) || enrollmentReferenceAt is null
            ? null
            : AssessVisitTiming(ExpectedSchedule.IndexByActivity(design), plannedActivityId, collectedAt, enrollmentReferenceAt);

    internal static VisitTimingAssessment? AssessVisitTiming(
        IReadOnlyDictionary<string, PlannedVisit>? visitsByActivity,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        DateTimeOffset? enrollmentReferenceAt)
    {
        if (visitsByActivity is null
            || string.IsNullOrWhiteSpace(plannedActivityId)
            || enrollmentReferenceAt is not { } anchor
            || !visitsByActivity.TryGetValue(plannedActivityId, out var planned))
        {
            return null;
        }

        var referenceStart = DateOnly.FromDateTime(anchor.DateTime);
        var actualDate = DateOnly.FromDateTime(collectedAt.ToOffset(anchor.Offset).DateTime);
        var expected = ExpectedSchedule.ComputeVisit(planned, referenceStart, actualDate);
        return ExpectedSchedule.Assess(expected, actualDate);
    }

    internal static DateTimeOffset? ResolveEnrollmentReferenceAt(Enrollment? enrollment)
    {
        if (enrollment is null)
        {
            return null;
        }

        var enrolledPeriod = enrollment.Periods
            .Where(p => p.Status == Cadence.Kernel.Execution.EnrollmentStatus.Enrolled && p.Period.Recorded.End is null)
            .OrderBy(p => p.Period.Valid.Start)
            .FirstOrDefault();
        return enrolledPeriod?.Period.Valid.Start ?? enrollment.StatusSince;
    }

    internal static List<AssertionView> FilterFormAssertions(
        IReadOnlyList<AssertionView> allAssertions,
        IReadOnlySet<Guid> memberObservationGuids)
    {
        var formQueryGuids = allAssertions
            .Where(a => a.Kind == AssertionKind.Queried && a.Target.Kind == NodeKinds.Observation && memberObservationGuids.Contains(a.Target.Id))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();
        return [.. allAssertions
            .Where(a => (a.Target.Kind == NodeKinds.Observation && memberObservationGuids.Contains(a.Target.Id))
                || (a.Target.Kind == NodeKinds.Assertion && formQueryGuids.Contains(a.Target.Id)))];
    }

    internal static (IReadOnlySet<AssertionId> ActiveIds, IReadOnlySet<AssertionId> AnsweredIds) ExtractActiveQueryAssertionIds(
        IReadOnlyList<AssertionView> assertions)
    {
        var activeIds = assertions
            .Where(a => a.Kind == AssertionKind.Queried && a.RetractedAt is null)
            .Select(a => a.AssertionId)
            .ToFrozenSet();
        if (activeIds.Count == 0)
        {
            return (FrozenSet<AssertionId>.Empty, FrozenSet<AssertionId>.Empty);
        }

        var answeredIds = assertions
            .Where(a => a.Kind == AssertionKind.QueryAnswered
                && a.RetractedAt is null
                && a.Target.Kind == NodeKinds.Assertion
                && activeIds.Contains(AssertionId.FromGuid(a.Target.Id)))
            .Select(a => AssertionId.FromGuid(a.Target.Id))
            .ToFrozenSet();
        return (activeIds, answeredIds);
    }

    internal static (int OpenCount, int AnsweredCount) CountActiveQueries(
        IReadOnlyList<AssertionView> assertions,
        IReadOnlySet<Guid>? excludeQueryIds = null)
    {
        var activeQueryIds = assertions
            .Where(a => a.Kind == AssertionKind.Queried
                && a.RetractedAt is null
                && (excludeQueryIds is null || !excludeQueryIds.Contains(a.AssertionId.Value)))
            .Select(a => a.AssertionId.Value)
            .ToHashSet();
        if (activeQueryIds.Count == 0)
        {
            return (0, 0);
        }

        var answeredQueryIds = assertions
            .Where(a => a.Kind == AssertionKind.QueryAnswered
                && a.RetractedAt is null
                && a.Target.Kind == NodeKinds.Assertion
                && activeQueryIds.Contains(a.Target.Id))
            .Select(a => a.Target.Id)
            .ToHashSet();
        var answeredCount = answeredQueryIds.Count;
        return (activeQueryIds.Count - answeredCount, answeredCount);
    }

    internal static int CountFormErrorFindings(
        Guid instanceGuid,
        IReadOnlySet<Guid> memberObservationGuids,
        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets) =>
        activeErrorTargets.Count(entry =>
            (entry.Target.Kind == FindingTargetKind.DatasetRecord
                && Guid.TryParse(entry.Target.Key, out var recordGuid)
                && recordGuid == instanceGuid)
            || (entry.Target.Kind == FindingTargetKind.Observation
                && entry.Target.Node is { Kind: NodeKinds.Observation } obsNode
                && memberObservationGuids.Contains(obsNode.Id)));

    internal static (
        bool Complete,
        IReadOnlyList<string> MissingRequired,
        int OpenQueries,
        int AnsweredQueries,
        int ErrorFindings,
        IReadOnlyList<string> Blockers) EvaluateFormReadiness(
        IReadOnlyList<CollectionItem> items,
        IReadOnlyList<CollectedValueView> values,
        IReadOnlyList<AssertionView> assertions,
        Guid instanceGuid,
        IReadOnlySet<Guid> memberGuids,
        IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)> activeErrorTargets) =>
        EvaluateFormReadinessWithErrorCount(
            items,
            values,
            assertions,
            CountFormErrorFindings(instanceGuid, memberGuids, activeErrorTargets));

    internal static (
        bool Complete,
        IReadOnlyList<string> MissingRequired,
        int OpenQueries,
        int AnsweredQueries,
        int ErrorFindings,
        IReadOnlyList<string> Blockers) EvaluateFormReadinessWithErrorCount(
        IReadOnlyList<CollectionItem> items,
        IReadOnlyList<CollectedValueView> values,
        IReadOnlyList<AssertionView> assertions,
        int errorFindings)
    {
        var recordedItems = values
            .Where(v => v.Value is not MissingValue { Reason: NullFlavor.NotAsked })
            .Select(v => v.ItemId)
            .ToHashSet(StringComparer.Ordinal);
        var missingRequired = items
            .Where(i => i.Required && !recordedItems.Contains(i.Id))
            .Select(i => i.Id)
            .ToList();
        var (openQueries, answeredQueries) = CountActiveQueries(assertions);
        var complete = (items.Count == 0 || recordedItems.Count > 0)
            && CollectionReadiness.IsComplete(missingRequired.Count, openQueries, answeredQueries, errorFindings);
        var blockers = CollectionReadiness.BuildBlockers(missingRequired, missingRequired.Count, openQueries, answeredQueries, errorFindings);
        return (complete, missingRequired, openQueries, answeredQueries, errorFindings, blockers);
    }
}
