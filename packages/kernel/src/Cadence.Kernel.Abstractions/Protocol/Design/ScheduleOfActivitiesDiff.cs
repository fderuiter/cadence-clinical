using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Protocol.Design;

/// <summary>What a Schedule of Activities directive says changed.</summary>
public enum SoaDirectiveKind
{
    /// <summary>A planned activity (column) is new.</summary>
    VisitAdded,

    /// <summary>A planned activity is gone.</summary>
    VisitRemoved,

    /// <summary>A planned activity moved to another study day or epoch, or gained or lost its fixed day.</summary>
    VisitMoved,

    /// <summary>A planned activity's window changed, and nothing else about when it happens.</summary>
    VisitWindowChanged,

    /// <summary>Where a planned activity may take place changed.</summary>
    VisitLocationsChanged,

    /// <summary>A collection specification (row) is new.</summary>
    ActivityAdded,

    /// <summary>A collection specification is gone.</summary>
    ActivityRemoved,

    /// <summary>The planned activities that collect a specification changed, and so how often it is collected.</summary>
    ActivityFrequencyChanged,

    /// <summary>A specification's items, or the concept versions they record, changed.</summary>
    ActivityConceptsChanged,

    /// <summary>An eligibility criterion is new.</summary>
    CriterionAdded,

    /// <summary>An eligibility criterion is gone.</summary>
    CriterionRemoved,

    /// <summary>An eligibility criterion's text, category, concepts or expression changed.</summary>
    CriterionChanged,
}

/// <summary>When and where a planned activity happens, as a directive compares it.</summary>
/// <param name="EpochId">Its epoch.</param>
/// <param name="StudyDay">Its planned study day; null when event-driven.</param>
/// <param name="WindowBefore">Days allowed before the planned day.</param>
/// <param name="WindowAfter">Days allowed after the planned day.</param>
/// <param name="Locations">Where it may take place.</param>
public sealed record SoaVisitState(string EpochId, int? StudyDay, int WindowBefore, int WindowAfter, IReadOnlyList<ActivityLocation> Locations)
{
    /// <summary>The state of <paramref name="activity"/>.</summary>
    public static SoaVisitState Of(PlannedActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return new SoaVisitState(
            activity.EpochId,
            activity.Timing is { } timing ? ExpectedSchedule.NormalizeStudyDay(timing.Day) : null,
            activity.Timing?.WindowBefore ?? 0,
            activity.Timing?.WindowAfter ?? 0,
            [.. activity.Locations.Distinct().Order()]);
    }
}

/// <summary>
/// One typed change between two Schedules of Activities, with the protocol elements and concept versions it touches,
/// in the terms an amendment declares (<see cref="ProtocolAmendment.ImpactedElements"/>).
/// </summary>
/// <param name="Kind">What changed.</param>
/// <param name="ElementId">The element that changed: a planned activity, a collection specification or a criterion.</param>
/// <param name="ImpactedElements">Every element the change touches, this one included, sorted.</param>
/// <param name="ImpactedConcepts">The concept versions the change touches, sorted.</param>
/// <param name="Before">For a visit directive, the activity before the change.</param>
/// <param name="After">For a visit directive, the activity after the change.</param>
/// <param name="FrequencyBefore">For <see cref="SoaDirectiveKind.ActivityFrequencyChanged"/>, how many activities collected it.</param>
/// <param name="FrequencyAfter">For <see cref="SoaDirectiveKind.ActivityFrequencyChanged"/>, how many collect it now.</param>
/// <param name="CollectedAtAdded">For <see cref="SoaDirectiveKind.ActivityFrequencyChanged"/>, the activities that now collect it.</param>
/// <param name="CollectedAtRemoved">For <see cref="SoaDirectiveKind.ActivityFrequencyChanged"/>, the activities that no longer do.</param>
public sealed record SoaDirective(
    SoaDirectiveKind Kind,
    string ElementId,
    IReadOnlyList<string> ImpactedElements,
    IReadOnlyList<ConceptRef> ImpactedConcepts,
    SoaVisitState? Before = null,
    SoaVisitState? After = null,
    int? FrequencyBefore = null,
    int? FrequencyAfter = null,
    IReadOnlyList<string>? CollectedAtAdded = null,
    IReadOnlyList<string>? CollectedAtRemoved = null);

/// <summary>
/// The Schedule of Activities and eligibility changes between two designs (ADR-2475): the input an amendment's impacted
/// elements and its transition policy are chosen from.
/// </summary>
/// <param name="Directives">The changes, visits first, then activities, then criteria, each group by element id.</param>
/// <param name="ImpactedElements">Every element the directives touch, sorted.</param>
/// <param name="ImpactedConcepts">Every concept version the directives touch, sorted.</param>
/// <param name="ChangesSchedule">
/// True when what a participant already on study does, or when, changes: a visit or activity directive. Such a change
/// reaches current participants unless the transition policy keeps them on the previous version.
/// </param>
/// <param name="ChangesEligibility">True when who may enter changes: a criterion directive. It concerns only new participants.</param>
public sealed record SoaAmendmentDiff(
    IReadOnlyList<SoaDirective> Directives,
    IReadOnlyList<string> ImpactedElements,
    IReadOnlyList<ConceptRef> ImpactedConcepts,
    bool ChangesSchedule,
    bool ChangesEligibility)
{
    /// <summary>
    /// The impacted elements <paramref name="declared"/> leaves out, sorted: what an amendment changes in its schedule
    /// or eligibility without saying so.
    /// </summary>
    public IReadOnlyList<string> Undeclared(IEnumerable<string> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        var named = declared.ToHashSet(StringComparer.Ordinal);
        return [.. ImpactedElements.Where(e => !named.Contains(e))];
    }
}

/// <summary>
/// Compares the Schedules of Activities and eligibility criteria of two designs, element by element through their
/// stable ids (K-PROT-1). Pure and deterministic. A changed name alone is not a directive; the structural diff reports it.
/// </summary>
public static class ScheduleOfActivitiesDiff
{
    /// <summary>The changes from <paramref name="before"/> (null for none) to <paramref name="after"/>.</summary>
    public static SoaAmendmentDiff Compare(ProtocolDesign? before, ProtocolDesign after)
    {
        ArgumentNullException.ThrowIfNull(after);
        before ??= new ProtocolDesign();
        var directives = new List<SoaDirective>();
        Visits(before, after, directives);
        Activities(before, after, directives);
        Criteria(before, after, directives);

        var ordered = directives
            .OrderBy(d => Group(d.Kind))
            .ThenBy(d => d.ElementId, StringComparer.Ordinal)
            .ThenBy(d => d.Kind)
            .ToList();
        return new SoaAmendmentDiff(
            ordered,
            Sorted(ordered.SelectMany(d => d.ImpactedElements)),
            Sorted(ordered.SelectMany(d => d.ImpactedConcepts)),
            ordered.Any(d => Group(d.Kind) < 2),
            ordered.Any(d => Group(d.Kind) == 2));
    }

    /// <summary>The diff as JSON, written the same way as <see cref="ScheduleOfActivities.Write"/>.</summary>
    public static string Write(SoaAmendmentDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return JsonSerializer.Serialize(diff, ScheduleOfActivitiesJsonContext.Default.SoaAmendmentDiff);
    }

    private static void Visits(ProtocolDesign before, ProtocolDesign after, List<SoaDirective> directives)
    {
        var old = before.PlannedActivities.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var @new = after.PlannedActivities.ToDictionary(a => a.Id, StringComparer.Ordinal);
        foreach (var id in old.Keys.Union(@new.Keys, StringComparer.Ordinal))
        {
            var was = old.GetValueOrDefault(id);
            var @is = @new.GetValueOrDefault(id);
            var wasState = was is null ? null : SoaVisitState.Of(was);
            var isState = @is is null ? null : SoaVisitState.Of(@is);
            var specs = (was?.CollectionSpecIds ?? []).Concat(@is?.CollectionSpecIds ?? []).ToList();
            SoaDirective Visit(SoaDirectiveKind kind) =>
                new(kind, id, Sorted([id, .. specs]), Sorted(Concepts(before, after, specs)), wasState, isState);

            if (wasState is null)
            {
                directives.Add(Visit(SoaDirectiveKind.VisitAdded));
                continue;
            }

            if (isState is null)
            {
                directives.Add(Visit(SoaDirectiveKind.VisitRemoved));
                continue;
            }

            if (!string.Equals(wasState.EpochId, isState.EpochId, StringComparison.Ordinal) || wasState.StudyDay != isState.StudyDay)
            {
                directives.Add(Visit(SoaDirectiveKind.VisitMoved));
            }
            else if (wasState.WindowBefore != isState.WindowBefore || wasState.WindowAfter != isState.WindowAfter)
            {
                directives.Add(Visit(SoaDirectiveKind.VisitWindowChanged));
            }

            if (!wasState.Locations.SequenceEqual(isState.Locations))
            {
                directives.Add(Visit(SoaDirectiveKind.VisitLocationsChanged));
            }
        }
    }

    private static void Activities(ProtocolDesign before, ProtocolDesign after, List<SoaDirective> directives)
    {
        var old = before.CollectionSpecs.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var @new = after.CollectionSpecs.ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var id in old.Keys.Union(@new.Keys, StringComparer.Ordinal))
        {
            var was = old.GetValueOrDefault(id);
            var @is = @new.GetValueOrDefault(id);
            if (was is null || @is is null)
            {
                var spec = (was ?? @is)!;
                directives.Add(new SoaDirective(
                    was is null ? SoaDirectiveKind.ActivityAdded : SoaDirectiveKind.ActivityRemoved,
                    id,
                    Sorted([id, .. spec.Items.Select(i => i.Id)]),
                    Sorted(spec.Items.Select(i => i.Concept))));
                continue;
            }

            var collectedBefore = CollectedAt(before, id);
            var collectedAfter = CollectedAt(after, id);
            if (!collectedBefore.SetEquals(collectedAfter))
            {
                var added = Sorted(collectedAfter.Except(collectedBefore));
                var removed = Sorted(collectedBefore.Except(collectedAfter));
                directives.Add(new SoaDirective(
                    SoaDirectiveKind.ActivityFrequencyChanged,
                    id,
                    Sorted([id, .. added, .. removed]),
                    Sorted(@is.Items.Select(i => i.Concept)),
                    FrequencyBefore: collectedBefore.Count,
                    FrequencyAfter: collectedAfter.Count,
                    CollectedAtAdded: added,
                    CollectedAtRemoved: removed));
            }

            var itemsBefore = was.Items.Select(i => (i.Id, i.Concept)).ToHashSet();
            var itemsAfter = @is.Items.Select(i => (i.Id, i.Concept)).ToHashSet();
            if (!itemsBefore.SetEquals(itemsAfter))
            {
                var changed = itemsBefore.Except(itemsAfter).Concat(itemsAfter.Except(itemsBefore)).ToList();
                directives.Add(new SoaDirective(
                    SoaDirectiveKind.ActivityConceptsChanged,
                    id,
                    Sorted([id, .. changed.Select(i => i.Id)]),
                    Sorted(changed.Select(i => i.Concept))));
            }
        }
    }

    private static void Criteria(ProtocolDesign before, ProtocolDesign after, List<SoaDirective> directives)
    {
        var old = before.EligibilityCriteria.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var @new = after.EligibilityCriteria.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var id in old.Keys.Union(@new.Keys, StringComparer.Ordinal))
        {
            var was = old.GetValueOrDefault(id);
            var @is = @new.GetValueOrDefault(id);
            SoaDirectiveKind kind;
            if (was is null)
            {
                kind = SoaDirectiveKind.CriterionAdded;
            }
            else if (@is is null)
            {
                kind = SoaDirectiveKind.CriterionRemoved;
            }
            else if (!string.Equals(was.Text, @is.Text, StringComparison.Ordinal)
                || was.Category != @is.Category
                || !string.Equals(was.Expression, @is.Expression, StringComparison.Ordinal)
                || !was.Concepts.SequenceEqual(@is.Concepts))
            {
                kind = SoaDirectiveKind.CriterionChanged;
            }
            else
            {
                continue;
            }

            // A criterion defines the populations that name it, and through them the estimands (ADR-2474).
            var populations = before.Populations.Concat(after.Populations)
                .Where(p => p.CriterionIds.Contains(id, StringComparer.Ordinal))
                .Select(p => p.Id);
            directives.Add(new SoaDirective(
                kind,
                id,
                Sorted([id, .. populations]),
                Sorted((was?.Concepts ?? []).Concat(@is?.Concepts ?? []))));
        }
    }

    private static HashSet<string> CollectedAt(ProtocolDesign design, string specId) =>
        design.PlannedActivities
            .Where(a => a.CollectionSpecIds.Contains(specId, StringComparer.Ordinal))
            .Select(a => a.Id)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<ConceptRef> Concepts(ProtocolDesign before, ProtocolDesign after, IReadOnlyCollection<string> specIds) =>
        before.CollectionSpecs.Concat(after.CollectionSpecs)
            .Where(s => specIds.Contains(s.Id, StringComparer.Ordinal))
            .SelectMany(s => s.Items)
            .Select(i => i.Concept);

    /// <summary>0 for visits, 1 for activities, 2 for criteria.</summary>
    private static int Group(SoaDirectiveKind kind) => kind switch
    {
        <= SoaDirectiveKind.VisitLocationsChanged => 0,
        <= SoaDirectiveKind.ActivityConceptsChanged => 1,
        _ => 2,
    };

    private static string[] Sorted(IEnumerable<string> ids) => [.. ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static ConceptRef[] Sorted(IEnumerable<ConceptRef> concepts) =>
        [.. concepts.Distinct().OrderBy(c => c.ConceptId.Value).ThenBy(c => c.Version)];
}
