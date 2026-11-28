using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;

namespace Cadence.Kernel.Simulation;

internal static class SimulationCanonicalJsonWriter
{
    internal static void WriteCanonicalResult(Utf8JsonWriter writer, SimulationResult result)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("asOf"u8);
        writer.WriteStartObject();
        writer.WriteString("knownAt"u8, SimulationResultHashing.Instant(result.AsOf.KnownAt));
        writer.WriteString("validAt"u8, SimulationResultHashing.Instant(result.AsOf.ValidAt));
        writer.WriteEndObject();

        writer.WritePropertyName("candidate"u8);
        writer.WriteStartObject();
        writer.WriteString("baseContentHash"u8, result.Candidate.BaseContentHash);
        writer.WriteString("baseVersion"u8, result.Candidate.BaseVersion?.ToString());
        writer.WriteString("contentHash"u8, result.Candidate.ContentHash);
        writer.WriteString("kind"u8, result.Candidate.Kind.ToString());
        writer.WriteString("version"u8, result.Candidate.Version?.ToString());
        writer.WriteEndObject();

        WriteDependents(writer, result.Dependents);
        WriteEngines(writer, result.EngineVersions);
        writer.WriteString("inputHash"u8, result.InputHash);
        WriteMasked(writer, result.Masked);
        WriteMatrixGaps(writer, result.MatrixGaps);
        WriteOperationalImpact(writer, result.OperationalImpact);
        WriteProjectionChanges(writer, result.ProjectionChanges);
        WriteScheduleChanges(writer, result.ScheduleChanges);
        writer.WriteString("scope"u8, result.Scope.ToString());
        WriteStructuralDiff(writer, result.StructuralDiff);
        WriteValidationDelta(writer, result.ValidationDelta);

        writer.WriteEndObject();
    }

    private static void WriteDependents(Utf8JsonWriter writer, DependentsReport? dependents)
    {
        if (dependents is null)
        {
            writer.WriteNull("dependents"u8);
            return;
        }

        writer.WritePropertyName("dependents"u8);
        writer.WriteStartObject();
        writer.WriteNumber("depth"u8, dependents.Depth);
        writer.WritePropertyName("groups"u8);
        writer.WriteStartArray();
        for (var i = 0; i < dependents.Groups.Count; i++)
        {
            var group = dependents.Groups[i];
            writer.WriteStartObject();
            writer.WriteNumber("count"u8, group.Count);
            writer.WriteString("kind"u8, group.Kind);
            writer.WritePropertyName("objects"u8);
            writer.WriteStartArray();
            for (var j = 0; j < group.Objects.Count; j++)
            {
                var obj = group.Objects[j];
                writer.WriteStartObject();
                writer.WriteNumber("distance"u8, obj.Distance);
                writer.WriteString("handle"u8, obj.Handle);
                writer.WriteString("mask"u8, obj.Mask?.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("start"u8, dependents.Start);
        writer.WriteNumber("total"u8, dependents.Total);
        writer.WriteBoolean("truncated"u8, dependents.Truncated);
        writer.WriteEndObject();
    }

    private static void WriteEngines(Utf8JsonWriter writer, IReadOnlyList<EngineVersion> engines)
    {
        writer.WritePropertyName("engineVersions"u8);
        writer.WriteStartArray();
        for (var i = 0; i < engines.Count; i++)
        {
            var engine = engines[i];
            writer.WriteStartObject();
            writer.WriteString("name"u8, engine.Name);
            writer.WriteString("version"u8, engine.Version);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteMasked(Utf8JsonWriter writer, IReadOnlyList<MaskedPart> masked)
    {
        writer.WritePropertyName("masked"u8);
        writer.WriteStartArray();
        for (var i = 0; i < masked.Count; i++)
        {
            var item = masked[i];
            writer.WriteStartObject();
            writer.WriteString("part"u8, item.Part);
            writer.WriteString("reason"u8, item.Reason.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteMatrixGaps(Utf8JsonWriter writer, IReadOnlyList<MatrixGapFinding>? gaps)
    {
        if (gaps is null)
        {
            writer.WriteNull("matrixGaps"u8);
            return;
        }

        writer.WritePropertyName("matrixGaps"u8);
        writer.WriteStartArray();
        for (var i = 0; i < gaps.Count; i++)
        {
            var gap = gaps[i];
            writer.WriteStartObject();
            writer.WriteString("description"u8, gap.Description);
            writer.WriteString("elementId"u8, gap.ElementId);
            writer.WriteString("jsonPointerPath"u8, gap.JsonPointerPath);
            writer.WriteString("kind"u8, gap.Kind.ToString());
            writer.WriteString("recommendation"u8, gap.Recommendation);
            writer.WriteString("severity"u8, gap.Severity.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteOperationalImpact(Utf8JsonWriter writer, OperationalImpactSummary? impact)
    {
        if (impact is null)
        {
            writer.WriteNull("operationalImpact"u8);
            return;
        }

        writer.WritePropertyName("operationalImpact"u8);
        writer.WriteStartObject();
        writer.WriteNumber("activeSiteCount"u8, impact.ActiveSiteCount);
        writer.WriteNumber("affectedParticipantCount"u8, impact.AffectedParticipantCount);

        writer.WritePropertyName("hidden"u8);
        writer.WriteStartObject();
        writer.WriteBoolean("moreExist"u8, impact.Hidden.MoreExist);
        writer.WritePropertyName("reasons"u8);
        writer.WriteStartArray();
        for (var i = 0; i < impact.Hidden.Reasons.Count; i++)
        {
            writer.WriteStringValue(impact.Hidden.Reasons[i].ToString());
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("participants"u8);
        writer.WriteStartArray();
        Span<char> dateChars = stackalloc char[10];
        for (var i = 0; i < impact.ParticipantImpacts.Count; i++)
        {
            var participant = impact.ParticipantImpacts[i];
            writer.WriteStartObject();
            writer.WriteString("cohort"u8, participant.Cohort.ToString());
            writer.WriteString("enrollment"u8, participant.Enrollment.ToString());
            writer.WriteBoolean("requiresReconsent"u8, participant.RequiresReconsent);
            writer.WritePropertyName("scheduleShifts"u8);
            writer.WriteStartArray();
            for (var j = 0; j < participant.ScheduleShifts.Count; j++)
            {
                var shift = participant.ScheduleShifts[j];
                writer.WriteStartObject();
                writer.WriteString("activityId"u8, shift.ActivityId);
                WriteParticipantVisit(writer, "after"u8, shift.After, dateChars);
                WriteParticipantVisit(writer, "before"u8, shift.Before, dateChars);
                writer.WriteString("change"u8, shift.Change.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("status"u8, participant.Status.ToString());
            writer.WriteString("subjectCode"u8, participant.SubjectCode);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("pendingSiteCount"u8, impact.PendingSiteCount);
        writer.WriteNumber("reconsentParticipantCount"u8, impact.ReconsentParticipantCount);
        writer.WriteNumber("totalParticipantCount"u8, impact.TotalParticipantCount);
        writer.WriteEndObject();
    }

    private static void WriteParticipantVisit(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        ParticipantPlannedVisit? visit,
        Span<char> dateChars)
    {
        if (visit is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        _ = visit.TargetDate.TryFormat(dateChars, out var targetLen, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        writer.WriteString("targetDate"u8, dateChars[..targetLen]);
        writer.WriteString("visitId"u8, visit.VisitId);
        _ = visit.WindowEnd.TryFormat(dateChars, out var endLen, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        writer.WriteString("windowEnd"u8, dateChars[..endLen]);
        _ = visit.WindowStart.TryFormat(dateChars, out var startLen, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        writer.WriteString("windowStart"u8, dateChars[..startLen]);
        writer.WriteEndObject();
    }

    private static void WriteProjectionChanges(Utf8JsonWriter writer, IReadOnlyList<ProjectionDatasetChange>? changes)
    {
        if (changes is null)
        {
            writer.WriteNull("projectionChanges"u8);
            return;
        }

        writer.WritePropertyName("projectionChanges"u8);
        writer.WriteStartArray();
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            writer.WriteStartObject();
            writer.WriteNumber("addedCount"u8, change.AddedCount);
            writer.WriteNumber("changedCount"u8, change.ChangedCount);
            writer.WriteString("dataset"u8, change.Dataset);
            writer.WriteNumber("removedCount"u8, change.RemovedCount);
            writer.WritePropertyName("samples"u8);
            writer.WriteStartArray();
            for (var j = 0; j < change.Samples.Count; j++)
            {
                var sample = change.Samples[j];
                writer.WriteStartObject();
                WriteSortedStringDictionary(writer, "after"u8, sample.After);
                WriteSortedStringDictionary(writer, "before"u8, sample.Before);
                writer.WriteString("change"u8, sample.Change.ToString());
                writer.WriteString("recordKey"u8, sample.RecordKey);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSortedStringDictionary(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        IReadOnlyDictionary<string, string?>? values)
    {
        if (values is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        if (values is SortedDictionary<string, string?> sorted)
        {
            foreach (var (key, value) in sorted)
            {
                writer.WriteString(key, value);
            }
        }
        else if (values.Count > 0)
        {
            var keys = new string[values.Count];
            var idx = 0;
            foreach (var kv in values)
            {
                keys[idx++] = kv.Key;
            }

            Array.Sort(keys, StringComparer.Ordinal);
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                writer.WriteString(key, values[key]);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteScheduleChanges(Utf8JsonWriter writer, IReadOnlyList<ScheduleChange> changes)
    {
        writer.WritePropertyName("scheduleChanges"u8);
        writer.WriteStartArray();
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            writer.WriteStartObject();
            writer.WriteString("activityId"u8, change.ActivityId);
            WritePlannedVisit(writer, "after"u8, change.After);
            WritePlannedVisit(writer, "before"u8, change.Before);
            writer.WriteString("change"u8, change.Change.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePlannedVisit(Utf8JsonWriter writer, ReadOnlySpan<byte> propertyName, PlannedVisit? visit)
    {
        if (visit is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("epochId"u8, visit.EpochId);
        writer.WriteNumber("studyDay"u8, ExpectedSchedule.NormalizeStudyDay(visit.StudyDay));
        writer.WriteNumber("windowAfter"u8, visit.WindowAfter);
        writer.WriteNumber("windowBefore"u8, visit.WindowBefore);
        writer.WriteEndObject();
    }

    private static void WriteStructuralDiff(Utf8JsonWriter writer, IReadOnlyList<ElementChange> diff)
    {
        writer.WritePropertyName("structuralDiff"u8);
        writer.WriteStartArray();
        for (var i = 0; i < diff.Count; i++)
        {
            var change = diff[i];
            writer.WriteStartObject();
            writer.WriteString("change"u8, change.Change.ToString());
            writer.WriteString("elementId"u8, change.ElementId);
            writer.WriteString("instanceType"u8, change.InstanceType);
            writer.WritePropertyName("properties"u8);
            writer.WriteStartArray();
            for (var j = 0; j < change.Properties.Count; j++)
            {
                writer.WriteStringValue(change.Properties[j]);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteValidationDelta(Utf8JsonWriter writer, ValidationDeltaSummary? delta)
    {
        if (delta is null)
        {
            writer.WriteNull("validationDelta"u8);
            return;
        }

        writer.WritePropertyName("validationDelta"u8);
        writer.WriteStartObject();
        WriteFindingsArray(writer, "opening"u8, delta.Opening);
        WriteFindingsArray(writer, "resolving"u8, delta.Resolving);
        writer.WriteNumber("unchangedCount"u8, delta.UnchangedCount);
        writer.WriteEndObject();
    }

    private static void WriteFindingsArray(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        IReadOnlyList<SimulatedValidationFinding> findings)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        for (var i = 0; i < findings.Count; i++)
        {
            var finding = findings[i];
            writer.WriteStartObject();
            writer.WriteString("dataset"u8, finding.Dataset);
            writer.WriteString("explanation"u8, finding.Explanation);
            writer.WriteString("ruleId"u8, finding.RuleId);
            writer.WriteNumber("ruleVersion"u8, finding.RuleVersion);
            writer.WriteString("severity"u8, finding.Severity.ToString());
            writer.WriteString("stage"u8, finding.Stage.ToString());
            writer.WriteString("subject"u8, finding.Subject);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
