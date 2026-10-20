using System;
using System.Linq;
using System.Text.Json.Nodes;

using Cadence.Kernel.Trust;
using Cadence.Modules.Intelligence.Evaluation.Sets;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Intelligence.Evaluation;

/// <summary>
/// Evaluation sets are versioned, hashed files that are refused whole when they break a rule (ADR-2634 §4.1).
/// </summary>
public sealed class EvaluationSetLoaderTests
{
    /// <summary>A valid set loads with its identity and the kernel content hash of its canonical JSON.</summary>
    [Fact]
    public void ValidSetLoadsWithItsContentHash()
    {
        var result = EvaluationSetLoader.Load(EvaluationFixtures.SetJson);

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Errors));
        result.Set!.Cases.Should().HaveCount(3);
        result.Ref.Should().Be(new EvaluationSetRef("query-draft-sample", 1, ContentHash.Compute(EvaluationFixtures.SetJson)));
    }

    /// <summary>Reformatting the file does not change its hash; changing a value does.</summary>
    [Fact]
    public void HashFollowsContentNotFormatting()
    {
        var original = EvaluationSetLoader.Load(EvaluationFixtures.SetJson).Ref!.ContentHash;
        var node = JsonNode.Parse(EvaluationFixtures.SetJson)!;
        var compact = node.ToJsonString();
        node["description"] = "Changed.";

        EvaluationSetLoader.Load(compact).Ref!.ContentHash.Should().Be(original);
        EvaluationSetLoader.Load(node.ToJsonString()).Ref!.ContentHash.Should().NotBe(original);
    }

    /// <summary>A set that is not exactly the pinned version is refused.</summary>
    [Fact]
    public void PinnedVersionMustMatch()
    {
        var pinned = new EvaluationSetRef("query-draft-sample", 1, "sha256:" + new string('0', 64));

        var result = EvaluationSetLoader.LoadPinned(EvaluationFixtures.SetJson, pinned);

        result.Succeeded.Should().BeFalse();
        result.Errors.Single().Should().Contain("pins");
    }

    /// <summary>Each broken rule is reported, and the set is not returned.</summary>
    [Theory]
    [InlineData("cases.0.provenance.deidentification", null, "without a de-identification method")]
    [InlineData("cases.0.provenance.reviewedBy", null, "without a reviewer")]
    [InlineData("cases.1.category", null, "adversarial without a category")]
    [InlineData("cases.1.caseId", "sbp-out-of-range", "appears 2 times")]
    [InlineData("setId", "Query Draft", "kebab-case")]
    public void BrokenRulesAreRefused(string path, string? value, string expected)
    {
        var json = Set(path, value is null ? null : JsonValue.Create(value));

        var result = EvaluationSetLoader.Load(json);

        result.Succeeded.Should().BeFalse();
        result.Set.Should().BeNull();
        result.Errors.Should().Contain(e => e.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>A prompt injection case needs a canary in its forbidden list, or nothing proves the agent ignored it.</summary>
    [Fact]
    public void InjectionCaseNeedsACanary()
    {
        var result = EvaluationSetLoader.Load(Set("cases.1.expectation.forbidden", new JsonArray()));

        result.Errors.Should().Contain(e => e.Contains("without a forbidden canary", StringComparison.Ordinal));
    }

    /// <summary>A required citation must name a node the case actually gives the agent.</summary>
    [Fact]
    public void RequiredCitationMustBeInContext()
    {
        var result = EvaluationSetLoader.Load(Set("cases.0.expectation.requiredCitations", new JsonArray("observation:elsewhere")));

        result.Errors.Should().Contain(e => e.Contains("not in its context", StringComparison.Ordinal));
    }

    /// <summary>A misspelled property in a reviewed file fails loudly instead of being ignored.</summary>
    [Fact]
    public void UnknownPropertiesAreRefused()
    {
        var result = EvaluationSetLoader.Load(Set("cases.0.expectation.requiredCitation", new JsonArray()));

        result.Succeeded.Should().BeFalse();
        result.Errors.Single().Should().Contain("not valid JSON for the evaluation set schema");
    }

    private static string Set(string path, JsonNode? value)
    {
        var root = JsonNode.Parse(EvaluationFixtures.SetJson)!;
        var parts = path.Split('.');
        var parent = parts[..^1].Aggregate(root, (node, part) => int.TryParse(part, out var i) ? node[i]! : node[part]!);
        var last = parts[^1];
        if (value is null)
        {
            parent.AsObject().Remove(last);
        }
        else
        {
            parent[last] = value;
        }

        return root.ToJsonString();
    }
}
