using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

public class AmendmentRegressionEngineTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 1);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "AmendmentRegressionEngine_CompareAsync_NullInputs_ThrowsArgumentNullException")]
    [Trait("Category", "Unit")]
    public async Task CompareAsync_NullInputs_ThrowsArgumentNullException()
    {
        var engine = new AmendmentRegressionEngine(new DotNetRuleEvaluator(), new FakeClock());
        var pack = CreateLoadedPack("P1", 1, []);

        Func<Task> act1 = async () => await engine.CompareAsync(null!, pack, ValidationStage.Collection, AsOf, [], Ct);
        Func<Task> act2 = async () => await engine.CompareAsync(pack, null!, ValidationStage.Collection, AsOf, [], Ct);
        Func<Task> act3 = async () => await engine.CompareAsync(pack, pack, ValidationStage.Collection, AsOf, null!, Ct);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
        await act3.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact(DisplayName = "AmendmentRegressionEngine_CompareAsync_DetectsAddedRemovedAndModifiedRules")]
    [Trait("Category", "Unit")]
    public async Task CompareAsync_DetectsAddedRemovedAndModifiedRules()
    {
        var clock = new FakeClock();
        var engine = new AmendmentRegressionEngine(new DotNetRuleEvaluator(), clock);

        var r1V1 = new Rule(
            "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
            new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
            ValidationSeverity.Error,
            new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(140m))),
            "R1 v1", null, EffectivePeriod.Always);

        var r1V2 = r1V1 with
        {
            Version = 2,
            Condition = new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(130m)))
        };

        var r2 = new Rule(
            "R2", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
            new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
            ValidationSeverity.Warning,
            new ComparisonExpression(ComparisonOperator.Less, new VariableExpression("DIABP"), new LiteralExpression(RuleValue.FromNumber(60m))),
            "R2", null, EffectivePeriod.Always);

        var r3 = new Rule(
            "R3", 1, RuleAuthority.Sponsor, new SourceCitation("S3", null, null),
            new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
            ValidationSeverity.Error,
            new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("PULSE"), new LiteralExpression(RuleValue.FromNumber(100m))),
            "R3", null, EffectivePeriod.Always);

        var baselinePack = CreateLoadedPack("PACK-01", 1, [r1V1, r2]);
        var targetPack = CreateLoadedPack("PACK-01", 2, [r1V2, r3]);

        var records = new List<RuleRecord>
        {
            new("VS", "rec-1", new Dictionary<string, RuleValue>
            {
                ["SYSBP"] = RuleValue.FromNumber(135m),
                ["DIABP"] = RuleValue.FromNumber(55m),
                ["PULSE"] = RuleValue.FromNumber(105m)
            })
        };

        var report = await engine.CompareAsync(baselinePack, targetPack, ValidationStage.Collection, AsOf, records, Ct);

        report.Should().NotBeNull();
        report.BaselineVersion.Should().Be("v1");
        report.TargetVersion.Should().Be("v2");

        report.RuleDiffs.AddedRules.Should().ContainSingle().Which.Id.Should().Be("R3");
        report.RuleDiffs.RemovedRules.Should().ContainSingle().Which.Id.Should().Be("R2");
        report.RuleDiffs.ModifiedRules.Should().ContainSingle().Which.Id.Should().Be("R1");

        // R1 v1 (135 > 140 -> false) vs R1 v2 (135 > 130 -> true): R1 is a new violation
        // R3 (105 > 100 -> true): R3 is a new violation
        report.NewViolations.Should().HaveCount(2);

        // R2 (55 < 60 -> true in baseline, removed in target): R2 is a resolved finding
        report.ResolvedFindings.Should().ContainSingle().Which.RuleId.Should().Be("R2");
    }

    [Fact(DisplayName = "AmendmentRegressionEngine_CompareAsync_DetectsBrokenAstReferences")]
    [Trait("Category", "Unit")]
    public async Task CompareAsync_DetectsBrokenAstReferences()
    {
        var engine = new AmendmentRegressionEngine(new DotNetRuleEvaluator(), new FakeClock());

        var rule = new Rule(
            "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
            new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
            ValidationSeverity.Error,
            new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("UNKNOWN_VAR"), new LiteralExpression(RuleValue.FromText("VAL"))),
            "Broken ref rule", null, EffectivePeriod.Always);

        var pack = CreateLoadedPack("PACK-01", 1, [rule]);
        var records = new List<RuleRecord>
        {
            new("VS", "rec-1", new Dictionary<string, RuleValue> { ["SYSBP"] = RuleValue.FromNumber(120m) })
        };

        var report = await engine.CompareAsync(pack, pack, ValidationStage.Collection, AsOf, records, Ct);

        report.BrokenAstReferences.Should().ContainSingle()
            .Which.ReferencedVariable.Should().Be("UNKNOWN_VAR");
    }

    private static LoadedRulePack CreateLoadedPack(string key, int version, List<Rule> rules)
    {
        var pack = new RulePack(RuleAuthority.Sponsor, key, rules);
        return new LoadedRulePack(
            LibraryItemId.New(),
            key,
            version,
            "hash_123",
            GovernanceStatus.Approved,
            pack);
    }
}
