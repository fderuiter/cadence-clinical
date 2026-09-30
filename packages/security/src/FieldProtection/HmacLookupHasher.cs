using System;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace Cadence.Security.FieldProtection;

/// <summary>
/// <see cref="ILookupHasher"/> computing HMAC-SHA256 over <c>purpose ‖ 0x00 ‖ value</c> with the key from
/// <see cref="LookupHashOptions"/>. The purpose separates hash domains, so the same raw value hashed for two different
/// purposes cannot be correlated.
/// @req:PRD-SYS-053
/// </summary>
public sealed class HmacLookupHasher : ILookupHasher
{
    private readonly byte[] _key;

    /// <summary>Creates the hasher from bound options; a missing or invalid key throws.</summary>
    public HmacLookupHasher(IOptions<LookupHashOptions> options)
        : this(options?.Value.KeyBase64)
    {
    }

    /// <summary>Creates the hasher from a base64-encoded 32-byte key; a missing or invalid key throws.</summary>
    public HmacLookupHasher(string? base64Key)
    {
        _key = FieldProtectionKey.Decode(base64Key, LookupHashOptions.ConfigurationKey);
        KeyId = FieldProtectionKey.ComputeKeyId(_key, "cadence.lookup-hash.key-id.v1");
    }

    /// <inheritdoc />
    public string KeyId { get; }

    /// <inheritdoc />
    public string ComputeHash(string value, string purpose)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        int purposeLength = Encoding.UTF8.GetByteCount(purpose);
        byte[] input = new byte[purposeLength + 1 + Encoding.UTF8.GetByteCount(value)];
        Encoding.UTF8.GetBytes(purpose, input);
        Encoding.UTF8.GetBytes(value, input.AsSpan(purposeLength + 1));

        byte[] mac = HMACSHA256.HashData(_key, input);
        CryptographicOperations.ZeroMemory(input);
        return Convert.ToHexStringLower(mac);
    }
}
