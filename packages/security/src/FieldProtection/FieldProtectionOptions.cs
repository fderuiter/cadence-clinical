namespace Cadence.Security.FieldProtection;

/// <summary>
/// Key for <see cref="AesGcmFieldEncryptor"/>. Bound from <see cref="ConfigurationKey"/>; there is no default.
/// @req:PRD-SYS-053
/// </summary>
public sealed class FieldEncryptionOptions
{
    /// <summary>The configuration key (and environment variable) holding the base64 AES-256 field-encryption key.</summary>
    public const string ConfigurationKey = "FIELD_ENCRYPTION_KEY";

    /// <summary>Base64-encoded 32-byte key.</summary>
    public string? KeyBase64 { get; set; }
}

/// <summary>
/// Key for <see cref="HmacLookupHasher"/>. Bound from <see cref="ConfigurationKey"/>; there is no default. It must
/// differ from the field-encryption key so that each key serves exactly one purpose.
/// @req:PRD-SYS-053
/// </summary>
public sealed class LookupHashOptions
{
    /// <summary>The configuration key (and environment variable) holding the base64 HMAC-SHA256 lookup-hash key.</summary>
    public const string ConfigurationKey = "FIELD_LOOKUP_HASH_KEY";

    /// <summary>Base64-encoded 32-byte key.</summary>
    public string? KeyBase64 { get; set; }
}
