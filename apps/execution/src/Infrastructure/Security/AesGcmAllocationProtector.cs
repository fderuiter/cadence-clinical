using System.Collections.Generic;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Security.FieldProtection;

using Microsoft.Extensions.Options;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// The randomisation key (#5607), bound from <see cref="ConfigurationKey"/>. There is no default, and the service
/// refuses to start without a valid key. It must differ from every other key the service holds, so that a reader who
/// holds the demographics or field-encryption key still learns no arm.
/// @req:PRD-SUB-001
/// </summary>
public sealed class AllocationProtectionOptions
{
    /// <summary>The configuration key (and environment variable) holding the base64 AES-256 randomisation key.</summary>
    public const string ConfigurationKey = "RANDOMIZATION_ALLOCATION_KEY";

    /// <summary>The message startup validation fails with when <see cref="KeyBase64"/> is not a usable key.</summary>
    public const string InvalidKeyMessage =
        "RANDOMIZATION_ALLOCATION_KEY must be set to a base64-encoded 32-byte key (generate one with `openssl rand -base64 32`).";

    /// <summary>Base64-encoded 32-byte key.</summary>
    public string? KeyBase64 { get; set; }
}

/// <summary>
/// <see cref="IAllocationProtector"/> over the shared AES-256-GCM field format (<see cref="AesGcmFieldEncryptor"/>:
/// a random nonce per value, the key id and the purpose authenticated), keyed by the randomisation key alone.
/// @req:PRD-SUB-001
/// </summary>
public sealed class AesGcmAllocationProtector(IOptions<AllocationProtectionOptions> options) : IAllocationProtector
{
    private readonly AesGcmFieldEncryptor _encryptor = new(options.Value.KeyBase64);

    /// <inheritdoc />
    public string Protect(string arm, string purpose) => _encryptor.Encrypt(arm, purpose);

    /// <inheritdoc />
    public IReadOnlyList<string> ProtectBatch(IReadOnlyList<(string Arm, string Purpose)> items) => _encryptor.EncryptBatch(items);

    /// <inheritdoc />
    public string Reveal(string ciphertext, string purpose) => _encryptor.Decrypt(ciphertext, purpose);
}
