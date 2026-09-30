namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// Binds the AES-GCM demographics encryption key from configuration. The key must never appear as
/// a literal in source — see `01_spec.md` §3's hardcoded-Fernet-key trap this closes.
/// The service refuses to start unless the key is valid (<see cref="IsValidKey"/>), so a missing or
/// malformed key fails at boot rather than on the first subject registration.
/// @req:PRD-SUB-001
/// </summary>
public sealed class DemographicsEncryptionOptions
{
    /// <summary>The configuration section the options bind from (<c>DemographicsEncryption__KeyBase64</c>).</summary>
    public const string SectionName = "DemographicsEncryption";

    /// <summary>The message startup validation fails with when <see cref="KeyBase64"/> is not a usable key.</summary>
    public const string InvalidKeyMessage =
        "DemographicsEncryption__KeyBase64 must be set to a Base64-encoded AES key of 16, 24 or 32 bytes " +
        "(generate one with `openssl rand -base64 32`).";

    /// <summary>Base64-encoded AES key material.</summary>
    public string KeyBase64 { get; set; } = string.Empty;

    /// <summary>True when <paramref name="keyBase64"/> is Base64 that decodes to an AES-128, -192 or -256 key.</summary>
    public static bool IsValidKey(string? keyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            return false;
        }

        Span<byte> key = stackalloc byte[64];
        return Convert.TryFromBase64String(keyBase64.Trim(), key, out int length) && length is 16 or 24 or 32;
    }
}
