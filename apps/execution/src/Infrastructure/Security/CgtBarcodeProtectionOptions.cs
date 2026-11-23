namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// CGT tamper-evident barcode HMAC-SHA256 key, bound from <see cref="ConfigurationKey"/>. There is no default, and the
/// service refuses to start without a valid 32-byte key.
/// </summary>
public sealed class CgtBarcodeProtectionOptions
{
    /// <summary>The configuration key (and environment variable) holding the base64 32-byte CGT barcode HMAC key.</summary>
    public const string ConfigurationKey = "CGT_BARCODE_HMAC_KEY";

    /// <summary>The message startup validation fails with when <see cref="KeyBase64"/> is not a usable key.</summary>
    public const string InvalidKeyMessage =
        "CGT_BARCODE_HMAC_KEY must be set to a base64-encoded 32-byte key (generate one with `openssl rand -base64 32`).";

    /// <summary>Base64-encoded 32-byte HMAC-SHA256 key.</summary>
    public string? KeyBase64 { get; set; }
}
