using System.Security.Cryptography;
using System.Text.Json;

using Cadence.Apps.Execution.Application.Abstractions;

using Microsoft.Extensions.Options;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// AES-GCM demographics encryption (Rule 7, `01_spec.md` §3), replacing the legacy's hardcoded-key
/// Fernet (AES-128-CBC+HMAC) symmetric encryption. Fails loudly
/// (<see cref="DemographicsDecryptionFailedException"/>) on any decrypt error rather than the
/// legacy's silent-swallow patterns (raise site #22).
/// @req:PRD-SUB-001
/// </summary>
public sealed class AesGcmDemographicsEncryptionService : IDemographicsEncryptionService
{
    private readonly byte[] _key;

    public AesGcmDemographicsEncryptionService(IOptions<DemographicsEncryptionOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.KeyBase64);
    }

    public string Encrypt(IReadOnlyDictionary<string, object?> demographics)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(demographics);
        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return Convert.ToBase64String([.. nonce, .. ciphertext, .. tag]);
    }

    public IReadOnlyDictionary<string, object?> Decrypt(string ciphertext)
    {
        try
        {
            int nonceSize = AesGcm.NonceByteSizes.MaxSize;
            int tagSize = AesGcm.TagByteSizes.MaxSize;

            byte[] blob = Convert.FromBase64String(ciphertext);
            var nonce = blob.AsSpan(0, nonceSize);
            var tag = blob.AsSpan(blob.Length - tagSize, tagSize);
            var body = blob.AsSpan(nonceSize, blob.Length - nonceSize - tagSize);

            byte[] plaintext = new byte[body.Length];
            using var aes = new AesGcm(_key, tagSize);
            aes.Decrypt(nonce, body, tag, plaintext);

            using var document = JsonDocument.Parse(plaintext);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new DemographicsDecryptionFailedException("Decrypted payload was not a JSON object.");

            var result = new Dictionary<string, object?>();
            foreach (var property in document.RootElement.EnumerateObject())
                result[property.Name] = ToClrValue(property.Value);
            return result;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            throw new DemographicsDecryptionFailedException("Demographics ciphertext could not be decrypted.", ex);
        }
    }

    private static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ToClrValue(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(ToClrValue).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out long integer) ? integer : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
