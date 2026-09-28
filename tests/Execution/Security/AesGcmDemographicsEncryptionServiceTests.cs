using System.Security.Cryptography;
using System.Text;

using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Security;

/// <summary>
/// Coverage for `MIG-SUBJ-015`'s <see cref="AesGcmDemographicsEncryptionService"/>: round-trip
/// fidelity, tamper detection across all three ciphertext segments (nonce/body/tag), malformed
/// input, non-object payloads, wrong-key decryption, and the no-PII-in-exception-message contract.
/// @req:PRD-SUB-001
/// </summary>
[GxPRequirement("PRD-SUB-001")]
public class AesGcmDemographicsEncryptionServiceTests
{
    private static AesGcmDemographicsEncryptionService CreateService(byte[]? key = null)
    {
        key ??= RandomNumberGenerator.GetBytes(32);
        var options = Options.Create(new DemographicsEncryptionOptions
        {
            KeyBase64 = Convert.ToBase64String(key),
        });
        return new AesGcmDemographicsEncryptionService(options);
    }

    private static string EncryptRawJson(byte[] key, string json)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(json);
        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return Convert.ToBase64String([.. nonce, .. ciphertext, .. tag]);
    }

    private static string FlipBitAt(string base64, int byteIndex)
    {
        byte[] blob = Convert.FromBase64String(base64);
        blob[byteIndex] ^= 0x01;
        return Convert.ToBase64String(blob);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void EncryptThenDecrypt_RoundTripsExactly()
    {
        var service = CreateService();
        var original = new Dictionary<string, object?>
        {
            ["gender"] = "F",
            ["age"] = 34L,
            ["consented"] = true,
            ["notes"] = null,
        };

        string ciphertext = service.Encrypt(original);
        var decrypted = service.Decrypt(ciphertext);

        decrypted.Should().BeEquivalentTo(original);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Decrypt_TamperedNonce_ThrowsDecryptionFailed()
    {
        var service = CreateService();
        string ciphertext = service.Encrypt(new Dictionary<string, object?> { ["gender"] = "M" });
        string tampered = FlipBitAt(ciphertext, byteIndex: 0);

        var act = () => service.Decrypt(tampered);

        act.Should().Throw<DemographicsDecryptionFailedException>()
            .WithInnerException<CryptographicException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Decrypt_TamperedCiphertextBody_ThrowsDecryptionFailed()
    {
        var service = CreateService();
        string ciphertext = service.Encrypt(new Dictionary<string, object?> { ["gender"] = "M" });
        int bodyStart = AesGcm.NonceByteSizes.MaxSize;
        string tampered = FlipBitAt(ciphertext, bodyStart);

        var act = () => service.Decrypt(tampered);

        act.Should().Throw<DemographicsDecryptionFailedException>()
            .WithInnerException<CryptographicException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Decrypt_TamperedTag_ThrowsDecryptionFailed()
    {
        var service = CreateService();
        string ciphertext = service.Encrypt(new Dictionary<string, object?> { ["gender"] = "M" });
        byte[] blob = Convert.FromBase64String(ciphertext);
        int lastByteIndex = blob.Length - 1;
        string tampered = FlipBitAt(ciphertext, lastByteIndex);

        var act = () => service.Decrypt(tampered);

        act.Should().Throw<DemographicsDecryptionFailedException>()
            .WithInnerException<CryptographicException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Decrypt_MalformedBase64_ThrowsDecryptionFailedWrappingFormatException()
    {
        var service = CreateService();

        var act = () => service.Decrypt("not-valid-base64!!!");

        act.Should().Throw<DemographicsDecryptionFailedException>()
            .WithInnerException<FormatException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Decrypt_ValidJsonButNotAnObject_ThrowsWithSpecificMessage()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var service = CreateService(key);
        string ciphertext = EncryptRawJson(key, "[1,2,3]");

        var act = () => service.Decrypt(ciphertext);

        act.Should().Throw<DemographicsDecryptionFailedException>()
            .WithMessage("Decrypted payload was not a JSON object.");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Decrypt_WithDifferentKey_ThrowsDecryptionFailed()
    {
        var encryptingService = CreateService();
        var decryptingService = CreateService();
        string ciphertext = encryptingService.Encrypt(new Dictionary<string, object?> { ["gender"] = "M" });

        var act = () => decryptingService.Decrypt(ciphertext);

        act.Should().Throw<DemographicsDecryptionFailedException>()
            .WithInnerException<CryptographicException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Decrypt_OnFailure_ExceptionMessageDoesNotLeakPlaintextOrCiphertext()
    {
        var service = CreateService();
        const string sensitiveValue = "SSN-123-45-6789";
        string ciphertext = service.Encrypt(new Dictionary<string, object?> { ["ssn"] = sensitiveValue });
        string tampered = FlipBitAt(ciphertext, byteIndex: 0);

        DemographicsDecryptionFailedException? thrown = null;
        try
        {
            service.Decrypt(tampered);
        }
        catch (DemographicsDecryptionFailedException ex)
        {
            thrown = ex;
        }

        thrown.Should().NotBeNull();
        thrown!.Message.Should().NotContain(sensitiveValue);
        thrown.Message.Should().NotContain(ciphertext);
        thrown.Message.Should().NotContain(tampered);
    }
}
