using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Protocol.Design;

namespace Cadence.Kernel.Application.Protocol;

/// <summary>
/// Real-time Schedule of Activities (SoA) matrix gap analyzer and ICH M11 template checker.
/// Evaluates a candidate USDM <see cref="ProtocolDesign"/> graph strictly in memory and returns
/// exact JSON Pointer paths for all identified structural gaps and template violations.
/// </summary>
public static class MatrixGapAnalyzer
{
    /// <summary>
    /// Analyzes <paramref name="design"/> for schedule matrix gaps, unlinked endpoints, orphan encounters,
    /// unscheduled safety rules, epoch cycle defects, and ICH M11 template non-conformances.
    /// </summary>
    public static IReadOnlyList<MatrixGapFinding> Analyze(ProtocolDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        var findings = new List<MatrixGapFinding>();

        var objectiveIds = design.Objectives.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var endpointIds = design.Endpoints.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var estimandIds = design.Estimands.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var epochIds = design.Epochs.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var collectionSpecIds = design.CollectionSpecs.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        // 1. ICH M11 Template and Core USDM Constraints
        CheckIchM11TemplateRequirements(design, findings);

        // 2. Unlinked Endpoints and Objectives
        CheckUnlinkedEndpoints(design, objectiveIds, endpointIds, estimandIds, findings);

        // 3. Orphan Encounters and Activities
        CheckOrphanEncounters(design, epochIds, collectionSpecIds, findings);

        // 4. Unscheduled Safety Procedures
        CheckUnscheduledSafetyProcedures(design, findings);

        // 5. Epoch Order and Transition Cycle Analysis
        CheckEpochTransitions(design, findings);

        return findings;
    }

    private static void CheckIchM11TemplateRequirements(ProtocolDesign design, List<MatrixGapFinding> findings)
    {
        if (design.Objectives.Count == 0)
        {
            findings.Add(new MatrixGapFinding(
                "design",
                MatrixGapKind.IchM11Violation,
                MatrixGapSeverity.Error,
                "/objectives",
                "ICH M11 requires at least one study objective.",
                "Define a primary study objective."));
        }
        else if (!design.Objectives.Any(o => o.Level == ObjectiveLevel.Primary))
        {
            findings.Add(new MatrixGapFinding(
                "design",
                MatrixGapKind.IchM11Violation,
                MatrixGapSeverity.Error,
                "/objectives",
                "ICH M11 requires a primary study objective.",
                "Set the level of at least one objective to Primary."));
        }

        if (design.Endpoints.Count == 0)
        {
            findings.Add(new MatrixGapFinding(
                "design",
                MatrixGapKind.IchM11Violation,
                MatrixGapSeverity.Error,
                "/endpoints",
                "ICH M11 requires at least one clinical endpoint.",
                "Define a primary endpoint measuring the primary objective."));
        }
        else if (!design.Endpoints.Any(e => e.Level == ObjectiveLevel.Primary))
        {
            findings.Add(new MatrixGapFinding(
                "design",
                MatrixGapKind.IchM11Violation,
                MatrixGapSeverity.Error,
                "/endpoints",
                "ICH M11 requires a primary endpoint.",
                "Set the level of at least one endpoint to Primary."));
        }

        if (design.Arms.Count == 0)
        {
            findings.Add(new MatrixGapFinding(
                "design",
                MatrixGapKind.IchM11Violation,
                MatrixGapSeverity.Error,
                "/arms",
                "ICH M11 requires at least one study arm.",
                "Add a study arm (e.g., Experimental or PlaceboComparator)."));
        }

        if (design.Epochs.Count == 0)
        {
            findings.Add(new MatrixGapFinding(
                "design",
                MatrixGapKind.IchM11Violation,
                MatrixGapSeverity.Error,
                "/epochs",
                "ICH M11 requires at least one study epoch.",
                "Add study epochs (e.g., Screening, Treatment, Follow-up)."));
        }

        if (design.Populations.Count == 0)
        {
            findings.Add(new MatrixGapFinding(
                "design",
                MatrixGapKind.IchM11Violation,
                MatrixGapSeverity.Warning,
                "/populations",
                "ICH M11 requires study population definitions.",
                "Define at least one study population."));
        }

        var hasInclusion = design.EligibilityCriteria.Any(c => c.Category == CriterionCategory.Inclusion);
        var hasExclusion = design.EligibilityCriteria.Any(c => c.Category == CriterionCategory.Exclusion);

        if (!hasInclusion)
        {
            findings.Add(new MatrixGapFinding(
                "design",
                MatrixGapKind.IchM11Violation,
                MatrixGapSeverity.Error,
                "/eligibilityCriteria",
                "ICH M11 requires inclusion criteria.",
                "Add at least one inclusion criterion."));
        }

        if (!hasExclusion)
        {
            findings.Add(new MatrixGapFinding(
                "design",
                MatrixGapKind.IchM11Violation,
                MatrixGapSeverity.Error,
                "/eligibilityCriteria",
                "ICH M11 requires exclusion criteria.",
                "Add at least one exclusion criterion."));
        }
    }

    private static void CheckUnlinkedEndpoints(
        ProtocolDesign design,
        HashSet<string> objectiveIds,
        HashSet<string> endpointIds,
        HashSet<string> estimandIds,
        List<MatrixGapFinding> findings)
    {
        // Endpoints without valid Objective reference
        for (var i = 0; i < design.Endpoints.Count; i++)
        {
            var endpoint = design.Endpoints[i];
            if (string.IsNullOrWhiteSpace(endpoint.ObjectiveId) || !objectiveIds.Contains(endpoint.ObjectiveId))
            {
                findings.Add(new MatrixGapFinding(
                    endpoint.Id,
                    MatrixGapKind.UnlinkedEndpoint,
                    MatrixGapSeverity.Error,
                    $"/endpoints/{i}/objectiveId",
                    $"Endpoint '{endpoint.Id}' is not linked to a valid objective.",
                    "Assign a valid objective ID to this endpoint."));
            }
        }

        // Planned activities purpose check
        for (var i = 0; i < design.PlannedActivities.Count; i++)
        {
            var activity = design.PlannedActivities[i];
            var purpose = activity.Purpose;

            if (purpose is null || (purpose.EndpointIds.Count == 0 && purpose.EstimandIds.Count == 0 && purpose.SafetyRuleIds.Count == 0 && string.IsNullOrWhiteSpace(purpose.OperationalNeed)))
            {
                findings.Add(new MatrixGapFinding(
                    activity.Id,
                    MatrixGapKind.UnlinkedEndpoint,
                    MatrixGapSeverity.Warning,
                    $"/plannedActivities/{i}/purpose",
                    $"Planned activity '{activity.Name}' ({activity.Id}) has no declared endpoint, estimand, safety rule, or operational purpose.",
                    "Link this planned activity to an endpoint or specify its data purpose."));
            }
            else
            {
                for (var j = 0; j < purpose.EndpointIds.Count; j++)
                {
                    var epId = purpose.EndpointIds[j];
                    if (!endpointIds.Contains(epId))
                    {
                        findings.Add(new MatrixGapFinding(
                            activity.Id,
                            MatrixGapKind.UnlinkedEndpoint,
                            MatrixGapSeverity.Error,
                            $"/plannedActivities/{i}/purpose/endpointIds/{j}",
                            $"Planned activity '{activity.Id}' references endpoint '{epId}' which does not exist in the design.",
                            "Fix or remove the unlinked endpoint ID reference."));
                    }
                }

                for (var j = 0; j < purpose.EstimandIds.Count; j++)
                {
                    var estId = purpose.EstimandIds[j];
                    if (!estimandIds.Contains(estId))
                    {
                        findings.Add(new MatrixGapFinding(
                            activity.Id,
                            MatrixGapKind.UnlinkedEndpoint,
                            MatrixGapSeverity.Error,
                            $"/plannedActivities/{i}/purpose/estimandIds/{j}",
                            $"Planned activity '{activity.Id}' references estimand '{estId}' which does not exist in the design.",
                            "Fix or remove the unlinked estimand ID reference."));
                    }
                }
            }
        }

        // Collection specs purpose check
        for (var i = 0; i < design.CollectionSpecs.Count; i++)
        {
            var spec = design.CollectionSpecs[i];
            var specPurpose = spec.Purpose;
            var itemsHavePurpose = spec.Items.Any(item => item.Purpose is not null);

            if (specPurpose is null && !itemsHavePurpose)
            {
                findings.Add(new MatrixGapFinding(
                    spec.Id,
                    MatrixGapKind.UnlinkedEndpoint,
                    MatrixGapSeverity.Warning,
                    $"/collectionSpecs/{i}/purpose",
                    $"Collection spec '{spec.Name}' ({spec.Id}) has no data purpose linked to clinical endpoints or safety rules.",
                    "Define a data purpose linking this assessment to an endpoint or safety rule."));
            }
        }
    }

    private static void CheckOrphanEncounters(
        ProtocolDesign design,
        HashSet<string> epochIds,
        HashSet<string> collectionSpecIds,
        List<MatrixGapFinding> findings)
    {
        var referencedEpochs = new HashSet<string>(StringComparer.Ordinal);
        var referencedSpecs = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < design.PlannedActivities.Count; i++)
        {
            var activity = design.PlannedActivities[i];

            if (string.IsNullOrWhiteSpace(activity.EpochId) || !epochIds.Contains(activity.EpochId))
            {
                findings.Add(new MatrixGapFinding(
                    activity.Id,
                    MatrixGapKind.OrphanEncounter,
                    MatrixGapSeverity.Error,
                    $"/plannedActivities/{i}/epochId",
                    $"Planned activity '{activity.Name}' ({activity.Id}) is not assigned to a valid study epoch.",
                    "Assign a valid epoch ID to this activity."));
            }
            else
            {
                referencedEpochs.Add(activity.EpochId);
            }

            if (activity.CollectionSpecIds.Count == 0)
            {
                findings.Add(new MatrixGapFinding(
                    activity.Id,
                    MatrixGapKind.OrphanEncounter,
                    MatrixGapSeverity.Warning,
                    $"/plannedActivities/{i}/collectionSpecIds",
                    $"Planned activity '{activity.Name}' ({activity.Id}) has no collection specifications attached.",
                    "Attach at least one collection specification or remove the empty visit."));
            }
            else
            {
                foreach (var specId in activity.CollectionSpecIds)
                {
                    if (!collectionSpecIds.Contains(specId))
                    {
                        findings.Add(new MatrixGapFinding(
                            activity.Id,
                            MatrixGapKind.OrphanEncounter,
                            MatrixGapSeverity.Error,
                            $"/plannedActivities/{i}/collectionSpecIds",
                            $"Planned activity '{activity.Id}' references missing collection specification '{specId}'.",
                            "Correct the collection specification reference."));
                    }
                    else
                    {
                        referencedSpecs.Add(specId);
                    }
                }
            }
        }

        // Epochs with no activities
        for (var i = 0; i < design.Epochs.Count; i++)
        {
            var epoch = design.Epochs[i];
            if (!referencedEpochs.Contains(epoch.Id))
            {
                findings.Add(new MatrixGapFinding(
                    epoch.Id,
                    MatrixGapKind.OrphanEncounter,
                    MatrixGapSeverity.Warning,
                    $"/epochs/{i}",
                    $"Epoch '{epoch.Name}' ({epoch.Id}) contains no scheduled planned activities or visits.",
                    "Add planned activities to this epoch or remove the unused epoch."));
            }
        }

        // Unreferenced collection specs
        for (var i = 0; i < design.CollectionSpecs.Count; i++)
        {
            var spec = design.CollectionSpecs[i];
            if (!referencedSpecs.Contains(spec.Id))
            {
                findings.Add(new MatrixGapFinding(
                    spec.Id,
                    MatrixGapKind.OrphanEncounter,
                    MatrixGapSeverity.Info,
                    $"/collectionSpecs/{i}",
                    $"Collection specification '{spec.Name}' ({spec.Id}) is defined but not included in any planned activity.",
                    "Link this collection spec to a planned activity in the Schedule grid."));
            }
        }
    }

    private static void CheckUnscheduledSafetyProcedures(
        ProtocolDesign design,
        List<MatrixGapFinding> findings)
    {
        var scheduledSafetyRules = new HashSet<string>(StringComparer.Ordinal);

        foreach (var activity in design.PlannedActivities)
        {
            if (activity.Purpose is { } purpose)
            {
                foreach (var sId in purpose.SafetyRuleIds)
                {
                    scheduledSafetyRules.Add(sId);
                }
            }
        }

        foreach (var spec in design.CollectionSpecs)
        {
            if (spec.Purpose is { } purpose)
            {
                foreach (var sId in purpose.SafetyRuleIds)
                {
                    scheduledSafetyRules.Add(sId);
                }
            }
            foreach (var item in spec.Items)
            {
                if (item.Purpose is { } itemPurpose)
                {
                    foreach (var sId in itemPurpose.SafetyRuleIds)
                    {
                        scheduledSafetyRules.Add(sId);
                    }
                }
            }
        }

        for (var i = 0; i < design.SafetyRules.Count; i++)
        {
            var rule = design.SafetyRules[i];
            if (!scheduledSafetyRules.Contains(rule.Id))
            {
                findings.Add(new MatrixGapFinding(
                    rule.Id,
                    MatrixGapKind.UnscheduledSafetyProcedure,
                    MatrixGapSeverity.Warning,
                    $"/safetyRules/{i}",
                    $"Safety rule '{rule.Id}' is defined but is not linked to any scheduled activity or collection procedure.",
                    "Link this safety rule to a planned safety assessment activity in the Schedule."));
            }
        }
    }

    private static void CheckEpochTransitions(ProtocolDesign design, List<MatrixGapFinding> findings)
    {
        if (design.Epochs.Count < 2)
        {
            return;
        }

        var orders = new Dictionary<int, string>();
        for (var i = 0; i < design.Epochs.Count; i++)
        {
            var epoch = design.Epochs[i];
            if (orders.TryGetValue(epoch.Order, out var duplicateEpochId))
            {
                findings.Add(new MatrixGapFinding(
                    epoch.Id,
                    MatrixGapKind.CyclicEpochTransition,
                    MatrixGapSeverity.Error,
                    $"/epochs/{i}/order",
                    $"Epoch '{epoch.Name}' ({epoch.Id}) shares the same order index ({epoch.Order}) as epoch '{duplicateEpochId}'. Epoch sequence must be strictly monotonic.",
                    "Assign unique sequential order integers to epochs."));
            }
            else
            {
                orders[epoch.Order] = epoch.Id;
            }
        }

        // Check activity study days across epoch order progression
        var epochOrderMap = design.Epochs.ToDictionary(e => e.Id, e => e.Order, StringComparer.Ordinal);
        var activitiesWithTiming = design.PlannedActivities
            .Where(a => a.Timing is not null && epochOrderMap.ContainsKey(a.EpochId))
            .OrderBy(a => epochOrderMap[a.EpochId])
            .ThenBy(a => a.Timing!.Day)
            .ToList();

        for (var i = 1; i < activitiesWithTiming.Count; i++)
        {
            var prev = activitiesWithTiming[i - 1];
            var curr = activitiesWithTiming[i];

            var prevOrder = epochOrderMap[prev.EpochId];
            var currOrder = epochOrderMap[curr.EpochId];

            if (currOrder > prevOrder && curr.Timing!.Day < prev.Timing!.Day)
            {
                var currIndex = GetActivityIndex(design.PlannedActivities, curr);
                findings.Add(new MatrixGapFinding(
                    curr.Id,
                    MatrixGapKind.CyclicEpochTransition,
                    MatrixGapSeverity.Warning,
                    $"/plannedActivities/{currIndex}/timing/day",
                    $"Activity '{curr.Name}' in epoch '{curr.EpochId}' (day {curr.Timing!.Day}) is scheduled before activity '{prev.Name}' in previous epoch '{prev.EpochId}' (day {prev.Timing!.Day}). Check for cyclic epoch timing.",
                    "Adjust study day timing so epoch progression is chronological."));
            }
        }
    }

    private static int GetActivityIndex(IReadOnlyList<PlannedActivity> activities, PlannedActivity target)
    {
        for (var i = 0; i < activities.Count; i++)
        {
            if (activities[i].Id == target.Id)
            {
                return i;
            }
        }
        return 0;
    }
}
