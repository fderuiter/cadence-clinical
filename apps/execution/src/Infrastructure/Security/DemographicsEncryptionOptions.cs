namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// Binds the AES-GCM demographics encryption key from configuration. The key must never appear as
/// a literal in source — see `01_spec.md` §3's hardcoded-Fernet-key trap this closes.
/// @req:PRD-SUB-001
/// </summary>
public sealed class DemographicsEncryptionOptions
{
    /// <summary>Base64-encoded AES key material.</summary>
    public string KeyBase64 { get; set; } = string.Empty;
}
