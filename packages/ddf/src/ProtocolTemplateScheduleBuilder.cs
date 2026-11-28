using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;

namespace Cadence.Ddf;

internal static class ProtocolTemplateScheduleBuilder
{
    private static readonly Epoch[] DefaultTemplateEpochs =
    [
        new("EP.SCREENING", "Screening", 1),
        new("EP.TREATMENT", "Treatment", 2),
        new("EP.FOLLOWUP", "Follow-Up", 3),
    ];

    private static readonly PlannedActivity[] DefaultTemplateActivities =
    [
        new("V.SCREENING", "Screening", "EP.SCREENING", [ActivityLocation.Site], [], new ActivityTiming(-7, 7, 6)),
        new("V.BASELINE", "Baseline (Day 1)", "EP.TREATMENT", [ActivityLocation.Site], [], new ActivityTiming(1, 0, 2)),
        new("V.WEEK4", "Week 4", "EP.TREATMENT", [ActivityLocation.Site], [], new ActivityTiming(28, 3, 3)),
        new("V.EOS", "End of Study", "EP.FOLLOWUP", [ActivityLocation.Site], [], new ActivityTiming(84, 7, 7)),
    ];

    public static Dictionary<string, IReadOnlyList<string>> BuildTemplateAssignmentLookup(
        IReadOnlyList<TemplateActivityAssignment>? assignments)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (assignments is null)
        {
            return map;
        }

        foreach (var assignment in assignments)
        {
            if (string.IsNullOrWhiteSpace(assignment.TemplateId))
            {
                continue;
            }

            var cleanedIds = (assignment.ActivityIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            map[assignment.TemplateId.Trim()] = cleanedIds;
        }

        return map;
    }

    public static (List<Epoch> Epochs, List<PlannedActivity> Activities) BuildTemplateSchedule(
        ProtocolDesign? baseDesign,
        IReadOnlyList<(string TemplateId, BundledProtocolTemplate? Metadata, IReadOnlyList<CollectionSpec> Specs)> specsByTemplate,
        IReadOnlyDictionary<string, IReadOnlyList<string>> assignmentsByTemplate,
        List<string> problems)
    {
        if (specsByTemplate.Count == 0)
        {
            return ([], []);
        }

        var baseActivitiesById = (baseDesign?.PlannedActivities ?? [])
            .ToDictionary(a => a.Id, StringComparer.Ordinal);
        var starterActivitiesById = DefaultTemplateActivities
            .ToDictionary(a => a.Id, StringComparer.Ordinal);
        var starterEpochsById = DefaultTemplateEpochs
            .ToDictionary(e => e.Id, StringComparer.Ordinal);

        var assignedSpecsByActivity = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void AddSpecToActivity(string activityId, string specId)
        {
            if (!assignedSpecsByActivity.TryGetValue(activityId, out var list))
            {
                list = [];
                assignedSpecsByActivity[activityId] = list;
            }

            if (!list.Contains(specId, StringComparer.Ordinal))
            {
                list.Add(specId);
            }
        }

        foreach (var (templateId, metadata, specs) in specsByTemplate)
        {
            if (assignmentsByTemplate.TryGetValue(templateId, out var explicitActivityIds))
            {
                foreach (var activityId in explicitActivityIds)
                {
                    if (!baseActivitiesById.ContainsKey(activityId) && !starterActivitiesById.ContainsKey(activityId))
                    {
                        problems.Add($"Template '{templateId}' assignment references unknown planned activity '{activityId}'.");
                        continue;
                    }

                    foreach (var spec in specs)
                    {
                        AddSpecToActivity(activityId, spec.Id);
                    }
                }

                continue;
            }

            foreach (var spec in specs)
            {
                if (spec.Repeating || metadata is { Repeating: true })
                {
                    continue;
                }

                var defaultTargetIds = SelectDefaultActivitiesForTemplate(baseDesign, metadata, spec);
                foreach (var activityId in defaultTargetIds)
                {
                    AddSpecToActivity(activityId, spec.Id);
                }
            }
        }

        if (assignedSpecsByActivity.Count == 0)
        {
            return ([], []);
        }

        var epochs = new List<Epoch>();
        var seenEpochIds = new HashSet<string>(StringComparer.Ordinal);
        var activities = new List<PlannedActivity>();

        foreach (var baseAct in baseDesign?.PlannedActivities ?? [])
        {
            if (assignedSpecsByActivity.TryGetValue(baseAct.Id, out var specIds) && specIds.Count > 0)
            {
                var combinedIds = baseAct.CollectionSpecIds
                    .Concat(specIds)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                activities.Add(baseAct with { CollectionSpecIds = combinedIds });
            }
        }

        foreach (var starterAct in DefaultTemplateActivities)
        {
            if (baseActivitiesById.ContainsKey(starterAct.Id))
            {
                continue;
            }

            if (assignedSpecsByActivity.TryGetValue(starterAct.Id, out var specIds) && specIds.Count > 0)
            {
                if (starterEpochsById.TryGetValue(starterAct.EpochId, out var starterEpoch) && seenEpochIds.Add(starterEpoch.Id))
                {
                    epochs.Add(starterEpoch);
                }

                activities.Add(starterAct with { CollectionSpecIds = specIds });
            }
        }

        return (epochs, activities);
    }

    private static IReadOnlyList<string> SelectDefaultActivitiesForTemplate(
        ProtocolDesign? baseDesign,
        BundledProtocolTemplate? metadata,
        CollectionSpec spec)
    {
        var label = $"{metadata?.Category} {metadata?.FormName} {spec.Name}".ToLowerInvariant();
        bool isScreeningOnly = label.Contains("inclusion", StringComparison.Ordinal)
            || label.Contains("exclusion", StringComparison.Ordinal)
            || label.Contains("eligibility", StringComparison.Ordinal)
            || label.Contains("medical history", StringComparison.Ordinal)
            || label.Contains("informed consent", StringComparison.Ordinal);
        bool isBaselineOnly = label.Contains("demographics", StringComparison.Ordinal)
            || label.Contains("subject characteristics", StringComparison.Ordinal)
            || label.Contains("substance use", StringComparison.Ordinal);
        bool isEndOfStudyOnly = label.Contains("disposition", StringComparison.Ordinal)
            || label.Contains("subject status", StringComparison.Ordinal)
            || label.Contains("death", StringComparison.Ordinal)
            || label.Contains("end of study", StringComparison.Ordinal);

        if (baseDesign is { PlannedActivities.Count: > 0 })
        {
            var scheduled = baseDesign.PlannedActivities
                .Where(a => !a.Repeating && a.Timing is not null)
                .ToList();
            if (scheduled.Count == 0)
            {
                scheduled = [.. baseDesign.PlannedActivities];
            }

            if (isScreeningOnly)
            {
                var scr = scheduled.FirstOrDefault(a => a.Name.Contains("screen", StringComparison.OrdinalIgnoreCase) || (a.Timing?.Day ?? 1) < 0)
                    ?? scheduled[0];
                return [scr.Id];
            }

            if (isBaselineOnly)
            {
                var bl = scheduled.FirstOrDefault(a => a.Name.Contains("baseline", StringComparison.OrdinalIgnoreCase) || a.Timing?.Day == 1)
                    ?? scheduled[0];
                return [bl.Id];
            }

            if (isEndOfStudyOnly)
            {
                return [scheduled[^1].Id];
            }

            return [.. scheduled.Select(a => a.Id)];
        }

        if (isScreeningOnly)
        {
            return ["V.SCREENING"];
        }

        if (isBaselineOnly)
        {
            return ["V.BASELINE"];
        }

        if (isEndOfStudyOnly)
        {
            return ["V.EOS"];
        }

        return ["V.SCREENING", "V.BASELINE", "V.WEEK4", "V.EOS"];
    }
}
