using System;
using System.Buffers;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cadence.Kernel.Domain.Standards;

/// <summary>
/// Canonical JSON and content hashes for governed metadata (spec §6.3). Object members are written in ordinal key
/// order with no insignificant whitespace, strings are re-escaped from their decoded value and numbers keep their JSON
/// text, so the same content hashes to the same value however it was indented or its members ordered. A content hash is <c>sha256:</c> followed by lowercase hex.
/// </summary>
public static class CanonicalJson
{
    /// <summary>Prefix of every content hash.</summary>
    public const string HashPrefix = "sha256:";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, SkipValidation = false };

    /// <summary>Serializes <paramref name="element"/> canonically.</summary>
    public static string Serialize(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            Write(writer, element);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The content hash of <paramref name="element"/>'s canonical form.</summary>
    public static string Hash(JsonElement element) => HashText(Serialize(element));

    /// <summary>The content hash of already-canonical text.</summary>
    public static string HashText(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        return HashPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                // Re-escaped from the decoded value, so "A" and "A" hash alike.
                writer.WriteStringValue(element.GetString());
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
