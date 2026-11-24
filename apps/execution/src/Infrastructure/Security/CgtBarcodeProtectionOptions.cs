using Cadence.Security.FieldProtection;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// The 32-byte HMAC-SHA256 signing key for Cell &amp; Gene Therapy vein-to-vein bag barcodes, bound from
/// <see cref="ConfigurationKey"/>. There is no source literal fallback: the Execution host refuses to start without a
/// valid key so forged barcodes can never be accepted under a shared default secret (ADR-2666).
/// @req:SRS-SYS-005
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

    /// <summary>Decodes <see cref="KeyBase64"/> to raw 32-byte key material, throwing if missing or invalid.</summary>
    public byte[] GetKeyBytes() => FieldProtectionKey.Decode(KeyBase64, ConfigurationKey);
}
