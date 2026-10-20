using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Cadence.Kernel.Trust;

namespace Cadence.Modules.Intelligence.Grounding;

/// <summary>
/// The comparable form of a value (ADR-2633 §4.6): null members removed, then RFC 8785 canonical JSON. The value a tool
/// showed and the value a model quoted compare equal exactly when they say the same thing, whatever their member order,
/// number spelling (<c>6.20</c> and <c>6.2</c>) or omitted nulls.
/// </summary>
public static class ValueFingerprint
{
    /// <summary>The fingerprint of <paramref name="json"/>, or null when it is not one JSON value.</summary>
    public static string? Of(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonNode.Parse(json) is { } node ? JsonCanonicalization.Canonicalize(WithoutNulls(node).ToJsonString()) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The fingerprint of <paramref name="value"/>, or null when it is undefined or null.</summary>
    public static string? Of(JsonElement value) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : Of(value.GetRawText());

    private static JsonNode WithoutNulls(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Where(p => p.Value is null).Select(p => p.Key).ToList())
                {
                    obj.Remove(name);
                }

                foreach (var (_, child) in obj)
                {
                    WithoutNulls(child!);
                }

                break;
            case JsonArray array:
                foreach (var child in array.OfType<JsonNode>())
                {
                    WithoutNulls(child);
                }

                break;
        }

        return node;
    }
}
