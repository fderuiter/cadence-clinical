using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cadence.Kernel.Trust;

/// <summary>
/// The kernel's content hash, which an attestation binds to (spec §9.5, 21 CFR 11.70): <c>sha256:</c> followed by the
/// lower-case hex SHA-256 of the canonical form of a JSON document. The algorithm prefix lets a later algorithm sit
/// beside this one without ambiguity. Protocol versions, standard versions and library items all hash this way; artifact versions hash their raw bytes with
/// <see cref="OfBytes"/>. The canonical form sorts object keys by ordinal comparison at every depth, keeps array
/// order, drops insignificant whitespace and uses relaxed escaping, the same form as
/// <c>Cadence.Security.CanonicalSigner</c>. Two serialisations of the same document therefore hash the same, and any
/// change to a value, key or array element changes the hash.
/// </summary>
public static class ContentHash
{
    /// <summary>The algorithm prefix of every content hash.</summary>
    public const string Prefix = "sha256:";

    /// <summary>The length of a content hash: the prefix and 64 hex characters.</summary>
    public const int Length = 71;

    /// <summary>A PostgreSQL regular expression that matches exactly the well-formed hashes.</summary>
    public const string Pattern = "^sha256:[0-9a-f]{64}$";

    private static readonly SearchValues<char> LowerHexDigits = SearchValues.Create("0123456789abcdef");

    private static readonly JsonSerializerOptions CanonicalWriter = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The canonical form of <paramref name="json"/>.</summary>
    /// <exception cref="JsonException">The text is not a single JSON value.</exception>
    public static string Canonicalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        return Sort(node)?.ToJsonString(CanonicalWriter) ?? "null";
    }

    /// <summary>The content hash of the canonical form of <paramref name="json"/>.</summary>
    /// <exception cref="JsonException">The text is not a single JSON value.</exception>
    public static string Compute(string json) => OfCanonical(Canonicalize(json));

    /// <summary>The content hash of text that is already canonical.</summary>
    public static string OfCanonical(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// The content hash of raw bytes, for content that is not JSON (an artifact version's file): <c>sha256:</c> and the
    /// lower-case hex SHA-256 of the bytes as stored.
    /// </summary>
    public static string OfBytes(ReadOnlySpan<byte> content) => Prefix + Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>True when <paramref name="value"/> has the shape of a hash from <see cref="Compute"/>.</summary>
    public static bool IsWellFormed(string? value) =>
        value is { Length: Length }
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && !value.AsSpan(Prefix.Length).ContainsAnyExcept(LowerHexDigits);

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Sort(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
        _ => node?.DeepClone(),
    };
}
