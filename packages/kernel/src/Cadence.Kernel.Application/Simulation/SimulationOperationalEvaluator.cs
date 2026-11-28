using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Execution;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>
/// Pure evaluator of bitemporal active/pending site counts, total/affected participant counts, per-participant schedule
/// shifts, and re-consent cohorts (CLINICAL_CANVAS §4.2, K-PROT-2, K-TIME-7, ADR-2590).
/// </summary>
internal static class SimulationOperationalEvaluator
{
    public static readonly EngineVersion Engine = new("protocol.operational_impact", "1");

    public static OperationalImpactSummary Evaluate(
        MaskedSimulationSnapshot snapshot,
        SimulationAsOf asOf,
        ProtocolDesign? baseDesign,
        ProtocolDesign? candidateDesign,
        ProtocolAmendment? amendment,
        SoaAmendmentDiff? soaDiff,
        IReadOnlySet<EnrollmentId>? additionalImpactedEnrollments = null)
    {
        var (activeSites, pendingSites) = CountSites(snapshot.VisibleSites, snapshot.VisibleEffectivities, asOf);

        var asOfDate = DateOnly.FromDateTime(asOf.ValidAt.UtcDateTime);
        var completedActivitiesByEnrollment = snapshot.VisibleCollections
            .Where(c => !string.IsNullOrWhiteSpace(c.PlannedActivityId) && c.CollectedAt <= asOf.ValidAt && c.OpenedAt <= asOf.KnownAt)
            .GroupBy(c => c.EnrollmentId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<string>)g.Select(c => c.PlannedActivityId!).ToHashSet(StringComparer.Ordinal));

        var hasEligibilityChanges = soaDiff?.ChangesEligibility is true;

        var hasBurdensomeScheduleChange = soaDiff?.Directives.Any(d =>
            d.Kind is SoaDirectiveKind.VisitAdded
                or SoaDirectiveKind.ActivityAdded
                or SoaDirectiveKind.ActivityConceptsChanged
            || (d.Kind == SoaDirectiveKind.ActivityFrequencyChanged
                && ((d.FrequencyAfter ?? 0) > (d.FrequencyBefore ?? 0) || d.CollectedAtAdded is { Count: > 0 }))) is true;

        var participantImpacts = new List<ParticipantSimulationImpact>(snapshot.VisibleEnrollments.Count);
        var affectedCount = 0;
        var reconsentCount = 0;

        foreach (var enrollment in snapshot.VisibleEnrollments
            .OrderBy(e => e.SubjectCode, StringComparer.Ordinal)
            .ThenBy(e => e.Id.Value))
        {
            var isTerminal = enrollment.StatusAsOf is EnrollmentStatus.ScreenFailed
                or EnrollmentStatus.Completed
                or EnrollmentStatus.Withdrawn;

            IReadOnlyList<ParticipantScheduleShift> shifts = [];
            if (!isTerminal && candidateDesign is not null)
            {
                var completed = completedActivitiesByEnrollment.TryGetValue(enrollment.Id, out var done)
                    ? done
                    : new HashSet<string>(StringComparer.Ordinal);
                shifts = ComputeParticipantScheduleShifts(
                    baseDesign,
                    candidateDesign,
                    enrollment.ReferenceStartDate,
                    asOfDate,
                    completed);
            }

            var hasDataOrRuleImpact = additionalImpactedEnrollments?.Contains(enrollment.Id) is true;
            var (cohort, requiresReconsent) = ClassifyCohort(
                enrollment.StatusAsOf,
                isTerminal,
                amendment?.TransitionPolicy,
                hasEligibilityChanges,
                hasBurdensomeScheduleChange,
                shifts.Count > 0,
                hasDataOrRuleImpact);

            if (cohort == ParticipantImpactCohort.CohortSplit && enrollment.StatusAsOf != EnrollmentStatus.Screening)
            {
                // Under CohortSplit, already-enrolled participants stay on the parent version without schedule shifts.
                shifts = [];
            }

            var isAffected = !isTerminal && (requiresReconsent
                || shifts.Count > 0
                || cohort == ParticipantImpactCohort.EligibilityReview
                || hasDataOrRuleImpact);

            if (isAffected)
            {
                affectedCount++;
            }

            if (requiresReconsent)
            {
                reconsentCount++;
            }

            var effectiveCohort = isAffected ? cohort : ParticipantImpactCohort.Unaffected;
            participantImpacts.Add(new ParticipantSimulationImpact(
                NodeRef.For(enrollment.Id),
                enrollment.SubjectCode,
                enrollment.StatusAsOf,
                effectiveCohort,
                requiresReconsent,
                shifts));
        }

        return new OperationalImpactSummary(
            activeSites,
            pendingSites,
            snapshot.VisibleEnrollments.Count,
            affectedCount,
            reconsentCount,
            participantImpacts,
            snapshot.Hidden);
    }

    private static (int ActiveSiteCount, int PendingSiteCount) CountSites(
        IReadOnlyList<SimulationSiteSnapshot> visibleSites,
        IReadOnlyList<SimulationEffectivitySnapshot> visibleEffectivities,
        SimulationAsOf asOf)
    {
        var active = 0;
        var pending = 0;
        var validAt = KernelTimestamps.ToStoredPrecision(asOf.ValidAt);
        var knownAt = KernelTimestamps.ToStoredPrecision(asOf.KnownAt);

        foreach (var site in visibleSites)
        {
            if (KernelTimestamps.ToStoredPrecision(site.CreatedAt) > knownAt
                || KernelTimestamps.ToStoredPrecision(site.CreatedAt) > validAt)
            {
                continue;
            }

            var isSiteActivated = site.IsActive && string.Equals(site.Status, "Active", StringComparison.OrdinalIgnoreCase);
            var isSiteSelectedOrPending = string.Equals(site.Status, "Selected", StringComparison.OrdinalIgnoreCase)
                || string.Equals(site.Status, "Pending", StringComparison.OrdinalIgnoreCase);

            var covering = visibleEffectivities
                .Where(e => e.Scope.Covers(site.Id, site.Country) && e.StatusAsOf is not null)
                .GroupBy(e => e.ProtocolVersionId)
                .Select(g => g.MaxBy(e => (int)e.Scope.Level)!.StatusAsOf!.Value)
                .ToList();

            if (covering.Count > 0)
            {
                if (covering.Contains(EffectivityStatus.Effective))
                {
                    if (isSiteActivated)
                    {
                        active++;
                    }
                    else if (isSiteSelectedOrPending)
                    {
                        pending++;
                    }
                }
                else if ((isSiteActivated || isSiteSelectedOrPending)
                    && covering.Any(s => s is EffectivityStatus.Pending
                        or EffectivityStatus.RegulatoryApproved
                        or EffectivityStatus.EthicsApproved))
                {
                    pending++;
                }

                continue;
            }

            if (isSiteActivated)
            {
                active++;
            }
            else if (site.IsActive && isSiteSelectedOrPending)
            {
                pending++;
            }
        }

        return (active, pending);
    }

    private static IReadOnlyList<ParticipantScheduleShift> ComputeParticipantScheduleShifts(
        ProtocolDesign? baseDesign,
        ProtocolDesign candidateDesign,
        DateOnly referenceStart,
        DateOnly asOfDate,
        IReadOnlySet<string> completedActivities)
    {
        var before = baseDesign is null ? [] : ExpectedSchedule.Compute(baseDesign, referenceStart, asOfDate);
        var after = ExpectedSchedule.Compute(candidateDesign, referenceStart, asOfDate);
        var beforeById = before.ToDictionary(v => v.Visit.ActivityId, StringComparer.Ordinal);
        var afterById = after.ToDictionary(v => v.Visit.ActivityId, StringComparer.Ordinal);

        var shifts = new List<ParticipantScheduleShift>();
        foreach (var id in beforeById.Keys.Union(afterById.Keys).Order(StringComparer.Ordinal))
        {
            if (completedActivities.Contains(id))
            {
                continue;
            }

            var old = beforeById.GetValueOrDefault(id);
            var @new = afterById.GetValueOrDefault(id);

            if (old is null && @new is not null)
            {
                if (@new.WindowEnd >= asOfDate)
                {
                    shifts.Add(new ParticipantScheduleShift(id, ElementChangeKind.Added, null, ToParticipantVisit(@new)));
                }
            }
            else if (old is not null && @new is null)
            {
                if (old.WindowEnd >= asOfDate)
                {
                    shifts.Add(new ParticipantScheduleShift(id, ElementChangeKind.Removed, ToParticipantVisit(old), null));
                }
            }
            else if (old is not null && @new is not null
                && (old.Target != @new.Target || old.WindowStart != @new.WindowStart || old.WindowEnd != @new.WindowEnd)
                && (old.WindowEnd >= asOfDate || @new.WindowEnd >= asOfDate))
            {
                shifts.Add(new ParticipantScheduleShift(id, ElementChangeKind.Changed, ToParticipantVisit(old), ToParticipantVisit(@new)));
            }
        }

        return shifts;
    }

    private static ParticipantPlannedVisit ToParticipantVisit(ExpectedVisit visit) =>
        new(visit.Visit.ActivityId, visit.Target, visit.WindowStart, visit.WindowEnd);

    private static (ParticipantImpactCohort Cohort, bool RequiresReconsent) ClassifyCohort(
        EnrollmentStatus status,
        bool isTerminal,
        TransitionPolicy? policy,
        bool hasEligibilityChanges,
        bool hasBurdensomeScheduleChange,
        bool hasScheduleShifts,
        bool hasDataOrRuleImpact)
    {
        if (isTerminal)
        {
            return (ParticipantImpactCohort.Unaffected, false);
        }

        if (policy == TransitionPolicy.RequiresReconsent)
        {
            return (ParticipantImpactCohort.RequiresReconsent, true);
        }

        if (status == EnrollmentStatus.Screening && hasEligibilityChanges)
        {
            return (ParticipantImpactCohort.EligibilityReview, false);
        }

        if (policy == TransitionPolicy.NewParticipantsOnly)
        {
            if (status == EnrollmentStatus.Screening && (hasScheduleShifts || hasDataOrRuleImpact))
            {
                return (ParticipantImpactCohort.CohortSplit, false);
            }

            return (ParticipantImpactCohort.Unaffected, false);
        }

        if (hasBurdensomeScheduleChange && hasScheduleShifts && policy is null)
        {
            return (ParticipantImpactCohort.RequiresReconsent, true);
        }

        if (policy == TransitionPolicy.ByCohort)
        {
            if (hasScheduleShifts || hasDataOrRuleImpact)
            {
                return (ParticipantImpactCohort.CohortSplit, false);
            }

            return (ParticipantImpactCohort.Unaffected, false);
        }

        if (hasScheduleShifts || hasDataOrRuleImpact)
        {
            return (ParticipantImpactCohort.AutomaticTransition, false);
        }

        return (ParticipantImpactCohort.Unaffected, false);
    }
}
