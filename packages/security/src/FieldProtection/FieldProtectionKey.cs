using System;
using System.Security.Cryptography;
using System.Text;

namespace Cadence.Security.FieldProtection;

/// <summary>
/// Parses and validates a base64-encoded field-protection key from configuration. There is no default key and no
/// fallback: a missing, malformed or too-short key throws, so a host that needs one fails at startup.
/// @req:PRD-SYS-053
/// </summary>
public static class FieldProtectionKey
{
    /// <summary>Required key length in bytes (256-bit keys for both AES-256-GCM and HMAC-SHA256).</summary>
    public const int KeySizeBytes = 32;

    /// <summary>
    /// Decodes <paramref name="base64Key"/> and checks it is exactly <see cref="KeySizeBytes"/> bytes.
    /// </summary>
    /// <param name="base64Key">The configured key, base64 encoded (generate with <c>openssl rand -base64 32</c>).</param>
    /// <param name="configurationKey">The configuration key the value came from, named in the error message.</param>
    /// <exception cref="InvalidOperationException">The key is missing, not base64, or not 32 bytes long.</exception>
    public static byte[] Decode(string? base64Key, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException(
                $"{configurationKey} must be set to a base64-encoded {KeySizeBytes}-byte key (openssl rand -base64 {KeySizeBytes}).");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{configurationKey} is not valid base64.", ex);
        }

        if (key.Length != KeySizeBytes)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                $"{configurationKey} must decode to exactly {KeySizeBytes} bytes.");
        }

        return key;
    }

    /// <summary>
    /// True when <paramref name="base64Key"/> would be accepted by <see cref="Decode"/>; used by options validation.
    /// </summary>
    public static bool IsValid(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[(base64Key.Length * 3 / 4) + 3];
        return Convert.TryFromBase64String(base64Key.Trim(), buffer, out int written) && written == KeySizeBytes;
    }

    /// <summary>
    /// A short, non-secret identifier for a key: the first 8 bytes of HMAC-SHA256(key, label), hex encoded. It lets
    /// stored values name the key that protected them without revealing anything about the key itself.
    /// </summary>
    internal static string ComputeKeyId(byte[] key, string label)
    {
        byte[] mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(label));
        return Convert.ToHexStringLower(mac.AsSpan(0, 8));
    }
}
