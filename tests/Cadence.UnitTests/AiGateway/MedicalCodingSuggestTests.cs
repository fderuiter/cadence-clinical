using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Apps.AiGateway.Services;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.AiGateway;

public sealed class MedicalCodingSuggestTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void VectorEmbeddingService_Generates384DimensionalNormalizedVector()
    {
        var service = new VectorEmbeddingService();
        float[] vector = service.GenerateEmbedding("Headache");

        vector.Should().NotBeNull();
        vector.Length.Should().Be(384);

        double normSq = 0.0;
        foreach (float val in vector)
        {
            normSq += val * val;
        }

        Math.Sqrt(normSq).Should().BeApproximately(1.0, 1e-3);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task MedicalCodingSuggestService_ReturnsExactAndSemanticMatches()
    {
        var embedder = new VectorEmbeddingService();
        var cache = new VectorCacheService();
        var suggestService = new MedicalCodingSuggestService(embedder, cache);

        var request = new SuggestMedicalCodingRequest(Query: "Headache", Dictionary: "MedDRA", Limit: 5);
        var response = await suggestService.GetSuggestionsAsync(request, TestContext.Current.CancellationToken);

        response.Should().NotBeNull();
        response.Query.Should().Be("Headache");
        response.Suggestions.Should().NotBeEmpty();

        var topMatch = response.Suggestions[0];
        topMatch.Code.Should().Be("10019211");
        topMatch.Confidence.Should().Be(1.0);
        topMatch.MatchType.Should().Be("EXACT_SYNONYM");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task MedicalCodingSuggestService_FailsGracefullyWhenCacheOffline()
    {
        var embedder = new VectorEmbeddingService();
        var cache = new VectorCacheService();
        cache.SetHealthy(false);

        var suggestService = new MedicalCodingSuggestService(embedder, cache);
        var request = new SuggestMedicalCodingRequest(Query: "Migraine", Dictionary: "MedDRA", Limit: 5);

        var response = await suggestService.GetSuggestionsAsync(request, TestContext.Current.CancellationToken);

        response.Should().NotBeNull();
        response.Suggestions.Should().NotBeEmpty();
        response.Suggestions[0].MatchType.Should().Be("EXACT_SYNONYM");
    }
}
