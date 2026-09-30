using System.Collections.Generic;

using Cadence.Security.FieldProtection;

using Microsoft.AspNetCore.Hosting;

namespace Cadence.Testing.Security;

/// <summary>
/// Test-only field-protection keys and ready-made <see cref="IFieldEncryptor"/> / <see cref="ILookupHasher"/>
/// instances. Production code has no default keys; tests take theirs from here.
/// </summary>
public static class TestFieldProtection
{
    /// <summary>Test-only AES-256 field-encryption key (base64 of 32 ASCII bytes).</summary>
    public const string EncryptionKeyBase64 = "Y2FkZW5jZS10ZXN0LWZpZWxkLWVuY3J5cHRpb24tMDE="; // pragma: allowlist secret

    /// <summary>A second test-only encryption key, for wrong-key and rotation tests.</summary>
    public const string AlternateEncryptionKeyBase64 = "Y2FkZW5jZS10ZXN0LWZpZWxkLWVuY3J5cHRpb24tMDI="; // pragma: allowlist secret

    /// <summary>Test-only HMAC-SHA256 lookup-hash key (base64 of 32 ASCII bytes).</summary>
    public const string LookupHashKeyBase64 = "Y2FkZW5jZS10ZXN0LWxvb2t1cC1oYXNoLWtleS0wMDE="; // pragma: allowlist secret

    /// <summary>Configuration entries supplying both keys, for hosts built in tests.</summary>
    public static IReadOnlyDictionary<string, string?> ConfigurationValues { get; } = new Dictionary<string, string?>
    {
        [FieldEncryptionOptions.ConfigurationKey] = EncryptionKeyBase64,
        [LookupHashOptions.ConfigurationKey] = LookupHashKeyBase64,
    };

    /// <summary>An encryptor using <see cref="EncryptionKeyBase64"/>.</summary>
    public static IFieldEncryptor CreateEncryptor() => new AesGcmFieldEncryptor(EncryptionKeyBase64);

    /// <summary>A lookup hasher using <see cref="LookupHashKeyBase64"/>.</summary>
    public static ILookupHasher CreateLookupHasher() => new HmacLookupHasher(LookupHashKeyBase64);

    /// <summary>Supplies both test keys so a host's field-protection startup validation passes.</summary>
    public static IWebHostBuilder UseTestFieldProtectionKeys(this IWebHostBuilder builder) =>
        builder
            .UseSetting(FieldEncryptionOptions.ConfigurationKey, EncryptionKeyBase64)
            .UseSetting(LookupHashOptions.ConfigurationKey, LookupHashKeyBase64);
}
