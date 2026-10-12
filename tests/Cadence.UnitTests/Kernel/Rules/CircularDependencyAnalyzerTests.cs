using System;
using System.Collections.Generic;

using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

public class CircularDependencyAnalyzerTests
{
    [Fact(DisplayName = "CircularDependencyAnalyzer_NullPacks_ThrowsArgumentNullException")]
    [Trait("Category", "Unit")]
    public void Analyze_NullPacks_ThrowsArgumentNullException()
    {
        Action act = () => CircularDependencyAnalyzer.Analyze(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CircularDependencyAnalyzer_EmptyPacks_ReturnsEmptyList")]
    [Trait("Category", "Unit")]
    public void Analyze_EmptyPacks_ReturnsEmptyList()
    {
        var result = CircularDependencyAnalyzer.Analyze([]);
        result.Should().BeEmpty();
    }

    [Fact(DisplayName = "CircularDependencyAnalyzer_NoCycles_ReturnsEmptyList")]
    [Trait("Category", "Unit")]
    public void Analyze_NoCycles_ReturnsEmptyList()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Linear Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(120m))),
                    "High SYSBP", null, EffectivePeriod.Always)
            ]);

        var result = CircularDependencyAnalyzer.Analyze([pack]);
        result.Should().BeEmpty();
    }

    [Fact(DisplayName = "CircularDependencyAnalyzer_DirectDatasetSelfReference_ReturnsDiagnostic")]
    [Trait("Category", "Unit")]
    public void Analyze_DirectDatasetSelfReference_ReturnsDiagnostic()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Self Ref Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("VS"), new LiteralExpression(RuleValue.FromText("X"))),
                    "Refers to dataset name in condition", null, EffectivePeriod.Always)
            ]);

        var result = CircularDependencyAnalyzer.Analyze([pack]);
        result.Should().ContainSingle().Which.Should().Contain("rule R1 reads variable 'VS' which matches its target dataset");
    }

    [Fact(DisplayName = "CircularDependencyAnalyzer_MultiRuleCycle_ReturnsPathDiagnostic")]
    [Trait("Category", "Unit")]
    public void Analyze_MultiRuleCycle_ReturnsPathDiagnostic()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Cyclic Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "DS_B"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("DS_A"), new LiteralExpression(RuleValue.FromNumber(1m))),
                    "R1 targets DS_B, reads DS_A", null, EffectivePeriod.Always),
                new Rule(
                    "R2", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "DS_A"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("DS_B"), new LiteralExpression(RuleValue.FromNumber(1m))),
                    "R2 targets DS_A, reads DS_B", null, EffectivePeriod.Always)
            ]);

        var result = CircularDependencyAnalyzer.Analyze([pack]);
        result.Should().Contain(c => c.Contains("Multi-rule circular logic loop detected"));
    }
}
