namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// Thrown by <see cref="AesGcmDemographicsEncryptionService.Decrypt"/> on any decrypt failure
/// (tamper, wrong key, malformed base64, non-object payload). Never carries the failed plaintext
/// or ciphertext in its message — this replaces the legacy's silent-swallow decrypt behavior
/// (raise site #22) with a loud, non-PII-leaking failure.
/// @req:PRD-SUB-001
/// </summary>
public sealed class DemographicsDecryptionFailedException(string message, Exception? inner = null)
    : Exception(message, inner);
