using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Kernel.Protocol.Design;

namespace Cadence.Ddf;

/// <summary>A USDM <c>Code</c> (or the standard code of an <c>AliasCode</c>): its code and decode.</summary>
internal sealed record UsdmCode(string Code, string Decode);

/// <summary>Tolerant readers for USDM JSON: a missing or mistyped property reads as absent.</summary>
internal static class Usdm
{
    public static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A string property that says something: blank text reads as absent.</summary>
    public static string? Text(JsonElement element, string name) => Str(element, name) is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;

    public static JsonElement? Obj(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    public static IReadOnlyList<JsonElement> Arr(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray()]
            : [];

    public static List<string> Strs(JsonElement element, string name) =>
        [.. Arr(element, name).Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)];

    public static string Id(JsonElement element) => Str(element, "id") ?? string.Empty;

    /// <summary>The code of a <c>Code</c>, or the standard code of an <c>AliasCode</c>.</summary>
    public static UsdmCode? Code(JsonElement element)
    {
        if (Obj(element, "standardCode") is { } standard)
        {
            return Code(standard);
        }

        return Str(element, "code") is { } code ? new UsdmCode(code, Str(element, "decode") ?? code) : null;
    }

    /// <summary>The decode of the code held in <paramref name="name"/>.</summary>
    public static string? Decode(JsonElement element, string name) => Obj(element, name) is { } code ? Code(code)?.Decode : null;
}

/// <summary>An ISO 8601 duration in whole days, and whether it has a part shorter than a day.</summary>
internal readonly partial record struct UsdmDuration(int Days, bool HasTimePart)
{
    /// <summary>Parses <c>PnW</c>, <c>PnD</c> and <c>PTnHnMnS</c> forms; years and months have no fixed length and read as null.</summary>
    public static UsdmDuration? Parse(string? value)
    {
        var match = value is null ? Match.Empty : Pattern().Match(value.Trim());
        if (!match.Success || match.Groups["y"].Success || match.Groups["mo"].Success)
        {
            return null;
        }

        var days = (Number(match, "w") * 7) + Number(match, "d");
        var hours = Number(match, "h") + (Number(match, "mi") / 60m) + (Number(match, "s") / 3600m);
        return new UsdmDuration((int)(days + decimal.Floor(hours / 24m)), hours % 24m != 0);
    }

    private static decimal Number(Match match, string group) =>
        match.Groups[group].Success ? decimal.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0m;

    [GeneratedRegex(
        @"^P(?:(?<y>\d+)Y)?(?:(?<mo>\d+)M)?(?:(?<w>\d+)W)?(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<mi>\d+)M)?(?:(?<s>\d+(?:\.\d+)?)S)?)?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex Pattern();
}

/// <summary>
/// The study day of each scheduled instance on a USDM timeline, from its timings (ADR-2473 §4.3). A <c>Fixed
/// Reference</c> timing sets its instance's day; a <c>Before</c> or <c>After</c> timing places its <c>relativeFrom</c>
/// instance that long before or after its <c>relativeTo</c> instance. Study days skip day 0, and parts shorter than a
/// day are noted and dropped.
/// </summary>
internal sealed class UsdmSchedule
{
    private readonly Dictionary<string, int> _days = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _windows = new(StringComparer.Ordinal);
    private readonly List<UsdmImportNote> _notes;

    public UsdmSchedule(JsonElement timeline, List<UsdmImportNote> notes)
    {
        _notes = notes;
        var relative = new List<(string From, string To, int Offset)>();
        foreach (var timing in Usdm.Arr(timeline, "timings"))
        {
            Read(timing, relative);
        }

        Resolve(relative);
    }

    public ActivityTiming? TimingOf(string instanceId)
    {
        if (!_days.TryGetValue(instanceId, out var day))
        {
            _notes.Add(new UsdmImportNote(instanceId, "no study day follows from the timeline's timings; the activity has no timing."));
            return null;
        }

        if (!_windows.TryGetValue(instanceId, out var timing))
        {
            return new ActivityTiming(day);
        }

        return new ActivityTiming(day, Window(timing, "windowLower"), Window(timing, "windowUpper"));
    }

    private void Read(JsonElement timing, List<(string From, string To, int Offset)> relative)
    {
        var id = Usdm.Id(timing);
        var type = (Usdm.Decode(timing, "type") ?? string.Empty).ToUpperInvariant();
        var from = Usdm.Str(timing, "relativeFromScheduledInstanceId");
        var to = Usdm.Str(timing, "relativeToScheduledInstanceId");
        var value = Usdm.Str(timing, "value");
        if (from is null || Duration(id, value) is not { } duration)
        {
            _notes.Add(new UsdmImportNote(id, $"value '{value}' or its instance cannot be read; the timing is not carried."));
            return;
        }

        if (type.Contains("FIXED", StringComparison.Ordinal))
        {
            _days[from] = duration.Days;
            _windows[from] = timing;
        }
        else if ((type == "BEFORE" || type == "AFTER") && to is not null)
        {
            relative.Add((from, to, type == "AFTER" ? duration.Days : -duration.Days));
            _windows[from] = timing;
        }
        else
        {
            _notes.Add(new UsdmImportNote(id, $"timing type '{type}' is not carried."));
        }
    }

    private void Resolve(List<(string From, string To, int Offset)> relative)
    {
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var (from, to, offset) in relative)
            {
                if (!_days.ContainsKey(from) && _days.TryGetValue(to, out var anchor))
                {
                    _days[from] = Shift(anchor, offset);
                    progress = true;
                }
                else if (_days.TryGetValue(from, out var known) && !_days.ContainsKey(to))
                {
                    _days[to] = Shift(known, -offset);
                    progress = true;
                }
            }
        }
    }

    /// <summary>Moves a study day by <paramref name="offset"/> days; there is no day 0.</summary>
    internal static int Shift(int day, int offset)
    {
        var result = day + offset;
        if (day > 0 && result <= 0)
        {
            return result - 1;
        }

        return day < 0 && result >= 0 ? result + 1 : result;
    }

    private int Window(JsonElement timing, string property)
    {
        var value = Usdm.Str(timing, property);
        if (value is null)
        {
            return 0;
        }

        if (Duration(Usdm.Id(timing), value) is { } duration)
        {
            return duration.Days;
        }

        _notes.Add(new UsdmImportNote(Usdm.Id(timing), $"{property} '{value}' cannot be read; the window is not carried."));
        return 0;
    }

    private UsdmDuration? Duration(string source, string? value)
    {
        var duration = UsdmDuration.Parse(value);
        if (duration is { HasTimePart: true })
        {
            _notes.Add(new UsdmImportNote(source, $"the part of {value} shorter than a day is not carried."));
        }

        return duration;
    }
}
