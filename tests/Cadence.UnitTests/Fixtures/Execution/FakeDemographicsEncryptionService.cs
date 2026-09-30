using System.Text.Json;
using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Infrastructure.Security;

namespace Cadence.UnitTests.Fixtures.Execution;

/// <summary>
/// Shared test double for <see cref="IDemographicsEncryptionService"/> providing consistent
/// mock encryption prefix transformations and configurable decryption behavior.
/// </summary>
public sealed class FakeDemographicsEncryptionService : IDemographicsEncryptionService
{
    public const string Prefix = "FAKE-CIPHER:";
    public const string CorruptedCiphertext = "CORRUPTED-NOT-VALID-CIPHERTEXT";

    private readonly IReadOnlyDictionary<string, object?>? _payload;
    private readonly bool _throwsOnDecrypt;

    public FakeDemographicsEncryptionService()
    {
    }

    public FakeDemographicsEncryptionService(
        IReadOnlyDictionary<string, object?>? payload,
        bool throwsOnDecrypt = false)
    {
        _payload = payload;
        _throwsOnDecrypt = throwsOnDecrypt;
    }

    public static FakeDemographicsEncryptionService ReturningPayload(IReadOnlyDictionary<string, object?> payload) =>
        new(payload, throwsOnDecrypt: false);

    public static FakeDemographicsEncryptionService ThrowingOnDecrypt() =>
        new(null, throwsOnDecrypt: true);

    public int EncryptCallCount { get; private set; }

    public List<string> DecryptedCiphertexts { get; } = new();

    public string Encrypt(IReadOnlyDictionary<string, object?> demographics)
    {
        EncryptCallCount++;
        return Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(demographics));
    }

    public IReadOnlyDictionary<string, object?> Decrypt(string ciphertext)
    {
        if (_throwsOnDecrypt || ciphertext == CorruptedCiphertext)
        {
            throw new DemographicsDecryptionFailedException("Demographics ciphertext could not be decrypted.");
        }

        DecryptedCiphertexts.Add(ciphertext);

        if (_payload != null)
        {
            return _payload;
        }

        if (ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
        {
            try
            {
                var bytes = Convert.FromBase64String(ciphertext[Prefix.Length..]);
                var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(bytes);
                if (dict != null)
                {
                    return dict;
                }
            }
            catch
            {
                // Fall back if deserialization fails
            }
        }

        return new Dictionary<string, object?> { ["marker"] = "decrypted" };
    }
}
