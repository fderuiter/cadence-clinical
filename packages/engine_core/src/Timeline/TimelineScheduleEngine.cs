using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cadence.Core.Domain.Timeline;

/// <summary>
/// Pure mathematical execution engine for Schedule of Activities (SoA) Vector Timelines.
/// Evaluates relative anchor event offsets, asymmetric window envelopes, cycle template
/// expansions, predecessor DAG chaining, and automated deviation classifications under 21 CFR Part 11.
/// @req:PRD-SYS-053
/// </summary>
public class TimelineScheduleEngine
{
    private readonly TimelineScheduleDefinition _definition;

    public TimelineScheduleDefinition Definition => _definition;

    public TimelineScheduleEngine(TimelineScheduleDefinition? definition = null)
    {
        _definition = definition ?? new TimelineScheduleDefinition(studyId: "DEFAULT_STUDY");
    }

    public static DateOnly ParseDate(object dt)
    {
        if (dt is DateOnly d)
        {
            return d;
        }
        if (dt is DateTime datetime)
        {
            return DateOnly.FromDateTime(datetime);
        }
        if (dt is string s)
        {
            string cleaned = s.Trim().Split('T')[0];
            if (DateOnly.TryParse(cleaned, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
            if (DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDt))
            {
                return DateOnly.FromDateTime(parsedDt);
            }
            throw new ArgumentException($"Invalid date format: {s}");
        }
        throw new ArgumentException($"Invalid date format: {dt}");
    }

    public static List<EncounterScheduleItem> ExpandCycleTemplates(
        CycleTemplateDefinition template,
        AnchorEventType anchorEvent = AnchorEventType.CYCLE_START,
        int startCycle = 1)
    {
        ArgumentNullException.ThrowIfNull(template);

        var expanded = new List<EncounterScheduleItem>();
        for (int cycleIdx = startCycle; cycleIdx < startCycle + template.CycleCount; cycleIdx++)
        {
            int cycleBaseOffset = (cycleIdx - 1) * template.CycleLengthDays;

            foreach (var item in template.EncountersPerCycle)
            {
                string encId = $"{item.EncounterId}_C{cycleIdx}";
                string encName = item.Name.Contains("Cycle", StringComparison.OrdinalIgnoreCase)
                    ? item.Name
                    : $"Cycle {cycleIdx} {item.Name}";

                int targetOffset = cycleBaseOffset + item.TargetDayOffset;

                expanded.Add(new EncounterScheduleItem(
                    EncounterId: encId,
                    Name: encName,
                    AnchorEvent: anchorEvent,
                    PredecessorEncounterId: item.PredecessorEncounterId,
                    TargetDayOffset: targetOffset,
                    Window: item.Window,
                    IsMandatory: item.IsMandatory,
                    EpochId: item.EpochId,
                    CycleNumber: cycleIdx
                ));
            }
        }

        return expanded;
    }

    public List<EncounterScheduleItem> GetAllEncounters()
    {
        var allItems = new List<EncounterScheduleItem>(_definition.Encounters);
        foreach (var template in _definition.CycleTemplates)
        {
            allItems.AddRange(ExpandCycleTemplates(template));
        }
        return allItems;
    }

    public List<CalculatedVisitSchedule> ComputeSubjectSchedule(
        IReadOnlyDictionary<string, object> anchorDates,
        IReadOnlyDictionary<string, object>? completedVisitDates = null,
        IEnumerable<EncounterScheduleItem>? encounters = null)
    {
        ArgumentNullException.ThrowIfNull(anchorDates);

        var normalizedAnchors = new Dictionary<string, DateOnly>();
        foreach (var kvp in anchorDates)
        {
            normalizedAnchors[kvp.Key.ToUpperInvariant()] = ParseDate(kvp.Value);
        }

        var completed = new Dictionary<string, DateOnly>();
        if (completedVisitDates != null)
        {
            foreach (var kvp in completedVisitDates)
            {
                completed[kvp.Key] = ParseDate(kvp.Value);
            }
        }

        var items = encounters != null ? encounters.ToList() : GetAllEncounters();
        var calculated = new List<CalculatedVisitSchedule>();

        foreach (var item in items)
        {
            string anchorKey = item.AnchorEvent.ToString().ToUpperInvariant();
            DateOnly? anchorDt = null;
            string anchorUsedName = item.AnchorEvent.ToString();

            // Check dynamic predecessor visit chaining
            if (item.AnchorEvent == AnchorEventType.PREVIOUS_VISIT && !string.IsNullOrEmpty(item.PredecessorEncounterId))
            {
                if (completed.TryGetValue(item.PredecessorEncounterId, out var predCompletedDt))
                {
                    anchorDt = predCompletedDt;
                    anchorUsedName = $"PREDECESSOR({item.PredecessorEncounterId})";
                }
                else if (normalizedAnchors.TryGetValue(item.PredecessorEncounterId.ToUpperInvariant(), out var predAnchorDt))
                {
                    anchorDt = predAnchorDt;
                    anchorUsedName = $"PREDECESSOR({item.PredecessorEncounterId})";
                }
            }

            // Standard anchor event lookup
            if (!anchorDt.HasValue)
            {
                if (normalizedAnchors.TryGetValue(anchorKey, out var foundDt))
                {
                    anchorDt = foundDt;
                }
                else if (normalizedAnchors.TryGetValue("FIRST_DOSE", out var firstDoseDt))
                {
                    anchorDt = firstDoseDt;
                    anchorUsedName = "FIRST_DOSE_FALLBACK";
                }
                else if (normalizedAnchors.TryGetValue("RANDOMIZATION", out var randDt))
                {
                    anchorDt = randDt;
                    anchorUsedName = "RANDOMIZATION_FALLBACK";
                }
                else if (normalizedAnchors.TryGetValue("SCREENING", out var screenDt))
                {
                    anchorDt = screenDt;
                    anchorUsedName = "SCREENING_FALLBACK";
                }
            }

            if (!anchorDt.HasValue)
            {
                continue;
            }

            DateOnly targetDt = anchorDt.Value.AddDays(item.TargetDayOffset);
            DateOnly windowStart = targetDt.AddDays(-item.Window.WindowBeforeDays);
            DateOnly windowEnd = targetDt.AddDays(item.Window.WindowAfterDays);

            calculated.Add(new CalculatedVisitSchedule(
                EncounterId: item.EncounterId,
                Name: item.Name,
                TargetDate: targetDt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                WindowStartDate: windowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                WindowEndDate: windowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                AnchorUsed: anchorUsedName,
                AnchorDate: anchorDt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                CycleNumber: item.CycleNumber,
                IsMandatory: item.IsMandatory,
                EpochId: item.EpochId
            ));
        }

        return calculated.OrderBy(s => s.TargetDate).ToList();
    }

    public VisitDeviationAssessment EvaluateVisitDeviation(
        string encounterId,
        object actualDate,
        IReadOnlyDictionary<string, object> anchorDates,
        IReadOnlyDictionary<string, object>? completedVisitDates = null,
        IEnumerable<EncounterScheduleItem>? encounters = null)
    {
        var schedules = ComputeSubjectSchedule(
            anchorDates: anchorDates,
            completedVisitDates: completedVisitDates,
            encounters: encounters
        );

        var schedMap = schedules.ToDictionary(s => s.EncounterId, s => s);
        if (!schedMap.TryGetValue(encounterId, out var sched))
        {
            throw new ArgumentException($"Encounter '{encounterId}' not found in calculated subject schedule");
        }

        DateOnly actualDt = ParseDate(actualDate);
        DateOnly targetDt = DateOnly.Parse(sched.TargetDate, CultureInfo.InvariantCulture);
        DateOnly winStart = DateOnly.Parse(sched.WindowStartDate, CultureInfo.InvariantCulture);
        DateOnly winEnd = DateOnly.Parse(sched.WindowEndDate, CultureInfo.InvariantCulture);

        int deltaDays = actualDt.DayNumber - targetDt.DayNumber;

        var allItems = encounters != null ? encounters.ToList() : GetAllEncounters();
        var matchingItem = allItems.FirstOrDefault(i => i.EncounterId == encounterId);
        int majorThreshold = matchingItem?.Window.MajorDeviationToleranceDays ?? 7;

        VisitDeviationClassification classification;
        bool isDeviation;
        DeviationSeverity severity;
        string rationale;

        if (actualDt >= winStart && actualDt <= winEnd)
        {
            classification = VisitDeviationClassification.WITHIN_WINDOW;
            isDeviation = false;
            severity = DeviationSeverity.NONE;
            rationale = $"Visit occurred on target {sched.TargetDate} (within permissible window [{sched.WindowStartDate} to {sched.WindowEndDate}]).";
        }
        else if (actualDt < winStart)
        {
            int daysEarly = winStart.DayNumber - actualDt.DayNumber;
            if (daysEarly > majorThreshold)
            {
                classification = VisitDeviationClassification.OUT_OF_WINDOW_MAJOR;
                isDeviation = true;
                severity = DeviationSeverity.MAJOR;
                rationale = $"Visit occurred {daysEarly} days before window opening ({sched.WindowStartDate}), exceeding major deviation threshold of {majorThreshold} days.";
            }
            else
            {
                classification = VisitDeviationClassification.EARLY_VISIT;
                isDeviation = true;
                severity = DeviationSeverity.MINOR;
                rationale = $"Visit occurred {daysEarly} days before window opening ({sched.WindowStartDate}).";
            }
        }
        else
        {
            int daysLate = actualDt.DayNumber - winEnd.DayNumber;
            if (daysLate > majorThreshold)
            {
                classification = VisitDeviationClassification.OUT_OF_WINDOW_MAJOR;
                isDeviation = true;
                severity = DeviationSeverity.MAJOR;
                rationale = $"Visit occurred {daysLate} days after window closing ({sched.WindowEndDate}), exceeding major deviation threshold of {majorThreshold} days.";
            }
            else
            {
                classification = VisitDeviationClassification.LATE_VISIT;
                isDeviation = true;
                severity = DeviationSeverity.MINOR;
                rationale = $"Visit occurred {daysLate} days after window closing ({sched.WindowEndDate}).";
            }
        }

        return new VisitDeviationAssessment(
            EncounterId: encounterId,
            ActualDate: actualDt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TargetDate: sched.TargetDate,
            WindowStartDate: sched.WindowStartDate,
            WindowEndDate: sched.WindowEndDate,
            DeltaDaysFromTarget: deltaDays,
            Classification: classification,
            IsDeviation: isDeviation,
            DeviationSeverity: severity,
            Rationale: rationale
        );
    }
}

/// <summary>
/// Alias class matching VectorTimelineEngine.
/// </summary>
public class VectorTimelineEngine : TimelineScheduleEngine
{
    public VectorTimelineEngine(TimelineScheduleDefinition? definition = null) : base(definition) { }
}
