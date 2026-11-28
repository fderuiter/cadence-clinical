using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;

using static Cadence.Ddf.OdmXmlNavigationAndVendorNotes;

namespace Cadence.Ddf;

internal static partial class OdmScheduleCompiler
{
    public static List<(XElement? EventRef, XElement EventDef)> OrderStudyEvents(
        XElement mdv,
        XElement? protocolEl,
        IReadOnlyDictionary<string, XElement> studyEventDefs,
        List<ProtocolImportNote> notes,
        List<string> problems)
    {
        var ordered = new List<(XElement? EventRef, XElement EventDef)>();
        var seenEventOids = new HashSet<string>(StringComparer.Ordinal);

        if (protocolEl is not null)
        {
            var ownerOid = Attr(mdv, "OID") ?? "Protocol";
            var orderedRefs = OrderRefs(Children(protocolEl, "StudyEventRef"), ownerOid, "StudyEventRef", problems);
            foreach (var eventRef in orderedRefs)
            {
                var eventOid = Attr(eventRef, "StudyEventOID");
                if (string.IsNullOrWhiteSpace(eventOid))
                {
                    problems.Add($"Protocol in '{ownerOid}' has a <StudyEventRef> without a StudyEventOID attribute.");
                    continue;
                }

                ValidateYesNoAttribute(eventRef, "Mandatory", $"Protocol StudyEventRef '{eventOid}'", problems);
                if (!studyEventDefs.TryGetValue(eventOid, out var eventDef))
                {
                    problems.Add($"Protocol references StudyEventOID '{eventOid}', which does not exist in <MetaDataVersion>.");
                    continue;
                }

                if (seenEventOids.Add(eventOid))
                {
                    ordered.Add((eventRef, eventDef));
                }
            }
        }

        foreach (var eventDef in Children(mdv, "StudyEventDef"))
        {
            var eventOid = Attr(eventDef, "OID");
            if (string.IsNullOrWhiteSpace(eventOid) || !seenEventOids.Add(eventOid))
            {
                continue;
            }

            var unreferencedType = Attr(eventDef, "Type");
            var isCommonOrUnscheduled = string.Equals(unreferencedType, "Common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unreferencedType, "Unscheduled", StringComparison.OrdinalIgnoreCase);
            if (!isCommonOrUnscheduled && protocolEl is not null && Children(protocolEl, "StudyEventRef").Any())
            {
                notes.Add(new ProtocolImportNote(
                    eventOid,
                    $"StudyEventDef '{Attr(eventDef, "Name") ?? eventOid}' was not referenced in <Protocol>; appended after protocol-ordered events."));
            }

            ordered.Add((null, eventDef));
        }

        return ordered;
    }

    public static (List<Epoch> Epochs, List<PlannedActivity> PlannedActivities) CompileStudySchedule(
        IReadOnlyList<(XElement? EventRef, XElement EventDef)> orderedEvents,
        IReadOnlyDictionary<string, string> formIdByOid,
        HashSet<string> usedIds,
        List<ProtocolImportNote> notes,
        List<string> problems)
    {
        if (orderedEvents.Count == 0)
        {
            return ([], []);
        }

        var epochs = new List<Epoch>();
        var epochIdByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var activities = new List<PlannedActivity>(orderedEvents.Count);
        int? previousScheduledDay = null;

        foreach (var (eventRef, eventDef) in orderedEvents)
        {
            var rawEventOid = Attr(eventDef, "OID")!;
            var eventName = FirstNonBlank(
                Attr(eventDef, "Name"),
                TranslatedText(Child(eventDef, "Description")),
                rawEventOid)!;
            var eventDescription = TranslatedText(Child(eventDef, "Description"));
            var eventRepeating = string.Equals(Attr(eventDef, "Repeating"), "Yes", StringComparison.OrdinalIgnoreCase);
            if (eventRepeating)
            {
                notes.Add(new ProtocolImportNote(
                    rawEventOid,
                    $"StudyEvent '{eventName}' declares Repeating='Yes'; multiple visit instances may occur per participant."));
            }

            var eventType = Attr(eventDef, "Type");
            if (!string.IsNullOrWhiteSpace(eventType)
                && !string.Equals(eventType, "Scheduled", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(eventType, "Unscheduled", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(eventType, "Common", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(new ProtocolImportNote(
                    rawEventOid,
                    $"StudyEvent '{eventName}' declares non-standard Type='{eventType}'; treated as scheduled activity."));
            }

            var orderedFormRefs = OrderRefs(Children(eventDef, "FormRef"), rawEventOid, "FormRef", problems);
            var specIds = new List<string>(orderedFormRefs.Count);
            var seenSpecIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var formRef in orderedFormRefs)
            {
                var formOid = Attr(formRef, "FormOID");
                if (string.IsNullOrWhiteSpace(formOid))
                {
                    problems.Add($"StudyEventDef '{rawEventOid}' has a <FormRef> without a FormOID attribute.");
                    continue;
                }

                ValidateYesNoAttribute(formRef, "Mandatory", $"StudyEventDef '{rawEventOid}' FormRef '{formOid}'", problems);
                if (!formIdByOid.TryGetValue(formOid, out var mappedSpecId))
                {
                    problems.Add($"StudyEventDef '{rawEventOid}' references FormOID '{formOid}', which does not exist in <MetaDataVersion>.");
                    continue;
                }

                if (seenSpecIds.Add(mappedSpecId))
                {
                    specIds.Add(mappedSpecId);
                }
            }

            if (string.Equals(eventType, "Common", StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventType, "Unscheduled", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add(new ProtocolImportNote(
                    rawEventOid,
                    $"StudyEvent '{eventName}' declares Type='{eventType}'; its referenced forms remain available as unscheduled/log forms outside the scheduled visit matrix."));
                continue;
            }

            var (epochKey, rawEpochId, epochName) = ResolveOdmEpoch(eventDef);
            if (!epochIdByKey.TryGetValue(epochKey, out var epochId))
            {
                epochId = OdmDesignImporter.AllocateUniqueElementId(rawEpochId, "Epoch", usedIds);
                epochIdByKey[epochKey] = epochId;
                epochs.Add(new Epoch(epochId, epochName, epochs.Count + 1));
            }

            var timing = ResolveActivityTiming(
                eventRef,
                eventDef,
                rawEventOid,
                eventName,
                eventDescription,
                eventType,
                eventRepeating,
                ref previousScheduledDay);

            var activityId = OdmDesignImporter.AllocateUniqueElementId(rawEventOid, "Activity", usedIds);
            activities.Add(new PlannedActivity(
                activityId,
                eventName,
                epochId,
                [ActivityLocation.Site],
                specIds,
                Timing: timing,
                Repeating: eventRepeating));
        }

        return (epochs, activities);
    }

    private static (string Key, string RawId, string Name) ResolveOdmEpoch(XElement eventDef)
    {
        if (Attr(eventDef, "Category") is { Length: > 0 } category)
        {
            return ($"category:{category}", $"Epoch.{OdmDesignImporter.SanitizeElementId(category, "epoch")}", category);
        }

        foreach (var alias in Children(eventDef, "Alias"))
        {
            var ctx = NormalizeAliasContext(Attr(alias, "Context"));
            var aliasName = Attr(alias, "Name");
            if (string.Equals(ctx, "epoch", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(aliasName))
            {
                var trimmed = aliasName.Trim();
                return ($"alias:{trimmed}", $"Epoch.{OdmDesignImporter.SanitizeElementId(trimmed, "epoch")}", trimmed);
            }
        }

        var eventType = Attr(eventDef, "Type");
        if (string.Equals(eventType, "Unscheduled", StringComparison.OrdinalIgnoreCase))
        {
            return ("type:unscheduled", "Epoch.unscheduled", "Unscheduled");
        }

        if (string.Equals(eventType, "Common", StringComparison.OrdinalIgnoreCase))
        {
            return ("type:common", "Epoch.common", "Common");
        }

        return ("type:scheduled", "Epoch.scheduled", "Scheduled");
    }

    private static ActivityTiming? ResolveActivityTiming(
        XElement? eventRef,
        XElement eventDef,
        string rawEventOid,
        string eventName,
        string? eventDescription,
        string? eventType,
        bool eventRepeating,
        ref int? previousScheduledDay)
    {
        int? explicitDay = TryReadIntMetadata(eventDef, eventRef, "StudyDay", "TargetDay", "PlannedDay", "Day");
        int? explicitBefore = TryReadIntMetadata(eventDef, eventRef, "WindowBefore", "MinWindow", "WindowLower");
        int? explicitAfter = TryReadIntMetadata(eventDef, eventRef, "WindowAfter", "MaxWindow", "WindowUpper");

        var isEventDrivenType = string.Equals(eventType, "Unscheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "Common", StringComparison.OrdinalIgnoreCase);
        if (isEventDrivenType && explicitDay is null)
        {
            return null;
        }

        var combinedLabel = string.Join(" ", new[] { eventName, eventDescription, rawEventOid }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var (parsedBefore, parsedAfter) = ParseWindowFromText(combinedLabel, eventDef, eventRef);
        int windowBefore = Math.Abs(explicitBefore ?? parsedBefore ?? 0);
        int windowAfter = Math.Abs(explicitAfter ?? parsedAfter ?? 0);

        if (explicitDay is { } givenDay)
        {
            int normalizedGiven = givenDay == 0 ? 1 : givenDay;
            previousScheduledDay = normalizedGiven;
            return new ActivityTiming(normalizedGiven, windowBefore, windowAfter);
        }

        if (StudyDayRegex().Match(combinedLabel) is { Success: true } dayMatch)
        {
            var rawDay = dayMatch.Groups[1].Success ? dayMatch.Groups[1].Value : dayMatch.Groups[2].Value;
            if (int.TryParse(rawDay, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDay))
            {
                int normalized = parsedDay == 0 ? 1 : parsedDay;
                previousScheduledDay = normalized;
                return new ActivityTiming(normalized, windowBefore, windowAfter);
            }
        }

        if (StudyWeekRegex().Match(combinedLabel) is { Success: true } weekMatch)
        {
            var rawWeek = weekMatch.Groups[1].Success ? weekMatch.Groups[1].Value : weekMatch.Groups[2].Value;
            if (int.TryParse(rawWeek, NumberStyles.Integer, CultureInfo.InvariantCulture, out var week) && week > 0)
            {
                int day = week * 7;
                previousScheduledDay = day;
                int before = explicitBefore ?? parsedBefore ?? 3;
                int after = explicitAfter ?? parsedAfter ?? 3;
                return new ActivityTiming(day, Math.Abs(before), Math.Abs(after));
            }
        }

        if (StudyMonthRegex().Match(combinedLabel) is { Success: true } monthMatch
            && int.TryParse(monthMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
            && month > 0)
        {
            int day = month * 28;
            previousScheduledDay = day;
            int before = explicitBefore ?? parsedBefore ?? 7;
            int after = explicitAfter ?? parsedAfter ?? 7;
            return new ActivityTiming(day, Math.Abs(before), Math.Abs(after));
        }

        var lower = eventName.ToLowerInvariant();
        if (lower.Contains("screen", StringComparison.Ordinal))
        {
            previousScheduledDay ??= -7;
            int before = explicitBefore ?? parsedBefore ?? 7;
            int after = explicitAfter ?? parsedAfter ?? 6;
            return new ActivityTiming(-7, Math.Abs(before), Math.Abs(after));
        }

        if (lower.Contains("baseline", StringComparison.Ordinal)
            || lower.Contains("random", StringComparison.Ordinal)
            || lower.Contains("enrol", StringComparison.Ordinal))
        {
            previousScheduledDay = 1;
            return new ActivityTiming(1, windowBefore, windowAfter);
        }

        if (eventRepeating && string.IsNullOrWhiteSpace(eventType))
        {
            return null;
        }

        int nextDay = previousScheduledDay is null or <= 0 ? 1 : previousScheduledDay.Value + 7;
        previousScheduledDay = nextDay;
        int defaultWindow = nextDay == 1 ? 0 : 2;
        return new ActivityTiming(
            nextDay,
            explicitBefore ?? parsedBefore ?? defaultWindow,
            explicitAfter ?? parsedAfter ?? defaultWindow);
    }

    private static int? TryReadIntMetadata(XElement eventDef, XElement? eventRef, params string[] names)
    {
        foreach (var source in new[] { eventRef, eventDef }.Where(x => x is not null))
        {
            foreach (var attr in source!.Attributes())
            {
                if (names.Any(n => string.Equals(attr.Name.LocalName, n, StringComparison.OrdinalIgnoreCase))
                    && int.TryParse(attr.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAttr))
                {
                    return parsedAttr;
                }
            }

            foreach (var alias in Children(source, "Alias"))
            {
                var ctx = NormalizeAliasContext(Attr(alias, "Context"));
                var val = Attr(alias, "Name");
                if (!string.IsNullOrWhiteSpace(ctx)
                    && !string.IsNullOrWhiteSpace(val)
                    && names.Any(n => string.Equals(ctx, n, StringComparison.OrdinalIgnoreCase))
                    && int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAlias))
                {
                    return parsedAlias;
                }
            }
        }

        return null;
    }

    private static (int? Before, int? After) ParseWindowFromText(string text, XElement eventDef, XElement? eventRef)
    {
        var candidates = new List<string> { text };
        foreach (var source in new[] { eventRef, eventDef }.Where(x => x is not null))
        {
            foreach (var alias in Children(source, "Alias"))
            {
                var ctx = NormalizeAliasContext(Attr(alias, "Context"));
                var val = Attr(alias, "Name");
                if (string.Equals(ctx, "window", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val))
                {
                    candidates.Insert(0, val.Trim());
                }
            }
        }

        foreach (var candidate in candidates)
        {
            if (AsymmetricWindowRegex().Match(candidate) is { Success: true } asym
                && int.TryParse(asym.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asymBefore)
                && int.TryParse(asym.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asymAfter))
            {
                return (asymBefore, asymAfter);
            }

            if (SymmetricWindowRegex().Match(candidate) is { Success: true } sym
                && int.TryParse(sym.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var symDays))
            {
                return (symDays, symDays);
            }
        }

        return (null, null);
    }

    [GeneratedRegex(@"\b(?:Study\s*Day|Day)\s*([+-]?\d+)\b|\bD([+-]?\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex StudyDayRegex();

    [GeneratedRegex(@"\bWeek\s*(\d+)\b|\bWk\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex StudyWeekRegex();

    [GeneratedRegex(@"\bMonth\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex StudyMonthRegex();

    [GeneratedRegex(@"(?:±|\+/-)\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SymmetricWindowRegex();

    [GeneratedRegex(@"-\s*(\d+)\s*/\s*\+\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex AsymmetricWindowRegex();
}
