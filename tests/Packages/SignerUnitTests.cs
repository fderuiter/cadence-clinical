using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

using Cadence.Security;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Behavioural tests for <see cref="CanonicalSigner"/> and <see cref="AsymmetricSigner"/>, which sign and
/// verify de-identification certificates and redaction manifests in <c>Cadence.Deid</c>.
/// </summary>
public class CanonicalSignerTests
{
    private const string Secret = "canonical-test-secret";

    private static JsonObject SamplePayload() => new()
    {
        ["zeta"] = 1,
        ["alpha"] = new JsonObject { ["delta"] = new JsonArray(3, 1, 2), ["charlie"] = "x" },
        ["bravo"] = null,
        ["echo"] = true,
        ["foxtrot"] = 1.5,
    };

    /// <summary>
    /// Known-answer vector produced by <c>canonicalSerialize</c> + HMAC-SHA256 in <c>packages/ui/signing.js</c>
    /// (Node): HTML-sensitive characters, non-ASCII text and control characters must serialize and sign
    /// identically in .NET and JavaScript.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public void CanonicalSerialize_MatchesTheJavaScriptSignerForHtmlSensitiveAndNonAsciiText()
    {
        var payload = new JsonObject
        {
            ["note"] = "a<b & c>d 'quoted' \"dq\"",
            ["site"] = "Zürich – São Paulo",
            ["tab"] = "x\ty\nz",
            ["n"] = 3,
            ["arr"] = new JsonArray(new JsonObject { ["z"] = 1, ["a"] = "é" }),
        };

        string json = Encoding.UTF8.GetString(CanonicalSigner.CanonicalSerialize(payload));

        json.Should().Be("{\"arr\":[{\"a\":\"é\",\"z\":1}],\"n\":3,\"note\":\"a<b & c>d 'quoted' \\\"dq\\\"\",\"site\":\"Zürich – São Paulo\",\"tab\":\"x\\ty\\nz\"}");
        CanonicalSigner.GenerateCanonicalSignature(payload, "kat-secret-for-canonical-json-0001")
            .Should().Be("e159c3f99891f02afe5fdbc4bf407b89c4c1d511cccf67c904f2ae49cd57dbb4");
    }

    [Fact]
    public void CanonicalSerialize_SortsKeysRecursivelyAndKeepsArrayOrder()
    {
        string json = Encoding.UTF8.GetString(CanonicalSigner.CanonicalSerialize(SamplePayload()));

        json.Should().Be("{\"alpha\":{\"charlie\":\"x\",\"delta\":[3,1,2]},\"bravo\":null,\"echo\":true,\"foxtrot\":1.5,\"zeta\":1}");
    }

    [Fact]
    public void CanonicalSerialize_IsIndependentOfInputKeyOrderAndInputShape()
    {
        var reordered = new Dictionary<string, object?>
        {
            ["foxtrot"] = 1.5,
            ["echo"] = true,
            ["bravo"] = null,
            ["alpha"] = new Dictionary<string, object> { ["charlie"] = "x", ["delta"] = new[] { 3, 1, 2 } },
            ["zeta"] = 1,
        };
        const string rawJson = "{ \"zeta\": 1, \"echo\": true, \"alpha\": { \"delta\": [3, 1, 2], \"charlie\": \"x\" }, \"foxtrot\": 1.5, \"bravo\": null }";

        byte[] expected = CanonicalSigner.CanonicalSerialize(SamplePayload());

        CanonicalSigner.CanonicalSerialize(reordered).Should().Equal(expected);
        CanonicalSigner.CanonicalSerialize(rawJson).Should().Equal(expected);
    }

    [Fact]
    public void GenerateCanonicalSignature_MatchesTheUiPackageForAsciiPayloads()
    {
        // Known-answer vector from packages/ui/signing.js generateCanonicalSignature(samplePayload, Secret).
        CanonicalSigner.GenerateCanonicalSignature(SamplePayload(), Secret)
            .Should().Be("5100aa0423c23fa9416e966a8fe219f90d94f91b791f29c8c990544c88f7a768"); // pragma: allowlist secret
    }

    [Fact]
    public void VerifyCanonicalSignature_AcceptsOwnSignatureInEitherHexCase()
    {
        string signature = CanonicalSigner.GenerateCanonicalSignature(SamplePayload(), Secret);

        CanonicalSigner.VerifyCanonicalSignature(SamplePayload(), signature, Secret).Should().BeTrue();
        CanonicalSigner.VerifyCanonicalSignature(SamplePayload(), signature.ToUpperInvariant(), Secret).Should().BeTrue();
    }

    [Fact]
    public void VerifyCanonicalSignature_RejectsTamperedPayload()
    {
        string signature = CanonicalSigner.GenerateCanonicalSignature(SamplePayload(), Secret);
        var tampered = SamplePayload();
        tampered["zeta"] = 2;

        CanonicalSigner.VerifyCanonicalSignature(tampered, signature, Secret).Should().BeFalse();
    }

    [Fact]
    public void VerifyCanonicalSignature_RejectsWrongSecret()
    {
        string signature = CanonicalSigner.GenerateCanonicalSignature(SamplePayload(), Secret);

        CanonicalSigner.VerifyCanonicalSignature(SamplePayload(), signature, "another-secret").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-hex")]
    public void VerifyCanonicalSignature_RejectsBlankOrMalformedSignature(string signature)
    {
        CanonicalSigner.VerifyCanonicalSignature(SamplePayload(), signature, Secret).Should().BeFalse();
    }

    [Fact]
    public void VerifyCanonicalSignature_RejectsBlankSecretInsteadOfThrowing()
    {
        string signature = CanonicalSigner.GenerateCanonicalSignature(SamplePayload(), Secret);

        CanonicalSigner.VerifyCanonicalSignature(SamplePayload(), signature, "").Should().BeFalse();
        CanonicalSigner.VerifyCanonicalSignature(SamplePayload(), signature, Array.Empty<byte>()).Should().BeFalse();
    }

    [Fact]
    public void GenerateCanonicalSignature_RejectsBlankSecret()
    {
        var act = () => CanonicalSigner.GenerateCanonicalSignature(SamplePayload(), " ");

        act.Should().Throw<ArgumentException>();
    }
}

public class AsymmetricSignerTests
{
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("{\"manifest\":\"redaction\",\"version\":1}");

    [Fact]
    public void RsaPss_RoundTripsWithPublicKeyPem()
    {
        using var rsa = RSA.Create(2048);

        string signature = AsymmetricSigner.AsymmetricSign(Data, rsa.ExportPkcs8PrivateKeyPem());

        AsymmetricSigner.AsymmetricVerify(Data, signature, rsa.ExportSubjectPublicKeyInfoPem()).Should().BeTrue();
    }

    [Fact]
    public void Ecdsa_RoundTripsWithPublicKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string signature = AsymmetricSigner.AsymmetricSign(Data, ecdsa.ExportPkcs8PrivateKeyPem());

        AsymmetricSigner.AsymmetricVerify(Data, signature, ecdsa.ExportSubjectPublicKeyInfoPem()).Should().BeTrue();
    }

    [Fact]
    public void RsaPss_RoundTripsWithCertificatePem()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=cadence-deid-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

        string signature = AsymmetricSigner.AsymmetricSign(Data, rsa.ExportPkcs8PrivateKeyPem());

        AsymmetricSigner.AsymmetricVerify(Data, signature, cert.ExportCertificatePem()).Should().BeTrue();
    }

    [Fact]
    public void Ecdsa_RoundTripsWithCertificatePem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=cadence-deid-test", ecdsa, HashAlgorithmName.SHA256);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

        string signature = AsymmetricSigner.AsymmetricSign(Data, ecdsa.ExportPkcs8PrivateKeyPem());

        AsymmetricSigner.AsymmetricVerify(Data, signature, cert.ExportCertificatePem()).Should().BeTrue();
    }

    [Fact]
    public void RsaPss_SignsWithPasswordProtectedKey()
    {
        using var rsa = RSA.Create(2048);
        byte[] password = Encoding.UTF8.GetBytes("key-password");
        string encryptedPem = rsa.ExportEncryptedPkcs8PrivateKeyPem(
            "key-password",
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000));

        string signature = AsymmetricSigner.AsymmetricSign(Data, encryptedPem, password);

        AsymmetricSigner.AsymmetricVerify(Data, signature, rsa.ExportSubjectPublicKeyInfoPem()).Should().BeTrue();
    }

    [Fact]
    public void Verify_RejectsTamperedData()
    {
        using var rsa = RSA.Create(2048);
        string signature = AsymmetricSigner.AsymmetricSign(Data, rsa.ExportPkcs8PrivateKeyPem());
        byte[] tampered = (byte[])Data.Clone();
        tampered[^2] ^= 0x01;

        AsymmetricSigner.AsymmetricVerify(tampered, signature, rsa.ExportSubjectPublicKeyInfoPem()).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsSignatureFromADifferentKey()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string signature = AsymmetricSigner.AsymmetricSign(Data, signer.ExportPkcs8PrivateKeyPem());

        AsymmetricSigner.AsymmetricVerify(Data, signature, other.ExportSubjectPublicKeyInfoPem()).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsEcdsaSignatureAgainstAnRsaKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rsa = RSA.Create(2048);
        string signature = AsymmetricSigner.AsymmetricSign(Data, ecdsa.ExportPkcs8PrivateKeyPem());

        AsymmetricSigner.AsymmetricVerify(Data, signature, rsa.ExportSubjectPublicKeyInfoPem()).Should().BeFalse();
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("")]
    public void Verify_RejectsMalformedSignature(string signature)
    {
        using var rsa = RSA.Create(2048);

        AsymmetricSigner.AsymmetricVerify(Data, signature, rsa.ExportSubjectPublicKeyInfoPem()).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsGarbageKeyMaterialInsteadOfThrowing()
    {
        using var rsa = RSA.Create(2048);
        string signature = AsymmetricSigner.AsymmetricSign(Data, rsa.ExportPkcs8PrivateKeyPem());

        AsymmetricSigner.AsymmetricVerify(Data, signature, "-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----").Should().BeFalse();
    }

    [Fact]
    public void Sign_ThrowsForUnparseablePrivateKey()
    {
        var act = () => AsymmetricSigner.AsymmetricSign(Data, "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----");

        act.Should().Throw<Exception>();
    }
}
