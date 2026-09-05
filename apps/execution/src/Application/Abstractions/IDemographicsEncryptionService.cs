namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Rule 7 demographics encryption seam (`01_spec.md` §3). Implementations must source their key
/// exclusively from configuration — never a literal in source — closing the legacy hardcoded-key
/// Fernet trap that made the Python implementation a "hard blocker for the .NET port."
/// @req:PRD-SUB-001
/// </summary>
public interface IDemographicsEncryptionService
{
    /// <summary>Serializes and encrypts <paramref name="demographics"/>, returning a base64 blob.</summary>
    string Encrypt(IReadOnlyDictionary<string, object?> demographics);

    /// <summary>
    /// Decrypts <paramref name="ciphertext"/>. Throws
    /// <see cref="Cadence.Apps.Execution.Infrastructure.Security.DemographicsDecryptionFailedException"/>
    /// on any failure (tamper, wrong key, malformed input, non-object payload) — never returns a
    /// sentinel or default value.
    /// </summary>
    IReadOnlyDictionary<string, object?> Decrypt(string ciphertext);
}
