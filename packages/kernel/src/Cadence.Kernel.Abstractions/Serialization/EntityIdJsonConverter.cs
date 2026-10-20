using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Kernel.Serialization;

/// <summary>
/// Generic JsonConverter for clinical kernel entity identifiers, serializing as UUID strings for Native AOT and System.Text.Json.
/// </summary>
public class EntityIdJsonConverter<TId> : JsonConverter<TId>
    where TId : struct, IEntityId<TId>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return TId.Empty;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            if (reader.TryGetGuid(out var guid))
            {
                return TId.FromGuid(guid);
            }

            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return TId.Empty;
            }

            if (TId.TryParse(str, out var id))
            {
                return id;
            }

            throw new JsonException($"Unable to parse '{str}' as {typeof(TId).Name}.");
        }

        throw new JsonException($"Unexpected token {reader.TokenType} when parsing {typeof(TId).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }

    public override TId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str))
        {
            return TId.Empty;
        }

        if (TId.TryParse(str, out var id))
        {
            return id;
        }

        throw new JsonException($"Unable to parse property name '{str}' as {typeof(TId).Name}.");
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.ToString() ?? string.Empty);
    }
}
