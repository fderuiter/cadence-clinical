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

    private static readonly JsonWriterOptions StreamingWriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = true,
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

    /// <summary>
    /// Streams canonical JSON (with object properties written in ordinal key order at every depth) directly into an
    /// incremental SHA-256 hash without materializing a <see cref="JsonNode"/> tree or buffered document string.
    /// </summary>
    public static string ComputeStreaming(Action<Utf8JsonWriter> writeCanonicalJson)
    {
        ArgumentNullException.ThrowIfNull(writeCanonicalJson);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var buffer = new IncrementalHashBufferWriter(hash);
        using (var writer = new Utf8JsonWriter(buffer, StreamingWriterOptions))
        {
            writeCanonicalJson(writer);
            writer.Flush();
        }

        buffer.Flush();
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        hash.GetHashAndReset(digest);
        return Prefix + Convert.ToHexStringLower(digest);
    }

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

    private sealed class IncrementalHashBufferWriter(IncrementalHash hash) : IBufferWriter<byte>, IDisposable
    {
        private const int DefaultBufferSize = 16384;
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
        private int _written;

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        public void Flush()
        {
            if (_written > 0)
            {
                hash.AppendData(_buffer.AsSpan(0, _written));
                _written = 0;
            }
        }

        public void Dispose()
        {
            var toReturn = _buffer;
            _buffer = [];
            if (toReturn.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(toReturn);
            }
        }

        private void EnsureCapacity(int sizeHint)
        {
            var required = sizeHint <= 0 ? 256 : sizeHint;
            if (_buffer.Length - _written >= required)
            {
                return;
            }

            Flush();
            if (_buffer.Length >= required)
            {
                return;
            }

            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(required);
        }
    }
}
