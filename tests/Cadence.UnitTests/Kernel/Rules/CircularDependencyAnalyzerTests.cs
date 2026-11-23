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

    [Fact(DisplayName = "CircularDependencyAnalyzer_VariableMatchingDatasetWithoutTargetVariable_ReturnsEmptyList")]
    [Trait("Category", "Unit")]
    public void Analyze_VariableMatchingDatasetWithoutTargetVariable_ReturnsEmptyList()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Dataset Name Coincidence Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("VS"), new LiteralExpression(RuleValue.FromText("X"))),
                    "Reads variable named VS without targeting a derived variable", null, EffectivePeriod.Always),
                new Rule(
                    "R2", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "DS_B"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("DS_A"), new LiteralExpression(RuleValue.FromNumber(1m))),
                    "Reads DS_A on dataset DS_B without target variable", null, EffectivePeriod.Always),
                new Rule(
                    "R3", 1, RuleAuthority.Sponsor, new SourceCitation("S3", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "DS_A"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("DS_B"), new LiteralExpression(RuleValue.FromNumber(1m))),
                    "Reads DS_B on dataset DS_A without target variable", null, EffectivePeriod.Always),
            ]);

        var result = CircularDependencyAnalyzer.Analyze([pack]);
        result.Should().BeEmpty();
    }

    [Fact(DisplayName = "CircularDependencyAnalyzer_DirectTargetVariableSelfReference_ReturnsDiagnostic")]
    [Trait("Category", "Unit")]
    public void Analyze_DirectTargetVariableSelfReference_ReturnsDiagnostic()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Self Ref Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS", "SYSBP"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("VS.SYSBP"), new LiteralExpression(RuleValue.FromNumber(120m))),
                    "Refers to its own target variable in condition", null, EffectivePeriod.Always)
            ]);

        var result = CircularDependencyAnalyzer.Analyze([pack]);
        result.Should().ContainSingle().Which.Should().Contain("rule R1 reads variable 'VS.SYSBP' which matches its target variable");
    }

    [Fact(DisplayName = "CircularDependencyAnalyzer_MultiRuleCycle_ReturnsDeterministicPathDiagnostic")]
    [Trait("Category", "Unit")]
    public void Analyze_MultiRuleCycle_ReturnsDeterministicPathDiagnostic()
    {
        var r1 = new Rule(
            "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
            new RuleApplicability(ValidationStage.Collection, null, null, null, "VS", "MAP"),
            ValidationSeverity.Error,
            new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("VS.PULSE"), new LiteralExpression(RuleValue.FromNumber(1m))),
            "R1 targets VS.MAP, reads VS.PULSE", null, EffectivePeriod.Always);
        var r2 = new Rule(
            "R2", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
            new RuleApplicability(ValidationStage.Collection, null, null, null, "VS", "PULSE"),
            ValidationSeverity.Error,
            new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("MAP"), new LiteralExpression(RuleValue.FromNumber(1m))),
            "R2 targets VS.PULSE, reads MAP", null, EffectivePeriod.Always);

        var forward = CircularDependencyAnalyzer.Analyze([new RulePack(RuleAuthority.Sponsor, "Cyclic Pack Forward", [r1, r2])]);
        var reversed = CircularDependencyAnalyzer.Analyze([new RulePack(RuleAuthority.Sponsor, "Cyclic Pack Reversed", [r2, r1])]);

        forward.Should().ContainSingle().Which.Should().Be("Multi-rule circular logic loop detected: R1 -> R2 -> R1");
        reversed.Should().Equal(forward);
    }
}
