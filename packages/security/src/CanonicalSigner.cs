using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cadence.Security;

/// <summary>
/// Provides canonical JSON serialization and HMAC-SHA256 signature generation/verification.
/// Canonical form: object keys sorted by ordinal (UTF-16 code unit) order, no insignificant whitespace, and
/// only the escapes JSON requires (quote, backslash, control characters). HTML-sensitive characters
/// (<c>&lt; &gt; &amp; '</c>) and non-ASCII text are written literally, byte-for-byte the same as
/// <c>canonicalSerialize</c> in <c>packages/ui/signing.js</c> (<c>JSON.stringify</c>), so a signature made
/// on either side verifies on the other.
/// @req:PRD-SYS-053
/// </summary>
public static class CanonicalSigner
{
    private static readonly JsonSerializerOptions CanonicalWriterOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] CanonicalSerialize(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        JsonNode? node = payload switch
        {
            JsonNode jn => jn,
            string jsonStr => JsonNode.Parse(jsonStr),
            _ => JsonSerializer.SerializeToNode(payload)
        };

        var sortedNode = SortJsonNode(node);
        string json = sortedNode?.ToJsonString(CanonicalWriterOptions) ?? "null";
        return Encoding.UTF8.GetBytes(json);
    }

    private static JsonNode? SortJsonNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var sortedObj = new JsonObject();
            var sortedProperties = obj.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
            foreach (var prop in sortedProperties)
            {
                var val = prop.Value;
                sortedObj.Add(prop.Key, SortJsonNode(val?.DeepClone()));
            }
            return sortedObj;
        }
        if (node is JsonArray arr)
        {
            var sortedArr = new JsonArray();
            foreach (var item in arr)
            {
                sortedArr.Add(SortJsonNode(item?.DeepClone()));
            }
            return sortedArr;
        }
        return node?.DeepClone();
    }

    public static string GenerateCanonicalSignature(object payload, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        byte[] serialized = CanonicalSerialize(payload);
        using var hmac = new HMACSHA256(secret);
        byte[] hash = hmac.ComputeHash(serialized);
        return Convert.ToHexStringLower(hash);
    }

    public static string GenerateCanonicalSignature(object payload, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return GenerateCanonicalSignature(payload, Encoding.UTF8.GetBytes(secret));
    }

    public static bool VerifyCanonicalSignature(object payload, string signature, byte[] secret)
    {
        if (string.IsNullOrWhiteSpace(signature) || secret == null || secret.Length == 0)
        {
            return false;
        }

        string expected = GenerateCanonicalSignature(payload, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant())
        );
    }

    public static bool VerifyCanonicalSignature(object payload, string signature, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }
        return VerifyCanonicalSignature(payload, signature, Encoding.UTF8.GetBytes(secret));
    }
}
