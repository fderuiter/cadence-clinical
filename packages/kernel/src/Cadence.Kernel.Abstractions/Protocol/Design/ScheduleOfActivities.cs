using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Protocol.Design;

/// <summary>
/// The Schedule of Activities of one protocol design (ADR-2475): planned activities as columns, grouped by epoch, and
/// collection specifications as rows, with a cell wherever an activity collects a specification. It is a rendering of
/// the design, never stored as truth: the same design always compiles to the same matrix, byte for byte.
/// </summary>
/// <param name="Title">The protocol's title.</param>
/// <param name="Epochs">The epochs that have columns, in epoch order, each with its span of columns.</param>
/// <param name="Columns">The planned activities, epoch by epoch; timed ones by study day, then the event-driven ones.</param>
/// <param name="Rows">The collection specifications, in design order.</param>
/// <param name="Footnotes">The footnotes the columns and rows refer to, numbered from 1 in order of first use.</param>
public sealed record SoaMatrix(
    string Title,
    IReadOnlyList<SoaEpoch> Epochs,
    IReadOnlyList<SoaColumn> Columns,
    IReadOnlyList<SoaRow> Rows,
    IReadOnlyList<SoaFootnote> Footnotes);

/// <summary>An epoch's header over its columns.</summary>
/// <param name="EpochId">The epoch's element id.</param>
/// <param name="Name">The epoch's name.</param>
/// <param name="Order">The epoch's order in the design.</param>
/// <param name="FirstColumn">The index of its first column.</param>
/// <param name="ColumnCount">How many columns it spans.</param>
public sealed record SoaEpoch(string EpochId, string Name, int Order, int FirstColumn, int ColumnCount);

/// <summary>A column: one planned activity (a visit or other planned contact).</summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Name">The activity's name.</param>
/// <param name="EpochId">The epoch it belongs to.</param>
/// <param name="StudyDay">The planned study day; null for an event-driven activity, which has no fixed day.</param>
/// <param name="WindowBefore">Days allowed before the planned day.</param>
/// <param name="WindowAfter">Days allowed after the planned day.</param>
/// <param name="Locations">Where it may take place.</param>
/// <param name="Footnotes">The numbers of its footnotes.</param>
public sealed record SoaColumn(
    string ActivityId,
    string Name,
    string EpochId,
    int? StudyDay,
    int WindowBefore,
    int WindowAfter,
    IReadOnlyList<ActivityLocation> Locations,
    IReadOnlyList<int> Footnotes);

/// <summary>A row: one collection specification and the columns that collect it.</summary>
/// <param name="CollectionSpecId">The collection specification's element id.</param>
/// <param name="Name">Its name.</param>
/// <param name="ItemIds">Its items' element ids, in design order.</param>
/// <param name="Concepts">The concept versions its items record, in item order, each once.</param>
/// <param name="Columns">The indexes of the columns that collect it, ascending.</param>
/// <param name="Footnotes">The numbers of its footnotes.</param>
public sealed record SoaRow(
    string CollectionSpecId,
    string Name,
    IReadOnlyList<string> ItemIds,
    IReadOnlyList<ConceptRef> Concepts,
    IReadOnlyList<int> Columns,
    IReadOnlyList<int> Footnotes)
{
    /// <summary>How many planned activities collect it.</summary>
    public int Frequency => Columns.Count;
}

/// <summary>What a footnote says about a column or row.</summary>
public enum SoaFootnoteKind
{
    /// <summary>The visit window around a planned day.</summary>
    Window,

    /// <summary>Where an activity may take place, when that is not only the site.</summary>
    Location,

    /// <summary>An activity that happens when an event triggers it, not on a fixed day.</summary>
    EventDriven,

    /// <summary>The operational need a specification is collected for.</summary>
    Purpose,

    /// <summary>A specification no planned activity collects.</summary>
    NotScheduled,
}

/// <summary>A footnote, generated from the design.</summary>
/// <param name="Number">Its number, from 1.</param>
/// <param name="Kind">What it says.</param>
/// <param name="Text">Its text.</param>
public sealed record SoaFootnote(int Number, SoaFootnoteKind Kind, string Text);

/// <summary>
/// Compiles a protocol design into its Schedule of Activities (ADR-2475). Pure: it reads nothing but the design, so the
/// same design, and therefore the same content hash, gives a byte-identical <see cref="Write"/>.
/// </summary>
public static class ScheduleOfActivities
{
    /// <summary>The matrix of <paramref name="design"/>.</summary>
    public static SoaMatrix Compile(ProtocolDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        var epochs = design.Epochs.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var ordered = design.PlannedActivities
            .OrderBy(a => epochs.TryGetValue(a.EpochId, out var epoch) ? epoch.Order : int.MaxValue)
            .ThenBy(a => a.EpochId, StringComparer.Ordinal)
            .ThenBy(a => a.Timing is null)
            .ThenBy(a => a.Timing is { } timing ? Offset(timing.Day) : 0)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();

        var footnotes = new Footnotes();
        var columns = ordered.Select(activity => new SoaColumn(
            activity.Id,
            activity.Name,
            activity.EpochId,
            activity.Timing?.Day,
            activity.Timing?.WindowBefore ?? 0,
            activity.Timing?.WindowAfter ?? 0,
            [.. activity.Locations.Distinct().Order()],
            footnotes.OfColumn(activity))).ToList();

        var rows = design.CollectionSpecs.Select(spec =>
        {
            int[] collectedAt = [.. ordered.Select((activity, index) => (activity, index))
                .Where(x => x.activity.CollectionSpecIds.Contains(spec.Id, StringComparer.Ordinal))
                .Select(x => x.index)];
            return new SoaRow(
                spec.Id,
                spec.Name,
                [.. spec.Items.Select(i => i.Id)],
                [.. spec.Items.Select(i => i.Concept).Distinct()],
                collectedAt,
                footnotes.OfRow(spec, collectedAt.Length));
        }).ToList();

        var groups = columns
            .Select((column, index) => (column, index))
            .GroupBy(x => x.column.EpochId, StringComparer.Ordinal)
            .Select(g => new SoaEpoch(
                g.Key,
                epochs.TryGetValue(g.Key, out var epoch) ? epoch.Name : g.Key,
                epoch?.Order ?? int.MaxValue,
                g.First().index,
                g.Count()))
            .ToList();

        return new SoaMatrix(design.Title, groups, columns, rows, footnotes.All);
    }

    /// <summary>
    /// The matrix as JSON: camel case, enums by name, two-space indents and <c>\n</c> line ends on every platform, so
    /// the text is the same wherever it is written.
    /// </summary>
    public static string Write(SoaMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        return JsonSerializer.Serialize(matrix, ScheduleOfActivitiesJsonContext.Default.SoaMatrix);
    }

    /// <summary>The content hash of the matrix (<see cref="ContentHash"/>), which is the same for the same design.</summary>
    public static string Hash(SoaMatrix matrix) => ContentHash.Compute(Write(matrix));

    /// <summary>Study days skip day 0, so day 0 sorts as day 1 and day -1 just before it (ADR-2487).</summary>
    private static int Offset(int studyDay) => studyDay > 0 ? studyDay - 1 : studyDay;

    /// <summary>Footnotes numbered in order of first use; the same text is one footnote.</summary>
    private sealed class Footnotes
    {
        private readonly List<SoaFootnote> _all = [];

        public IReadOnlyList<SoaFootnote> All => _all;

        public IReadOnlyList<int> OfColumn(PlannedActivity activity)
        {
            var numbers = new List<int>();
            if (activity.Timing is not { } timing)
            {
                numbers.Add(Number(SoaFootnoteKind.EventDriven, "Not on a fixed day: done when the event that triggers it occurs."));
            }
            else if (timing.WindowBefore > 0 || timing.WindowAfter > 0)
            {
                numbers.Add(Number(SoaFootnoteKind.Window, timing.WindowBefore == timing.WindowAfter
                    ? $"Window: {Days(timing.WindowBefore)} either side."
                    : $"Window: {Days(timing.WindowBefore)} before, {Days(timing.WindowAfter)} after."));
            }

            var locations = activity.Locations.Distinct().Order().ToList();
            if (locations.Count > 0 && !(locations is [ActivityLocation.Site]))
            {
                numbers.Add(Number(SoaFootnoteKind.Location, $"May take place: {string.Join(", ", locations.Select(Describe))}."));
            }

            return numbers;
        }

        public IReadOnlyList<int> OfRow(CollectionSpec spec, int frequency)
        {
            var numbers = new List<int>();
            if (spec.Purpose?.OperationalNeed is { Length: > 0 } need)
            {
                numbers.Add(Number(SoaFootnoteKind.Purpose, $"Collected for: {need}"));
            }

            if (frequency == 0)
            {
                numbers.Add(Number(SoaFootnoteKind.NotScheduled, "No planned activity collects it."));
            }

            return numbers;
        }

        private int Number(SoaFootnoteKind kind, string text)
        {
            var existing = _all.Find(f => f.Kind == kind && string.Equals(f.Text, text, StringComparison.Ordinal));
            if (existing is not null)
            {
                return existing.Number;
            }

            var footnote = new SoaFootnote(_all.Count + 1, kind, text);
            _all.Add(footnote);
            return footnote.Number;
        }

        private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

        private static string Describe(ActivityLocation location) => location switch
        {
            ActivityLocation.Site => "at the site",
            ActivityLocation.Home => "at home",
            ActivityLocation.Telehealth => "by telehealth",
            ActivityLocation.LocalFacility => "at a local facility",
            ActivityLocation.Remote => "remotely",
            _ => location.ToString(),
        };
    }
}

/// <summary>Source-generated JSON for the Schedule of Activities and its diff: camel case, enums by name, indented.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    IndentSize = 2,
    NewLine = "\n",
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SoaMatrix))]
[JsonSerializable(typeof(SoaAmendmentDiff))]
internal sealed partial class ScheduleOfActivitiesJsonContext : JsonSerializerContext;
