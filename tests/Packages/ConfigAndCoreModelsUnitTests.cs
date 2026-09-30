using System.Collections.Generic;

using Cadence.CoreModels;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class ConfigAndCoreModelsUnitTests
{
    [Fact]
    public void ProtocolRAG_FormatCitationMarker_ProducesCanonicalFormat()
    {
        var marker1 = ProtocolRAGEvaluator.FormatCitationMarker("2.1", "7.3", 42);
        marker1.Should().Be("[Protocol v2.1, Section 7.3, Page 42]");

        var marker2 = ProtocolRAGEvaluator.FormatCitationMarker("v3.0", null, 15);
        marker2.Should().Be("[Protocol v3.0, Page 15]");
    }

    [Fact]
    public void ProtocolRAG_FormatGroundedRagPrompt_FormatsPromptWithExcerpts()
    {
        var chunk = new ProtocolRAGContextChunk(
            ChunkId: "chk-1",
            ProtocolVersion: "v2.1",
            SectionNumber: "7.3",
            SectionTitle: "SAE Reporting",
            PageNumber: 42,
            ChunkText: "Serious adverse events must be reported within 24 hours of awareness."
        );

        var prompt = ProtocolRAGEvaluator.FormatGroundedRagPrompt("What is the SAE reporting window?", new[] { chunk });

        prompt.Should().Contain("--- PROTOCOL EXCERPTS ---");
        prompt.Should().Contain("[EXCERPT 1: Protocol v2.1, Section 7.3 (SAE Reporting), Page 42]");
        prompt.Should().Contain("Serious adverse events must be reported within 24 hours of awareness.");
        prompt.Should().Contain("CLINICAL INQUIRY:\nWhat is the SAE reporting window?");
    }

    [Fact]
    public void ProtocolRAG_EvaluateFaithfulness_ValidatesGroundedAnswer()
    {
        var chunk = new ProtocolRAGContextChunk(
            ChunkId: "chk-1",
            ProtocolVersion: "v2.1",
            SectionNumber: "7.3",
            SectionTitle: "SAE Reporting",
            PageNumber: 42,
            ChunkText: "Serious adverse events must be reported within 24 hours of site awareness.",
            IsApproved: true
        );

        var query = "How soon must serious adverse events be reported?";
        var answer = "Serious adverse events must be reported within 24 hours of site awareness [Protocol v2.1, Section 7.3, Page 42].";

        var (score, citations, isGrounded) = ProtocolRAGEvaluator.EvaluateFaithfulness(query, answer, new[] { chunk });

        score.Should().BeGreaterThanOrEqualTo(0.85);
        isGrounded.Should().BeTrue();
        citations.Should().ContainSingle();
        citations[0].IsVerified.Should().BeTrue();
        citations[0].IsApproved.Should().BeTrue();
        citations[0].CitationMarker.Should().Be("[Protocol v2.1, Section 7.3, Page 42]");
    }

    [Fact]
    public void ProtocolRAG_EvaluateFaithfulness_FailsClosedForInsufficientInformation()
    {
        var chunk = new ProtocolRAGContextChunk(
            ChunkId: "chk-1",
            ProtocolVersion: "v2.1",
            PageNumber: 10,
            ChunkText: "Exclusion criteria: Active hepatic disease."
        );

        var query = "What is the pediatric dosage?";
        var answer = "The provided study protocol excerpts do not contain sufficient information to answer this inquiry.";

        var (score, citations, isGrounded) = ProtocolRAGEvaluator.EvaluateFaithfulness(query, answer, new[] { chunk });

        score.Should().Be(0.0);
        isGrounded.Should().BeFalse();
        citations.Should().BeEmpty();
    }

    [Fact]
    public void ProtocolRAG_EvaluateFaithfulness_DetectsHallucinatedCitations()
    {
        var chunk = new ProtocolRAGContextChunk(
            ChunkId: "chk-1",
            ProtocolVersion: "v2.1",
            PageNumber: 10,
            ChunkText: "Adult dosage is 50mg daily."
        );

        var query = "What is the adult dosage?";
        var answer = "Adult dosage is 50mg daily [Protocol v2.1, Section 5.1, Page 99].";

        var (score, citations, isGrounded) = ProtocolRAGEvaluator.EvaluateFaithfulness(query, answer, new[] { chunk });

        score.Should().BeLessThan(0.85);
        isGrounded.Should().BeFalse();
        citations.Should().ContainSingle();
        citations[0].IsVerified.Should().BeFalse();
    }
}
