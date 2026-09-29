using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Cadence.Compliance;
using Cadence.CoreModels;
using Cadence.Deid;
using Cadence.Testing;

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
    /// Validates that ProtocolRagService.EvaluateFaithfulness produces outputs matching ProtocolRAGEvaluator.
    /// @req:PRD-TCK-005
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-TCK-005")]
    [GxPRequirement("PRD-SYS-051")]
    public void ProtocolRagService_EvaluateFaithfulness_MatchesProtocolRAGEvaluator()
    {
        var chunk = new ProtocolRAGContextChunk(
            ChunkId: "chk-100",
            ProtocolVersion: "v2.1",
            SectionNumber: "7.3",
            SectionTitle: "SAE Reporting",
            PageNumber: 42,
            ChunkText: "Serious adverse events must be reported within 24 hours of site awareness.",
            IsApproved: true
        );

        var query = "How soon must serious adverse events be reported?";
        var answer = "Serious adverse events must be reported within 24 hours of site awareness [Protocol v2.1, Section 7.3, Page 42].";

        var ragService = new ProtocolRagService();
        var (scoreService, citationsService, isGroundedService) = ragService.EvaluateFaithfulness(query, answer, new[] { chunk });
        var (scoreEvaluator, citationsEvaluator, isGroundedEvaluator) = ProtocolRAGEvaluator.EvaluateFaithfulness(query, answer, new[] { chunk });

        scoreService.Should().Be(scoreEvaluator);
        isGroundedService.Should().Be(isGroundedEvaluator);
        citationsService.Should().BeEquivalentTo(citationsEvaluator);

        var formattedMarker = ragService.FormatCitationMarker("2.1", "7.3", 42);
        formattedMarker.Should().Be("[Protocol v2.1, Section 7.3, Page 42]");

        var prompt = ragService.FormatGroundedRagPrompt(query, new[] { chunk });
        prompt.Should().Contain("--- PROTOCOL EXCERPTS ---");
    }

    /// <summary>
    /// Validates that PhiRedactorService redacts custom terms and auto-detected PHI like emails and phone numbers.
    /// </summary>
    [Fact]
    public void PhiRedactorService_RedactsConfiguredTermsAndAutoDetectedPhi()
    {
        var service = new PhiRedactorService();
        var rawText = "Patient Jane Doe (email jane.doe@example.com, phone 555-123-4567) visited Site 101.";
        var customTerms = new[] { "Jane Doe", "Site 101" };

        var redactedText = service.RedactText(rawText, customTerms);

        redactedText.Should().Contain("[REDACTED]");
        redactedText.Should().Contain("[EMAIL]");
        redactedText.Should().Contain("[TELEPHONE_FAX]");
        redactedText.Should().NotContain("jane.doe@example.com");
        redactedText.Should().NotContain("555-123-4567");
        redactedText.Should().NotContain("Jane Doe");

        var rawBytes = Encoding.UTF8.GetBytes(rawText);
        var redactedBytes = service.RedactContent(rawBytes, customTerms);
        var redactedFromBytes = Encoding.UTF8.GetString(redactedBytes);

        redactedFromBytes.Should().Be(redactedText);
    }
}
