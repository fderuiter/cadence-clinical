using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Application.Simulation;

internal static class SimulationVisibleDigestWriter
{
    internal static string ComputeVisibleDigest(
        IReadOnlyList<SimulationSiteSnapshot> sites,
        IReadOnlyList<SimulationEffectivitySnapshot> effectivities,
        IReadOnlyList<SimulationEnrollmentSnapshot> enrollments,
        IReadOnlyList<SimulationObservationSnapshot> observations,
        IReadOnlyList<SimulationCollectionSnapshot> collections) =>
        ContentHash.ComputeStreaming(writer =>
        {
            writer.WriteStartObject();

            writer.WritePropertyName("collections"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                collections,
                static c => c.Id.Value,
                writer,
                static (w, c) => WriteCollectionDigestEntry(w, c));
            writer.WriteEndArray();

            writer.WritePropertyName("effectivities"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                effectivities,
                static e => e.Id.Value,
                writer,
                static (w, e) => WriteEffectivityDigestEntry(w, e));
            writer.WriteEndArray();

            writer.WritePropertyName("enrollments"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                enrollments,
                static e => e.Id.Value,
                writer,
                static (w, e) => WriteEnrollmentDigestEntry(w, e));
            writer.WriteEndArray();

            writer.WritePropertyName("observations"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                observations,
                static o => o.Id.Value,
                writer,
                static (w, o) => WriteObservationDigestEntry(w, o));
            writer.WriteEndArray();

            writer.WritePropertyName("sites"u8);
            writer.WriteStartArray();
            ZeroLohOrdering.ForEachOrderedByGuid(
                sites,
                static s => s.Id.Value,
                writer,
                static (w, s) => WriteSiteDigestEntry(w, s));
            writer.WriteEndArray();

            writer.WriteEndObject();
        });

    private static void WriteCollectionDigestEntry(Utf8JsonWriter writer, SimulationCollectionSnapshot collection)
    {
        Span<char> guidChars = stackalloc char[36];
        writer.WriteStartObject();
        writer.WriteString("activity"u8, collection.PlannedActivityId);
        WriteGuidProperty(writer, "id"u8, collection.Id.Value, guidChars);
        writer.WritePropertyName("items"u8);
        writer.WriteStartArray();
        WriteSortedCollectionItems(writer, collection.ValuesByItemId);
        writer.WriteEndArray();
        writer.WriteString("spec"u8, collection.CollectionSpecId);
        writer.WriteEndObject();
    }

    private static void WriteSortedCollectionItems(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, SimulationCollectedItemSnapshot> items)
    {
        var count = items.Count;
        if (count == 0)
        {
            return;
        }

        if (count <= ZeroLohOrdering.MaxSohChunkLength)
        {
            var rented = ArrayPool<KeyValuePair<string, SimulationCollectedItemSnapshot>>.Shared.Rent(count);
            try
            {
                var idx = 0;
                foreach (var kv in items)
                {
                    rented[idx++] = kv;
                }

                if (idx > 1)
                {
                    Array.Sort(rented, 0, idx, CollectedItemKeyComparer.Instance);
                }

                for (var i = 0; i < idx; i++)
                {
                    var entry = rented[i];
                    writer.WriteStartObject();
                    writer.WriteString("item"u8, entry.Key);
                    WriteObservationDisplayValue(writer, "value"u8, entry.Value.Value);
                    writer.WriteEndObject();
                }
            }
            finally
            {
                ArrayPool<KeyValuePair<string, SimulationCollectedItemSnapshot>>.Shared.Return(rented, clearArray: true);
            }

            return;
        }

        ZeroLohOrdering.ForEachOrderedByKey(
            items,
            static kv => kv.Key,
            writer,
            static (w, entry) =>
            {
                w.WriteStartObject();
                w.WriteString("item"u8, entry.Key);
                WriteObservationDisplayValue(w, "value"u8, entry.Value.Value);
                w.WriteEndObject();
            });
    }

    private static void WriteEffectivityDigestEntry(Utf8JsonWriter writer, SimulationEffectivitySnapshot effectivity)
    {
        Span<char> guidChars = stackalloc char[36];
        writer.WriteStartObject();
        WriteGuidProperty(writer, "id"u8, effectivity.Id.Value, guidChars);
        WriteGuidProperty(writer, "protocolVersion"u8, effectivity.ProtocolVersionId.Value, guidChars);
        writer.WriteString("status"u8, effectivity.StatusAsOf?.ToString());
        writer.WriteEndObject();
    }

    private static void WriteEnrollmentDigestEntry(Utf8JsonWriter writer, SimulationEnrollmentSnapshot enrollment)
    {
        Span<char> guidChars = stackalloc char[36];
        Span<char> dateChars = stackalloc char[10];
        writer.WriteStartObject();
        WriteGuidProperty(writer, "id"u8, enrollment.Id.Value, guidChars);
        _ = enrollment.ReferenceStartDate.TryFormat(dateChars, out var dateLen, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        writer.WriteString("referenceStart"u8, dateChars[..dateLen]);
        writer.WriteString("status"u8, enrollment.StatusAsOf.ToString());
        writer.WriteString("subjectCode"u8, enrollment.SubjectCode);
        writer.WriteEndObject();
    }

    private static void WriteObservationDigestEntry(Utf8JsonWriter writer, SimulationObservationSnapshot observation)
    {
        Span<char> scratch = stackalloc char[64];
        writer.WriteStartObject();
        WriteConceptRefProperty(writer, "concept"u8, observation.Concept, scratch);
        WriteGuidProperty(writer, "id"u8, observation.Id.Value, scratch[..36]);
        WriteObservationDisplayValue(writer, "value"u8, observation.Value);
        writer.WriteEndObject();
    }

    private static void WriteSiteDigestEntry(Utf8JsonWriter writer, SimulationSiteSnapshot site)
    {
        Span<char> guidChars = stackalloc char[36];
        writer.WriteStartObject();
        writer.WriteBoolean("active"u8, site.IsActive);
        writer.WriteString("country"u8, site.Country);
        WriteGuidProperty(writer, "id"u8, site.Id.Value, guidChars);
        writer.WriteString("status"u8, site.Status);
        writer.WriteEndObject();
    }

    private static void WriteGuidProperty(Utf8JsonWriter writer, ReadOnlySpan<byte> propertyName, Guid id, Span<char> scratch)
    {
        _ = id.TryFormat(scratch, out var len, "D");
        writer.WriteString(propertyName, scratch[..len]);
    }

    private static void WriteConceptRefProperty(Utf8JsonWriter writer, ReadOnlySpan<byte> propertyName, ConceptRef concept, Span<char> scratch)
    {
        _ = concept.ConceptId.Value.TryFormat(scratch, out var guidLen, "D");
        scratch[guidLen] = '@';
        _ = concept.Version.TryFormat(scratch[(guidLen + 1)..], out var verLen, default, CultureInfo.InvariantCulture);
        writer.WriteString(propertyName, scratch[..(guidLen + 1 + verLen)]);
    }

    private static void WriteObservationDisplayValue(Utf8JsonWriter writer, ReadOnlySpan<byte> propertyName, ObservationValue value)
    {
        switch (value)
        {
            case QuantityValue q:
                var comp = q.Comparator switch
                {
                    QuantityComparator.LessThan => "<",
                    QuantityComparator.LessOrEqual => "<=",
                    QuantityComparator.GreaterThan => ">",
                    QuantityComparator.GreaterOrEqual => ">=",
                    _ => string.Empty,
                };
                Span<char> scratch = stackalloc char[160];
                comp.AsSpan().CopyTo(scratch);
                var pos = comp.Length;
                if (q.Value.TryFormat(scratch[pos..], out var valLen, default, CultureInfo.InvariantCulture)
                    && pos + valLen + 1 + q.Unit.Length <= scratch.Length)
                {
                    pos += valLen;
                    scratch[pos++] = ' ';
                    q.Unit.AsSpan().CopyTo(scratch[pos..]);
                    pos += q.Unit.Length;
                    writer.WriteString(propertyName, scratch[..pos]);
                    return;
                }

                writer.WriteString(propertyName, q.ToDisplayString());
                return;
            case CodedValue coded:
                writer.WriteString(propertyName, coded.Code);
                return;
            case TextValue text:
                writer.WriteString(propertyName, text.Value);
                return;
            case BooleanValue boolean:
                writer.WriteString(propertyName, boolean.Value ? "true"u8 : "false"u8);
                return;
            case DateTimeValue dateTime:
                writer.WriteString(propertyName, dateTime.Value);
                return;
            case MissingValue { Detail: null } missing:
                writer.WriteString(propertyName, missing.Reason.ToString());
                return;
            default:
                writer.WriteString(propertyName, value.ToDisplayString());
                return;
        }
    }

    private sealed class CollectedItemKeyComparer : IComparer<KeyValuePair<string, SimulationCollectedItemSnapshot>>
    {
        public static readonly CollectedItemKeyComparer Instance = new();

        public int Compare(KeyValuePair<string, SimulationCollectedItemSnapshot> x, KeyValuePair<string, SimulationCollectedItemSnapshot> y) =>
            string.CompareOrdinal(x.Key, y.Key);
    }
}
