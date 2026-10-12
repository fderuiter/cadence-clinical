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
}
