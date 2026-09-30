using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Cadence.Compliance;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Parity tests for <see cref="ESignatureVerifier"/> against the Python reference
/// (<c>packages/compliance/services/esignature_verifier.py</c>, deleted once these tests landed and kept in git history): revocation-list normalisation, duplicate
/// PEM and XML-DSig blocks, legacy PKCS#1 v1.5 padding, ECDSA keys and <see cref="ESignatureVerifier.VerifyPdf"/>.
/// </summary>
/// <remarks>
/// The verifier reports any failed document whose text contains "mock" as MOCK_SIGNATURE_DETECTED, and random base64
/// contains the word in about 1 in 700 documents, so every signed fixture here is regenerated until it does not.
/// </remarks>
public class ESignatureVerifierTests
{
    private const string Body = "Visit 3 vital signs reviewed and approved by the investigator.";

    // Serial 0x012C9F = 76959; its hex "12c9f" is not all digits, so decimal and hex spellings differ.
    private static readonly Credentials Rsa = CreateRsaCredentials([0x01, 0x2C, 0x9F]);

    // Serial 0x1234 = 4660; its hex "1234" is all digits, which the Python matches as hex text too.
    private static readonly Credentials DigitsOnlyHexSerial = CreateRsaCredentials([0x12, 0x34]);

    private static readonly byte[] ValidRsaDocument =
        WithoutMock(() => Encoding.UTF8.GetBytes(Assemble(Body, Rsa.Pem, SignRsa(Rsa, Body, RSASignaturePadding.Pss))));

    /// <summary>
    /// A certificate is revoked by its serial in decimal, 0x-prefixed hex or plain hex (any case, leading zeros),
    /// as in the Python reference.
    /// </summary>
    [Theory]
    [InlineData("76959")]
    [InlineData("0x12c9f")]
    [InlineData("0X012C9F")]
    [InlineData("12c9f")]
    [InlineData("012C9F")]
    [InlineData("  76959  ")]
    public void VerifySignature_RejectsCertificateRevokedBySerial(string revokedEntry)
    {
        new ESignatureVerifier([revokedEntry]).VerifySignature(ValidRsaDocument)
            .Should().Be(new VerificationResult(false, "REVOKED_CERTIFICATE", "Certificate has been revoked."));
    }

    /// <summary>
    /// A digits-only entry matches a serial whose hex spelling it equals, as well as the serial it is in decimal.
    /// </summary>
    [Theory]
    [InlineData("4660")]
    [InlineData("1234")]
    [InlineData("0x1234")]
    public void VerifySignature_MatchesDigitsOnlyEntryAsDecimalOrHexSerial(string revokedEntry)
    {
        var document = WithoutMock(() =>
            Encoding.UTF8.GetBytes(Assemble(Body, DigitsOnlyHexSerial.Pem, SignRsa(DigitsOnlyHexSerial, Body, RSASignaturePadding.Pss))));

        new ESignatureVerifier([revokedEntry]).VerifySignature(document).Status.Should().Be("REVOKED_CERTIFICATE");
    }

    /// <summary>
    /// A certificate is revoked by its SHA-256 fingerprint, plain or separated by colons, hyphens or spaces, in any
    /// case; by its PEM, even with different line endings; and by its SHA-1 thumbprint.
    /// </summary>
    [Fact]
    public void VerifySignature_RejectsCertificateRevokedByFingerprintPemOrThumbprint()
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Rsa.Certificate.RawData));
        var pairs = Enumerable.Range(0, 32).Select(i => fingerprint.Substring(i * 2, 2)).ToArray();

        string[] entries =
        [
            fingerprint,
            fingerprint.ToLowerInvariant(),
            string.Join(':', pairs),
            string.Join('-', pairs).ToLowerInvariant(),
            string.Join(' ', pairs),
            Rsa.Pem,
            "\n" + Rsa.Pem.Replace("\n", "\r\n", StringComparison.Ordinal) + "\n",
            Rsa.Certificate.Thumbprint,
            string.Join(':', Enumerable.Range(0, 20).Select(i => Rsa.Certificate.Thumbprint.Substring(i * 2, 2))),
        ];

        foreach (var entry in entries)
        {
            new ESignatureVerifier([entry]).VerifySignature(ValidRsaDocument).Status
                .Should().Be("REVOKED_CERTIFICATE", "entry {0} names the signing certificate", entry);
        }
    }

    /// <summary>
    /// Entries that name another certificate, blank or null entries, and fragments of the subject or PEM do not
    /// revoke the signing certificate.
    /// </summary>
    [Fact]
    public void VerifySignature_AcceptsCertificateNotOnRevocationList()
    {
        var other = CreateRsaCredentials([0x01, 0x2C, 0xA0]);
        string?[] entries =
        [
            "76960",
            "0x12ca0",
            Convert.ToHexString(SHA256.HashData(other.Certificate.RawData)),
            other.Pem,
            other.Certificate.Thumbprint,
            "Cadence",
            "CN=Cadence Verifier Test",
            Rsa.Pem.Split('\n')[1][..8],
            "",
            "   ",
            null,
            "-----BEGIN CERTIFICATE-----\nnot base64\n-----END CERTIFICATE-----",
        ];

        new ESignatureVerifier(entries).VerifySignature(ValidRsaDocument)
            .Should().Be(new VerificationResult(true, "VALID"));
    }

    /// <summary>
    /// Revocation is checked after the signature, as in the Python reference: a tampered document signed with a
    /// revoked certificate reports tampering, and <see cref="ESignatureVerifier.VerifyPdf"/> throws for it.
    /// </summary>
    [Fact]
    public void VerifySignature_ReportsTamperingBeforeRevocation()
    {
        var tampered = WithoutMock(() =>
            Encoding.UTF8.GetBytes(Assemble(Body, Rsa.Pem, SignRsa(Rsa, Body, RSASignaturePadding.Pss))
                .Replace("approved", "rejected", StringComparison.Ordinal)));
        var verifier = new ESignatureVerifier(["76959"]);

        verifier.VerifySignature(tampered).Status.Should().Be("TAMPERED_INVALID_HASH");
        verifier.Invoking(v => v.VerifyPdf(tampered)).Should().Throw<TamperDetectedException>();
    }

    /// <summary>
    /// A signature made with legacy PKCS#1 v1.5 padding verifies cryptographically but is rejected, and
    /// <see cref="ESignatureVerifier.VerifyPdf"/> returns that result rather than throwing.
    /// </summary>
    [Fact]
    public void VerifySignature_RejectsLegacyPkcs1Padding()
    {
        var legacy = WithoutMock(() =>
            Encoding.UTF8.GetBytes(Assemble(Body, Rsa.Pem, SignRsa(Rsa, Body, RSASignaturePadding.Pkcs1))));
        var verifier = new ESignatureVerifier();

        var result = verifier.VerifySignature(legacy);

        result.IsValid.Should().BeFalse();
        result.Status.Should().Be("LEGACY_PADDING_REJECTED");
        result.FailureReason.Should().StartWith("LEGACY PADDING DETECTED");
        verifier.VerifyPdf(legacy).Should().Be(result);
    }

    /// <summary>
    /// A legacy-padding signature over different content is plain tampering, not legacy padding.
    /// </summary>
    [Fact]
    public void VerifySignature_TreatsLegacyPaddingOverOtherContentAsTampering()
    {
        var tampered = WithoutMock(() =>
            Encoding.UTF8.GetBytes(Assemble(Body + " Amended.", Rsa.Pem, SignRsa(Rsa, Body, RSASignaturePadding.Pkcs1))));

        new ESignatureVerifier().VerifySignature(tampered).Status.Should().Be("TAMPERED_INVALID_HASH");
    }

    /// <summary>
    /// A second certificate, signature, or XML-DSig certificate, signature value or signature block is rejected as
    /// an injection, even when the rest of the document verifies and even when it contains "mock".
    /// </summary>
    [Theory]
    [InlineData("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----")]
    [InlineData("-----BEGIN SIGNATURE-----\nAAAA\n-----END SIGNATURE-----")]
    [InlineData("<X509Certificate>AAAA</X509Certificate><X509Certificate>BBBB</X509Certificate>")]
    [InlineData("<SignatureValue>AAAA</SignatureValue><SignatureValue>BBBB</SignatureValue>")]
    [InlineData("<Signature></Signature><Signature></Signature>")]
    [InlineData("<Signature></Signature><Signature></Signature> mock")]
    public void VerifySignature_RejectsDuplicateBlocks(string injected)
    {
        var body = Body + "\n" + injected;
        var document = Encoding.UTF8.GetBytes(Assemble(body, Rsa.Pem, SignRsa(Rsa, body, RSASignaturePadding.Pss)));

        new ESignatureVerifier().VerifySignature(document).Should().Be(new VerificationResult(
            false,
            "DUPLICATE_BLOCKS_REJECTED",
            "Duplicate or injected certificate/signature blocks detected."));
    }

    /// <summary>
    /// A document that signs one XML-DSig block of each kind is not a duplicate and verifies.
    /// </summary>
    [Fact]
    public void VerifySignature_AcceptsSingleXmlDsigBlock()
    {
        const string body = "<Signature><SignatureValue>AAAA</SignatureValue><X509Certificate>BBBB</X509Certificate></Signature>";
        var document = WithoutMock(() =>
            Encoding.UTF8.GetBytes(Assemble(body, Rsa.Pem, SignRsa(Rsa, body, RSASignaturePadding.Pss))));

        new ESignatureVerifier().VerifySignature(document).Status.Should().Be("VALID");
    }

    /// <summary>
    /// ECDSA certificates verify a DER-encoded ECDSA SHA-256 signature, as in the Python reference, and report a
    /// changed document as tampering.
    /// </summary>
    [Fact]
    public void VerifySignature_VerifiesEcdsaSignatures()
    {
        var (key, pem) = CreateWithoutMock(() =>
        {
            var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=Cadence Verifier ECDSA Test", ecdsa, HashAlgorithmName.SHA256);
            using var certificate = request.CreateSelfSigned(FakeClock.DefaultStart, FakeClock.DefaultStart.AddYears(1));
            return (ecdsa, certificate.ExportCertificatePem());
        });
        using var disposeKey = key;
        byte[] Sign(DSASignatureFormat format) =>
            key.SignData(Encoding.UTF8.GetBytes(Body + pem), HashAlgorithmName.SHA256, format);

        var valid = WithoutMock(() => Encoding.UTF8.GetBytes(Assemble(Body, pem, Sign(DSASignatureFormat.Rfc3279DerSequence))));
        var tampered = WithoutMock(() =>
            Encoding.UTF8.GetBytes(Assemble(Body + " Amended.", pem, Sign(DSASignatureFormat.Rfc3279DerSequence))));
        var wrongFormat = WithoutMock(() =>
            Encoding.UTF8.GetBytes(Assemble(Body, pem, Sign(DSASignatureFormat.IeeeP1363FixedFieldConcatenation))));
        var verifier = new ESignatureVerifier();

        verifier.VerifySignature(valid).Should().Be(new VerificationResult(true, "VALID"));
        verifier.VerifySignature(tampered).Status.Should().Be("TAMPERED_INVALID_HASH");
        verifier.VerifySignature(wrongFormat).Status.Should().Be("TAMPERED_INVALID_HASH");
    }

    /// <summary>
    /// A certificate whose key is neither RSA nor ECDSA is reported as an unsupported key type.
    /// </summary>
    [Fact]
    public void VerifySignature_ReportsUnsupportedKeyType()
    {
        // A DSA public key in a certificate issued (signed) by the RSA test key.
        var document = WithoutMock(() =>
        {
            using var key = DSA.Create(2048);
            var request = new CertificateRequest(
                new X500DistinguishedName("CN=Cadence Verifier DSA Test"),
                new PublicKey(key),
                HashAlgorithmName.SHA256);
            using var certificate = request.Create(
                Rsa.Certificate.SubjectName,
                X509SignatureGenerator.CreateForRSA(Rsa.Key, RSASignaturePadding.Pkcs1),
                FakeClock.DefaultStart,
                FakeClock.DefaultStart.AddYears(1),
                [0x05]);
            return Encoding.UTF8.GetBytes(Assemble(Body, certificate.ExportCertificatePem(), [0x30, 0x00]));
        });

        new ESignatureVerifier().VerifySignature(document).Status.Should().Be("UNSUPPORTED_KEY_TYPE");
    }

    /// <summary>
    /// <see cref="ESignatureVerifier.VerifyPdf"/> returns a valid result, and throws
    /// <see cref="TamperDetectedException"/> carrying the tamper status when the bytes changed after signing or the
    /// signature markers are missing.
    /// </summary>
    [Fact]
    public void VerifyPdf_ReturnsValidResult_OrThrowsTamperDetected()
    {
        var pdf = WithoutMock(() =>
            new Pkcs7Signer(Rsa.Certificate, Rsa.Key).SignPdf("%PDF-1.7\nSynthetic visit report\n%%EOF"u8.ToArray()));
        var verifier = new ESignatureVerifier();

        verifier.VerifyPdf(pdf).Should().Be(new VerificationResult(true, "VALID"));

        var altered = (byte[])pdf.Clone();
        altered[10] ^= 0x01;
        var tamper = verifier.Invoking(v => v.VerifyPdf(altered)).Should().Throw<TamperDetectedException>().Which;
        tamper.IsValid.Should().BeFalse();
        tamper.Status.Should().Be("TAMPERED_INVALID_HASH");
        tamper.Message.Should().StartWith("TAMPER DETECTED: TAMPER DETECTED: Cryptographic signature mismatch");

        verifier.Invoking(v => v.VerifyPdf("%PDF-1.7 unsigned"u8.ToArray()))
            .Should().Throw<TamperDetectedException>()
            .WithMessage("TAMPER DETECTED: TAMPER DETECTED: Missing certificate or signature markers.");
    }

    /// <summary>
    /// <see cref="ESignatureVerifier.VerifyPdf"/> returns, rather than throws, failures other than tampering.
    /// </summary>
    [Fact]
    public void VerifyPdf_ReturnsNonTamperFailures()
    {
        var verifier = new ESignatureVerifier(["76959"]);

        verifier.VerifyPdf(ValidRsaDocument).Status.Should().Be("REVOKED_CERTIFICATE");
        verifier.VerifyPdf("%PDF-1.7 mock signature"u8.ToArray()).Status.Should().Be("MOCK_SIGNATURE_DETECTED");
    }

    private static string Assemble(string body, string certPem, byte[] signature) =>
        $"{body}\n{certPem.Trim()}\n-----BEGIN SIGNATURE-----\n{Convert.ToBase64String(signature)}\n-----END SIGNATURE-----\n";

    private static byte[] SignRsa(Credentials credentials, string body, RSASignaturePadding padding) =>
        credentials.Key.SignData(Encoding.UTF8.GetBytes(body + credentials.Pem.Trim()), HashAlgorithmName.SHA256, padding);

    private static byte[] WithoutMock(Func<byte[]> create)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var bytes = create();
            if (!Encoding.UTF8.GetString(bytes).Contains("mock", StringComparison.OrdinalIgnoreCase))
            {
                return bytes;
            }
        }

        throw new InvalidOperationException("Could not produce a signed fixture without the word \"mock\".");
    }

    // Creates a key and its certificate PEM, again with a new key while the PEM contains "mock".
    private static (TKey Key, string Pem) CreateWithoutMock<TKey>(Func<(TKey Key, string Pem)> create)
        where TKey : IDisposable
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var created = create();
            if (!created.Pem.Contains("mock", StringComparison.OrdinalIgnoreCase))
            {
                return created;
            }

            created.Key.Dispose();
        }

        throw new InvalidOperationException("Could not produce a certificate without the word \"mock\".");
    }

    private static Credentials CreateRsaCredentials(byte[] serial)
    {
        var (key, pem) = CreateWithoutMock(() =>
        {
            var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Cadence Verifier Test, O=Cadence Clinical Software",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
            using var certificate = request.Create(
                request.SubjectName,
                X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pss),
                FakeClock.DefaultStart,
                FakeClock.DefaultStart.AddYears(1),
                serial);
            return (rsa, certificate.ExportCertificatePem());
        });

        return new Credentials(X509Certificate2.CreateFromPem(pem), key, pem);
    }

    private sealed record Credentials(X509Certificate2 Certificate, RSA Key, string Pem);
}
