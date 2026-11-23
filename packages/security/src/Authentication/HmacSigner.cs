using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cadence.Security.Authentication;

/// <summary>
/// High-throughput HMAC-SHA256 signature generation and validation for inter-service Gateway authentication and canonical serialization.
/// @req:PRD-SYS-053
/// </summary>
public static class HmacSigner
{
    private const int DefaultBufferCapacity = 512;
    private const int StackAllocThreshold = 512;

    private static readonly JsonWriterOptions CanonicalWriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Pre-encoded canonical gateway payload keys in strict StringComparer.Ordinal order.
    private static readonly JsonEncodedText ChangeReasonProperty = JsonEncodedText.Encode("change_reason", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText RolesProperty = JsonEncodedText.Encode("roles", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText SigTokenProperty = JsonEncodedText.Encode("sig_token", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText SiteIdProperty = JsonEncodedText.Encode("site_id", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText SponsorIdProperty = JsonEncodedText.Encode("sponsor_id", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText StudyIdsProperty = JsonEncodedText.Encode("study_ids", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText TenantIdProperty = JsonEncodedText.Encode("tenant_id", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText TenantProductFamiliesProperty = JsonEncodedText.Encode("tenant_product_families", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText TenantReadOnlyProperty = JsonEncodedText.Encode("tenant_read_only", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText TenantScopingProperty = JsonEncodedText.Encode("tenant_scoping", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText TimestampProperty = JsonEncodedText.Encode("timestamp", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText UnblindedAccessProperty = JsonEncodedText.Encode("unblinded_access", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static readonly JsonEncodedText UserIdProperty = JsonEncodedText.Encode("user_id", JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

    private static readonly Comparison<KeyValuePair<string, object?>> OrdinalPairComparison =
        static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key);

    [ThreadStatic]
    private static PooledUtf8JsonWriter? _cachedWriter;

    public static string ComputeSignature(string payload, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        ComputePayloadDigest(payload, secret, hash);
        return Convert.ToHexStringLower(hash);
    }

    public static bool VerifySignature(string payload, string secret, string expectedSignature)
    {
        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(expectedSignature))
        {
            return false;
        }

        Span<byte> actualHash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        ComputePayloadDigest(payload, secret, actualHash);
        return FixedTimeEqualsHex(actualHash, expectedSignature);
    }

    public static byte[] CanonicalSerialize(IDictionary<string, object?> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var pooled = PooledUtf8JsonWriter.Rent();
        WriteCanonicalDictionary(pooled.Writer, payload);
        pooled.Writer.Flush();
        return pooled.WrittenSpan.ToArray();
    }

    public static string GenerateGatewaySignature(
        string userId,
        string roles,
        string timestamp,
        byte[] secret,
        string? changeReason = null,
        string? siteId = null,
        string? studyIds = null,
        string? sponsorId = null,
        bool unblindedAccess = false,
        string? tenantId = null,
        string? sigToken = null,
        bool tenantReadOnly = false,
        string? tenantProductFamilies = null,
        string? tenantScoping = null)
    {
        ArgumentNullException.ThrowIfNull(secret);

        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        ComputeGatewaySignatureDigest(
            userId, roles, timestamp, secret, changeReason, siteId, studyIds, sponsorId, unblindedAccess, tenantId, sigToken, tenantReadOnly, tenantProductFamilies, tenantScoping, hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Verifies a gateway identity signature against the one canonical payload
    /// <see cref="GenerateGatewaySignature"/> produces. There are deliberately no fallback payload shapes: every
    /// signed field must match exactly, so a caller cannot drop a scope field (site, study, sponsor, tenant, unblinded
    /// access, tenant read-only state, product families, or scoping caps) and still pass verification.
    /// </summary>
    public static bool VerifyGatewaySignature(
        string userId,
        string roles,
        string timestamp,
        string signature,
        byte[] secret,
        string? changeReason = null,
        string? siteId = null,
        string? studyIds = null,
        string? sponsorId = null,
        bool unblindedAccess = false,
        string? tenantId = null,
        string? sigToken = null,
        bool tenantReadOnly = false,
        string? tenantProductFamilies = null,
        string? tenantScoping = null)
    {
        if (string.IsNullOrWhiteSpace(signature) || secret is null || secret.Length == 0)
        {
            return false;
        }

        Span<byte> expectedHash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        ComputeGatewaySignatureDigest(
            userId, roles, timestamp, secret, changeReason, siteId, studyIds, sponsorId, unblindedAccess, tenantId, sigToken, tenantReadOnly, tenantProductFamilies, tenantScoping, expectedHash);

        return FixedTimeEqualsHex(expectedHash, signature);
    }

    public static string ComputeSha256Hash(string input)
    {
        if (input is null)
        {
            return string.Empty;
        }

        int maxByteCount = Encoding.UTF8.GetMaxByteCount(input.Length);
        byte[]? rented = maxByteCount <= StackAllocThreshold
            ? null
            : ArrayPool<byte>.Shared.Rent(maxByteCount);
        Span<byte> utf8Buffer = rented is null
            ? stackalloc byte[StackAllocThreshold]
            : rented;

        try
        {
            int byteCount = Encoding.UTF8.GetBytes(input.AsSpan(), utf8Buffer);
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(utf8Buffer[..byteCount], hash);
            return Convert.ToHexStringLower(hash);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public static string ComputeSha256Hash(byte[] input)
    {
        if (input is null || input.Length == 0)
        {
            return string.Empty;
        }

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        return Convert.ToHexStringLower(hash);
    }

    private static void ComputeGatewaySignatureDigest(
        string? userId,
        string? roles,
        string? timestamp,
        ReadOnlySpan<byte> secret,
        string? changeReason,
        string? siteId,
        string? studyIds,
        string? sponsorId,
        bool unblindedAccess,
        string? tenantId,
        string? sigToken,
        bool tenantReadOnly,
        string? tenantProductFamilies,
        string? tenantScoping,
        Span<byte> destination)
    {
        using var pooled = PooledUtf8JsonWriter.Rent();
        var writer = pooled.Writer;

        writer.WriteStartObject();
        writer.WriteString(ChangeReasonProperty, changeReason ?? string.Empty);
        writer.WriteString(RolesProperty, roles ?? string.Empty);
        if (sigToken is not null)
        {
            writer.WriteString(SigTokenProperty, sigToken);
        }

        writer.WriteString(SiteIdProperty, siteId ?? string.Empty);
        writer.WriteString(SponsorIdProperty, sponsorId ?? string.Empty);
        writer.WriteString(StudyIdsProperty, studyIds ?? string.Empty);
        writer.WriteString(TenantIdProperty, tenantId ?? string.Empty);
        if (tenantProductFamilies is not null)
        {
            writer.WriteString(TenantProductFamiliesProperty, tenantProductFamilies);
        }

        if (tenantReadOnly)
        {
            writer.WriteBoolean(TenantReadOnlyProperty, true);
        }

        if (tenantScoping is not null)
        {
            writer.WriteString(TenantScopingProperty, tenantScoping);
        }

        writer.WriteString(TimestampProperty, timestamp ?? string.Empty);
        writer.WriteBoolean(UnblindedAccessProperty, unblindedAccess);
        writer.WriteString(UserIdProperty, userId ?? string.Empty);
        writer.WriteEndObject();
        writer.Flush();

        HMACSHA256.HashData(secret, pooled.WrittenSpan, destination);
    }

    private static void ComputePayloadDigest(string payload, string secret, Span<byte> destination)
    {
        int maxKeyBytes = Encoding.UTF8.GetMaxByteCount(secret.Length);
        int maxPayloadBytes = Encoding.UTF8.GetMaxByteCount(payload.Length);

        byte[]? rentedKey = maxKeyBytes <= StackAllocThreshold
            ? null
            : ArrayPool<byte>.Shared.Rent(maxKeyBytes);
        byte[]? rentedPayload = maxPayloadBytes <= StackAllocThreshold
            ? null
            : ArrayPool<byte>.Shared.Rent(maxPayloadBytes);
        Span<byte> keyBuffer = rentedKey is null
            ? stackalloc byte[StackAllocThreshold]
            : rentedKey;
        Span<byte> payloadBuffer = rentedPayload is null
            ? stackalloc byte[StackAllocThreshold]
            : rentedPayload;

        try
        {
            int keyLength = Encoding.UTF8.GetBytes(secret.AsSpan(), keyBuffer);
            int payloadLength = Encoding.UTF8.GetBytes(payload.AsSpan(), payloadBuffer);
            HMACSHA256.HashData(keyBuffer[..keyLength], payloadBuffer[..payloadLength], destination);
        }
        finally
        {
            if (rentedKey is not null)
            {
                keyBuffer.Clear();
                ArrayPool<byte>.Shared.Return(rentedKey);
            }

            if (rentedPayload is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedPayload);
            }
        }
    }

    private static bool FixedTimeEqualsHex(ReadOnlySpan<byte> expectedDigest, string? hexSignature)
    {
        if (string.IsNullOrEmpty(hexSignature) || hexSignature.Length != expectedDigest.Length * 2)
        {
            return false;
        }

        Span<byte> actualDigest = expectedDigest.Length <= 64
            ? stackalloc byte[expectedDigest.Length]
            : new byte[expectedDigest.Length];

        OperationStatus status = Convert.FromHexString(hexSignature.AsSpan(), actualDigest, out int charsConsumed, out int bytesWritten);
        if (status != OperationStatus.Done || charsConsumed != hexSignature.Length || bytesWritten != expectedDigest.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest);
    }

    private static void WriteCanonicalDictionary(Utf8JsonWriter writer, IDictionary<string, object?> dictionary)
    {
        writer.WriteStartObject();
        int count = dictionary.Count;
        if (count > 0)
        {
            KeyValuePair<string, object?>[] rented = ArrayPool<KeyValuePair<string, object?>>.Shared.Rent(count);
            try
            {
                dictionary.CopyTo(rented, 0);
                WriteSortedEntries(writer, rented.AsSpan(0, count));
            }
            finally
            {
                ArrayPool<KeyValuePair<string, object?>>.Shared.Return(rented, clearArray: true);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalReadOnlyDictionary(Utf8JsonWriter writer, IReadOnlyDictionary<string, object?> dictionary)
    {
        writer.WriteStartObject();
        int count = dictionary.Count;
        if (count > 0)
        {
            KeyValuePair<string, object?>[] rented = ArrayPool<KeyValuePair<string, object?>>.Shared.Rent(count);
            try
            {
                int index = 0;
                foreach (var entry in dictionary)
                {
                    rented[index++] = entry;
                }

                WriteSortedEntries(writer, rented.AsSpan(0, index));
            }
            finally
            {
                ArrayPool<KeyValuePair<string, object?>>.Shared.Return(rented, clearArray: true);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalNonGenericDictionary(Utf8JsonWriter writer, IDictionary dictionary)
    {
        writer.WriteStartObject();
        int count = dictionary.Count;
        if (count > 0)
        {
            KeyValuePair<string, object?>[] rented = ArrayPool<KeyValuePair<string, object?>>.Shared.Rent(count);
            try
            {
                int index = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    string key = entry.Key?.ToString() ?? throw new NotSupportedException("Canonical JSON object keys must be non-null strings.");
                    rented[index++] = new KeyValuePair<string, object?>(key, entry.Value);
                }

                WriteSortedEntries(writer, rented.AsSpan(0, index));
            }
            finally
            {
                ArrayPool<KeyValuePair<string, object?>>.Shared.Return(rented, clearArray: true);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteSortedEntries(Utf8JsonWriter writer, Span<KeyValuePair<string, object?>> entries)
    {
        entries.Sort(OrdinalPairComparison);
        foreach (ref readonly var entry in entries)
        {
            writer.WritePropertyName(entry.Key);
            WriteCanonicalValue(writer, entry.Value);
        }
    }

    private static void WriteCanonicalValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case byte[] binary:
                writer.WriteBase64StringValue(binary);
                break;
            case JsonElement element:
                WriteCanonicalElement(writer, element);
                break;
            case JsonNode node:
                WriteCanonicalNode(writer, node);
                break;
            case IDictionary<string, object?> genericDict:
                WriteCanonicalDictionary(writer, genericDict);
                break;
            case IReadOnlyDictionary<string, object?> readOnlyDict:
                WriteCanonicalReadOnlyDictionary(writer, readOnlyDict);
                break;
            case IDictionary untypedDict:
                WriteCanonicalNonGenericDictionary(writer, untypedDict);
                break;
            case IEnumerable sequence:
                writer.WriteStartArray();
                foreach (object? item in sequence)
                {
                    WriteCanonicalValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                WriteCanonicalScalar(writer, value);
                break;
        }
    }

    private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteCanonicalElementObject(writer, element);
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonicalElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteCanonicalElementObject(Utf8JsonWriter writer, JsonElement element)
    {
        writer.WriteStartObject();
        int count = element.GetPropertyCount();
        if (count > 0)
        {
            JsonProperty[] rented = ArrayPool<JsonProperty>.Shared.Rent(count);
            try
            {
                int index = 0;
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    rented[index++] = property;
                }

                Span<JsonProperty> properties = rented.AsSpan(0, index);
                properties.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
                foreach (ref readonly var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }
            }
            finally
            {
                ArrayPool<JsonProperty>.Shared.Return(rented, clearArray: true);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalNode(Utf8JsonWriter writer, JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                WriteCanonicalNodeObject(writer, obj);
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (JsonNode? item in array)
                {
                    if (item is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        WriteCanonicalNode(writer, item);
                    }
                }

                writer.WriteEndArray();
                break;
            case JsonValue jsonValue when jsonValue.TryGetValue(out string? text):
                writer.WriteStringValue(text);
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static void WriteCanonicalNodeObject(Utf8JsonWriter writer, JsonObject obj)
    {
        writer.WriteStartObject();
        int count = obj.Count;
        if (count > 0)
        {
            KeyValuePair<string, JsonNode?>[] rented = ArrayPool<KeyValuePair<string, JsonNode?>>.Shared.Rent(count);
            try
            {
                int index = 0;
                foreach (var entry in obj)
                {
                    rented[index++] = entry;
                }

                Span<KeyValuePair<string, JsonNode?>> entries = rented.AsSpan(0, index);
                entries.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
                foreach (ref readonly var entry in entries)
                {
                    writer.WritePropertyName(entry.Key);
                    if (entry.Value is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        WriteCanonicalNode(writer, entry.Value);
                    }
                }
            }
            finally
            {
                ArrayPool<KeyValuePair<string, JsonNode?>>.Shared.Return(rented, clearArray: true);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalScalar(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case uint number:
                writer.WriteNumberValue(number);
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            case short number:
                writer.WriteNumberValue(number);
                break;
            case ushort number:
                writer.WriteNumberValue(number);
                break;
            case byte number:
                writer.WriteNumberValue(number);
                break;
            case sbyte number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case float number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case Guid guid:
                writer.WriteStringValue(guid);
                break;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime);
                break;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset);
                break;
            case DateOnly dateOnly:
                writer.WriteStringValue(dateOnly.ToString("O", CultureInfo.InvariantCulture));
                break;
            case TimeOnly timeOnly:
                writer.WriteStringValue(timeOnly.ToString("O", CultureInfo.InvariantCulture));
                break;
            case char character:
                Span<char> singleChar = [character];
                writer.WriteStringValue(singleChar);
                break;
            case Uri uri:
                writer.WriteStringValue(uri.OriginalString);
                break;
            case Enum enumValue:
                if (Type.GetTypeCode(enumValue.GetType()) == TypeCode.UInt64)
                {
                    writer.WriteNumberValue(Convert.ToUInt64(enumValue, CultureInfo.InvariantCulture));
                }
                else
                {
                    writer.WriteNumberValue(Convert.ToInt64(enumValue, CultureInfo.InvariantCulture));
                }

                break;
            default:
                throw new NotSupportedException($"Unsupported canonical payload value type '{value.GetType()}'.");
        }
    }

    private sealed class PooledUtf8JsonWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[]? _buffer;
        private int _written;

        private PooledUtf8JsonWriter()
        {
            _buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferCapacity);
            Writer = new Utf8JsonWriter(this, CanonicalWriterOptions);
        }

        public Utf8JsonWriter Writer { get; }

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

        public static PooledUtf8JsonWriter Rent()
        {
            var instance = _cachedWriter;
            if (instance is not null)
            {
                _cachedWriter = null;
                instance.Initialize();
                return instance;
            }

            return new PooledUtf8JsonWriter();
        }

        public void Advance(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            _written += count;
        }

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

        public void Dispose()
        {
            Writer.Reset();
            byte[]? rented = _buffer;
            if (rented is not null)
            {
                rented.AsSpan(0, _written).Clear();
                _buffer = null;
                _written = 0;
                ArrayPool<byte>.Shared.Return(rented);
            }

            Return(this);
        }

        private static void Return(PooledUtf8JsonWriter instance)
        {
            _cachedWriter ??= instance;
        }

        private void Initialize()
        {
            _buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferCapacity);
            _written = 0;
            Writer.Reset(this);
        }

        private void EnsureCapacity(int sizeHint)
        {
            int required = sizeHint <= 0 ? 1 : sizeHint;
            byte[] current = _buffer!;
            if (current.Length - _written >= required)
            {
                return;
            }

            int nextCapacity = Math.Max(current.Length * 2, _written + required);
            byte[] next = ArrayPool<byte>.Shared.Rent(nextCapacity);
            current.AsSpan(0, _written).CopyTo(next);
            current.AsSpan(0, _written).Clear();
            _buffer = next;
            ArrayPool<byte>.Shared.Return(current);
        }
    }
}
