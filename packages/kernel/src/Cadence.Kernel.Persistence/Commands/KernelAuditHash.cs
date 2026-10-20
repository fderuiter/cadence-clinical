using System;
using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Cadence.Kernel.Persistence.Commands;

/// <summary>
/// The kernel audit chain's hashing rule (spec §5.3): SHA-256, lower-case hex, over a canonical JSON array of every
/// field of <see cref="AuditEntryRecord"/> except <see cref="AuditEntryRecord.Hash"/>, in a fixed order. Times are
/// UTC with exactly six fractional digits, the precision PostgreSQL stores, so a chain verifies after a database
/// round-trip. A JSON array (rather than a delimiter-joined string) makes field boundaries unambiguous.
/// </summary>
internal static class KernelAuditHash
{
    /// <summary>Predecessor of a tenant's first entry.</summary>
    public const string Genesis = "";

    public static string Compute(AuditEntryRecord entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteStringValue("cadence.kernel.audit/v1");
            writer.WriteStringValue(entry.TenantId.Value.ToString("D"));
            writer.WriteNumberValue(entry.Sequence);
            writer.WriteStringValue(FormatTime(entry.RecordedAt));
            writer.WriteStringValue(entry.ActorId);
            writer.WriteStringValue(entry.Action);
            WriteNullable(writer, entry.AggregateType);
            WriteNullable(writer, entry.AggregateId);
            if (entry.AggregateVersion is { } version)
            {
                writer.WriteNumberValue(version);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteStringValue(entry.ReasonForChange);
            writer.WriteStringValue(entry.ActivityId.ToString("D"));
            writer.WriteStringValue(entry.Changes);
            writer.WriteStringValue(entry.PredecessorHash);
            writer.WriteEndArray();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    internal static string FormatTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    private static void WriteNullable(Utf8JsonWriter writer, string? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
