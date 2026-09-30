using System.Linq;

using Cadence.Kernel.Validation;
using Cadence.Rules.Benchmarks;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Packs;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

/// <summary>
/// Keeps the rules benchmark harness (spec §13) working: its pack is valid content, its dataset is reproducible, and a
/// run is deterministic, so a measured comparison with another engine compares like with like.
/// </summary>
public class RuleBenchmarkTests
{
    [Fact(DisplayName = "RuleBenchmark_SmallRun_IsReproducibleAndFindsViolations")]
    [Trait("Category", "Unit")]
    public void RuleBenchmarkSmallRunIsReproducibleAndFindsViolations()
    {
        var first = RuleBenchmark.Run(new DotNetRuleEvaluator(), 10_000, seed: 7);
        var second = RuleBenchmark.Run(new DotNetRuleEvaluator(), 10_000, seed: 7);
        var otherSeed = RuleBenchmark.Run(new DotNetRuleEvaluator(), 10_000, seed: 8);

        first.Records.Should().Be(10_000);
        first.Rules.Should().Be(12);
        first.Violations.Should().BeGreaterThan(0);
        second.ResultHash.Should().Be(first.ResultHash);
        second.Violations.Should().Be(first.Violations);
        otherSeed.ResultHash.Should().NotBe(first.ResultHash);
    }

    [Fact(DisplayName = "RuleBenchmark_Pack_IsValidRulePackContent_AndEveryRuleFiresExceptTheMissingVariableRule")]
    [Trait("Category", "Unit")]
    public void RuleBenchmarkPackIsValidRulePackContentAndEveryRuleFiresExceptTheMissingVariableRule()
    {
        var pack = RuleBenchmark.Pack();

        new RulePackContentValidator().Validate(RulePackJson.ToJson(pack)).Should().BeEmpty();
        var fired = new DotNetRuleEvaluator()
            .Evaluate(pack, new RuleEvaluationContext(ValidationStage.Collection, RuleBenchmark.AsOf), RuleBenchmark.Dataset(10_000, 7))
            .Select(v => v.RuleId).Distinct().Order().ToList();

        // B12 reads a variable no record has: an ordering comparison with null is false, so it never fires.
        fired.Should().Equal(pack.Rules.Select(r => r.Id).Where(id => id != "B12").Order());
    }
}
