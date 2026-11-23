using System;
using System.Text;

using Cadence.Compliance;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class ComplianceUnitTests
{
    /// <summary>
    /// Validates that Pkcs7Signer generates valid cryptographic signatures verified by ESignatureVerifier.
    /// </summary>
    [Fact]
    public void Pkcs7Signer_GeneratesValidSignature_PassesESignatureVerifier()
    {
        var (cert, key) = Pkcs7Signer.GenerateSigningCredentials("investigator-01");
        var signer = new Pkcs7Signer(cert, key);

        var data = "Clinical document payload for 21 CFR Part 11 signing."u8.ToArray();
        var signedBytes = signer.SignDocument(data);

        var verifier = new ESignatureVerifier();
        var result = verifier.VerifySignature(signedBytes);

        result.IsValid.Should().BeTrue();
        result.Status.Should().Be("VALID");
    }

    /// <summary>
    /// Validates that Pkcs7Signer.ComputeDetachedSignature creates a non-empty PKCS7 CMS DER structure.
    /// </summary>
    [Fact]
    public void Pkcs7Signer_ComputeDetachedSignature_ReturnsValidPkcs7CmsDer()
    {
        var (cert, key) = Pkcs7Signer.GenerateSigningCredentials("investigator-01");
        var signer = new Pkcs7Signer(cert, key);

        var data = "Sample study document data"u8.ToArray();
        var detachedSignature = signer.ComputeDetachedSignature(data);

        detachedSignature.Should().NotBeNullOrEmpty();
        detachedSignature.Length.Should().BeGreaterThan(100);
    }

    /// <summary>
    /// Validates that GxPSigner.SignGxpMarkdown generates real cryptographic PKCS7 detached signatures.
    /// </summary>
    [Fact]
    public void GxPSigner_SignGxpMarkdown_GeneratesValidSignature_NotDummy()
    {
        var markdownContent = "# Qualification Test Document\n\nAll automated unit tests passed successfully.";
        var signedMarkdown = GxPSigner.SignGxpMarkdown(
            content: markdownContent,
            signerId: "qa-auditor",
            signingReason: "GxP System Release Sign-Off"
        );

        signedMarkdown.Should().NotContain("MIIC...DUMMY");
        signedMarkdown.Should().Contain("## Electronic Signature Block");
        signedMarkdown.Should().Contain("- **Signer Identity:** qa-auditor");
        signedMarkdown.Should().Contain("-----BEGIN CERTIFICATE-----");
        signedMarkdown.Should().Contain("-----BEGIN SIGNATURE-----");

        var verifier = new ESignatureVerifier();
        var result = verifier.VerifyMarkdown(signedMarkdown);

        result.IsValid.Should().BeTrue();
        result.Status.Should().Be("VALID");
    }

    /// <summary>
    /// Validates that ESignatureVerifier rejects markdown documents where body text was modified post-signature.
    /// </summary>
    [Fact]
    public void ESignatureVerifier_DetectsTamperedMarkdownBody()
    {
        var markdownContent = "Original Unmodified Clinical Document";
        var signedMarkdown = GxPSigner.SignGxpMarkdown(markdownContent);

        var tamperedMarkdown = signedMarkdown.Replace("Original Unmodified Clinical Document", "Tampered Modified Clinical Document");

        var verifier = new ESignatureVerifier();
        var result = verifier.VerifyMarkdown(tamperedMarkdown);

        result.IsValid.Should().BeFalse();
        result.Status.Should().Be("TAMPERED_INVALID_HASH");
    }

    /// <summary>
    /// Validates that a genuine signature verifies even when the signed text contains "mock", as a document can and
    /// as a base64 certificate or signature does by chance in about 1 in 700 signings.
    /// </summary>
    [Fact]
    public void ESignatureVerifier_AcceptsValidSignature_WhoseTextContainsMock()
    {
        var (cert, key) = Pkcs7Signer.GenerateSigningCredentials("investigator-01");
        var signed = new Pkcs7Signer(cert, key).SignDocument("Mock visit schedule reviewed with the site."u8.ToArray());
        var signedMarkdown = GxPSigner.SignGxpMarkdown("Hammock-shaped enrollment curve discussed.");

        var verifier = new ESignatureVerifier();

        verifier.VerifySignature(signed).Status.Should().Be("VALID");
        verifier.VerifyMarkdown(signedMarkdown).Status.Should().Be("VALID");
    }

    /// <summary>
    /// Validates that a placeholder signature is rejected and reported as a mock, whether it replaces the signature
    /// block or the document has no signature markers at all.
    /// </summary>
    [Fact]
    public void ESignatureVerifier_ReportsMockPlaceholderSignature()
    {
        var (cert, key) = Pkcs7Signer.GenerateSigningCredentials("investigator-01");
        var signed = Encoding.UTF8.GetString(new Pkcs7Signer(cert, key).SignDocument("Visit 2 vitals."u8.ToArray()));
        var sigStart = signed.IndexOf("-----BEGIN SIGNATURE-----", StringComparison.Ordinal) + "-----BEGIN SIGNATURE-----".Length;
        var sigEnd = signed.IndexOf("-----END SIGNATURE-----", StringComparison.Ordinal);
        var placeholder = signed[..sigStart] + "\nMOCK-SIGNATURE-PLACEHOLDER\n" + signed[sigEnd..];

        var verifier = new ESignatureVerifier();

        verifier.VerifySignature(Encoding.UTF8.GetBytes(placeholder)).Status.Should().Be("MOCK_SIGNATURE_DETECTED");
        verifier.VerifySignature("Visit 2 vitals.\nSignature: mock"u8.ToArray()).Status.Should().Be("MOCK_SIGNATURE_DETECTED");
        verifier.VerifySignature("Visit 2 vitals."u8.ToArray()).Status.Should().Be("TAMPERED_INVALID_HASH");
    }
}
