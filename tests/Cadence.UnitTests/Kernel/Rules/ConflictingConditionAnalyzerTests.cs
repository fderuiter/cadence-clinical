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

public class ConflictingConditionAnalyzerTests
{
    [Fact(DisplayName = "ConflictingConditionAnalyzer_NullPacks_ThrowsArgumentNullException")]
    [Trait("Category", "Unit")]
    public void Analyze_NullPacks_ThrowsArgumentNullException()
    {
        Action act = () => ConflictingConditionAnalyzer.Analyze(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "ConflictingConditionAnalyzer_NoConflicts_ReturnsEmptyList")]
    [Trait("Category", "Unit")]
    public void Analyze_NoConflicts_ReturnsEmptyList()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Valid Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("STAT"), new LiteralExpression(RuleValue.FromText("NOT DONE"))),
                    "R1 condition", null, EffectivePeriod.Always),
                new Rule(
                    "R2", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Warning,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("REAS"), new LiteralExpression(RuleValue.FromText("REFUSED"))),
                    "R2 condition", null, EffectivePeriod.Always)
            ]);

        var result = ConflictingConditionAnalyzer.Analyze([pack]);
        result.Should().BeEmpty();
    }

    [Fact(DisplayName = "ConflictingConditionAnalyzer_ContradictoryCondition_ReturnsDiagnostic")]
    [Trait("Category", "Unit")]
    public void Analyze_ContradictoryCondition_ReturnsDiagnostic()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Conflicting Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("STAT"), new LiteralExpression(RuleValue.FromText("NOT DONE"))),
                    "R1 requires STAT == NOT DONE", null, EffectivePeriod.Always),
                new Rule(
                    "R2", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Warning,
                    new ComparisonExpression(ComparisonOperator.NotEqual, new VariableExpression("STAT"), new LiteralExpression(RuleValue.FromText("NOT DONE"))),
                    "R2 requires STAT != NOT DONE", null, EffectivePeriod.Always)
            ]);

        var result = ConflictingConditionAnalyzer.Analyze([pack]);
        result.Should().ContainSingle().Which.Should().Contain("Mutually conflicting rule conditions detected on dataset 'VS'");
    }

    [Fact(DisplayName = "ConflictingConditionAnalyzer_DuplicateEqualChecksAndCompatibleBounds_ReturnEmptyList")]
    [Trait("Category", "Unit")]
    public void Analyze_DuplicateEqualChecksAndCompatibleBounds_ReturnEmptyList()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Compatible Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Warning,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("STAT"), new LiteralExpression(RuleValue.FromText("NOT DONE"))),
                    "Duplicate check warning", null, EffectivePeriod.Always),
                new Rule(
                    "R2", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("STAT"), new LiteralExpression(RuleValue.FromText("NOT DONE"))),
                    "Duplicate check error", null, EffectivePeriod.Always),
                new Rule(
                    "R3", 1, RuleAuthority.Sponsor, new SourceCitation("S3", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Less, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(90m))),
                    "SYSBP below 90 violates", null, EffectivePeriod.Always),
                new Rule(
                    "R4", 1, RuleAuthority.Sponsor, new SourceCitation("S4", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(180m))),
                    "SYSBP above 180 violates", null, EffectivePeriod.Always),
            ]);

        ConflictingConditionAnalyzer.Analyze([pack]).Should().BeEmpty();
    }

    [Fact(DisplayName = "ConflictingConditionAnalyzer_InequalityAndReversedIntervalContradictions_ReturnDiagnostics")]
    [Trait("Category", "Unit")]
    public void Analyze_InequalityAndReversedIntervalContradictions_ReturnDiagnostics()
    {
        var lessVsGreaterOrEqual = new RulePack(
            RuleAuthority.Sponsor,
            "Less vs GreaterOrEqual",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Less, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(100m))),
                    "Violates when SYSBP < 100", null, EffectivePeriod.Always),
                new Rule(
                    "R2", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.GreaterOrEqual, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(100m))),
                    "Violates when SYSBP >= 100", null, EffectivePeriod.Always),
            ]);

        ConflictingConditionAnalyzer.Analyze([lessVsGreaterOrEqual])
            .Should().ContainSingle()
            .Which.Should().Be("Mutually conflicting rule conditions detected on dataset 'VS': R1 (v1) vs R2 (v1).");

        var reversedLiteralLeft = new RulePack(
            RuleAuthority.Sponsor,
            "Reversed Comparison",
            [
                new Rule(
                    "R3", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Less, new LiteralExpression(RuleValue.FromNumber(100m)), new VariableExpression("SYSBP")),
                    "Violates when 100 < SYSBP", null, EffectivePeriod.Always),
                new Rule(
                    "R4", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.LessOrEqual, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(100m))),
                    "Violates when SYSBP <= 100", null, EffectivePeriod.Always),
            ]);

        ConflictingConditionAnalyzer.Analyze([reversedLiteralLeft])
            .Should().ContainSingle()
            .Which.Should().Be("Mutually conflicting rule conditions detected on dataset 'VS': R3 (v1) vs R4 (v1).");
    }

    [Fact(DisplayName = "ConflictingConditionAnalyzer_DisjointApplicabilityOrEffectivePeriods_ReturnEmptyList")]
    [Trait("Category", "Unit")]
    public void Analyze_DisjointApplicabilityOrEffectivePeriods_ReturnEmptyList()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Non-overlapping Scope Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, "SDTMIG", "3.4", null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Less, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(100m))),
                    "Collection VS check 2025", null, new EffectivePeriod(new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1))),
                new Rule(
                    "R2", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, "SDTMIG", "3.4", null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.GreaterOrEqual, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(100m))),
                    "Collection VS check 2026", null, new EffectivePeriod(new DateOnly(2026, 1, 1), null)),
                new Rule(
                    "R3", 1, RuleAuthority.Sponsor, new SourceCitation("S3", null, null),
                    new RuleApplicability(ValidationStage.Standardization, "SDTMIG", "3.4", null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.GreaterOrEqual, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(100m))),
                    "Different stage check", null, EffectivePeriod.Always),
                new Rule(
                    "R4", 1, RuleAuthority.Sponsor, new SourceCitation("S4", null, null),
                    new RuleApplicability(ValidationStage.Collection, "SDTMIG", "3.4", null, "LB"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.GreaterOrEqual, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(100m))),
                    "Different dataset check", null, EffectivePeriod.Always),
            ]);

        ConflictingConditionAnalyzer.Analyze([pack]).Should().BeEmpty();
    }
}
