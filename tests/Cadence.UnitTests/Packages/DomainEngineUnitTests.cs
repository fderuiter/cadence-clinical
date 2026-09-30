using System;
using System.Collections.Generic;

using Cadence.Core.Domain.Randomization;
using Cadence.Core.Domain.Rules;
using Cadence.Core.Domain.Supply;
using Cadence.Core.Domain.Toxicity;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class DomainEngineUnitTests
{
    [Fact]
    public void BoinEngine_CalculateBoinBoundaries_ReturnsExpectedBoundaries()
    {
        var (lambdaE, lambdaD) = BoinEngine.CalculateBoinBoundaries(0.25);
        lambdaE.Should().BeInRange(0.19, 0.21);
        lambdaD.Should().BeInRange(0.29, 0.31);
    }

    [Fact]
    public void BoinEngine_EvaluateDoseLevel_EscalatesWhenLowDltRate()
    {
        var config = new BOINConfig(TargetDltRate: 0.25);
        var doseLevels = new List<DoseLevelDefinition>
        {
            new(0, 10.0, "mg"),
            new(1, 20.0, "mg"),
            new(2, 40.0, "mg")
        };
        var currentState = new DoseCohortState(DoseIndex: 0, DoseAmount: 10.0, DoseUnit: "mg", EvaluatedSubjects: 3, ObservedDlts: 0);

        var rec = BoinEngine.EvaluateDoseLevel(config, doseLevels, currentState);
        rec.Action.Should().Be(DoseEscalationAction.ESCALATE);
        rec.RecommendedDoseIndex.Should().Be(1);
        rec.RecommendedDoseAmount.Should().Be(20.0);
    }

    [Fact]
    public void BoinEngine_EvaluateDoseLevel_StopsTrialWhenExcessiveToxicityAtLowestDose()
    {
        var config = new BOINConfig(TargetDltRate: 0.25, StoppingCutoff: 0.95);
        var doseLevels = new List<DoseLevelDefinition>
        {
            new(0, 10.0, "mg"),
            new(1, 20.0, "mg")
        };
        var currentState = new DoseCohortState(DoseIndex: 0, DoseAmount: 10.0, DoseUnit: "mg", EvaluatedSubjects: 3, ObservedDlts: 3);

        var rec = BoinEngine.EvaluateDoseLevel(config, doseLevels, currentState);
        rec.Action.Should().Be(DoseEscalationAction.STOP_TRIAL);
        rec.IsTrialStopped.Should().BeTrue();
    }

    [Fact]
    public void BlockRandomizationEngine_GeneratesCanonicalStratumKey()
    {
        var factors = new Dictionary<string, object>
        {
            { "GENDER", "F" },
            { "AGE_GROUP", "18-40" }
        };
        var active = new List<string> { "GENDER", "AGE_GROUP" };

        string key = BlockRandomizationEngine.GenerateCanonicalStratumKey(factors, active);
        key.Should().Be("AGE_GROUP=18-40|GENDER=F");
    }

    [Fact]
    public void BlockRandomizationEngine_Allocate_AssignsArmWithChecksum()
    {
        var config = new StratifiedBlockConfig(
            ArmsRatios: new Dictionary<string, int> { { "ARM_A", 1 }, { "ARM_B", 1 } },
            StratificationFactors: new List<string> { "AGE" },
            BlockSizes: new List<int> { 2, 4 },
            Seed: 42
        );

        var request = new SubjectAllocationRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            SiteId: "SITE-1",
            StratificationFactors: new Dictionary<string, object> { { "AGE", "ADULT" } }
        );

        var result = BlockRandomizationEngine.Allocate(config, request);
        result.AssignedArm.Should().Match(arm => arm == "ARM_A" || arm == "ARM_B");
        result.StratumKey.Should().Be("AGE=ADULT");
        result.DeterministicChecksum.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void MinimizationEngine_Allocate_CalculatesImbalanceAndAllocates()
    {
        var config = new MinimizationConfig(
            ArmsRatios: new Dictionary<string, int> { { "ARM_A", 1 }, { "ARM_B", 1 } },
            StratificationFactors: new List<string> { "CENTER", "STAGE" },
            DistanceMetric: ImbalanceDistanceMetric.RANGE,
            PPreferred: 0.8,
            Seed: 101
        );

        var request = new SubjectAllocationRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-202",
            SiteId: "SITE-1",
            StratificationFactors: new Dictionary<string, object> { { "CENTER", "NY" }, { "STAGE", "III" } }
        );

        var result = MinimizationEngine.Allocate(config, request);
        result.AssignedArm.Should().Match(a => a == "ARM_A" || a == "ARM_B");
        result.ImbalanceScore.Should().NotBeNull();
        result.DeterministicChecksum.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DTPLogisticsEngine_CalculateMeanKineticTemperature_ComputesHaynesMkt()
    {
        var readings = new List<TemperatureReading>
        {
            new(4.0), new(5.0), new(6.0), new(5.5), new(4.5)
        };

        double mkt = DTPLogisticsEngine.CalculateMeanKineticTemperature(readings);
        mkt.Should().BeInRange(4.8, 5.3);
    }

    [Fact]
    public void DTPLogisticsEngine_EvaluateDtpDispatch_GeneratesTamperSealWhenEligible()
    {
        var readings = new List<TemperatureReading> { new(5.0), new(5.2) };
        var auth = DTPLogisticsEngine.EvaluateDtpDispatch(
            studyId: "STUDY-01", siteId: "SITE-01", subjectId: "SUBJ-01",
            kitNumber: "KIT-999", lotNumber: "LOT-100", daysUntilExpiry: 60,
            authorizedAt: FakeClock.DefaultStart,
            temperatureReadings: readings
        );

        auth.IsEligibleForDispatch.Should().BeTrue();
        auth.TamperSealId.Should().StartWith("SEAL-");
        auth.AuthorizedTimestamp.Should().Be("2026-01-01T00:00:00.0000000Z");
    }

    [Fact]
    public void LabToxicityGradingEngine_EvaluatesGrade3Alt_AndGeneratesAeDraft()
    {
        var engine = new LabToxicityGradingEngine();
        var result = engine.EvaluateObservation(
            testCode: "ALT",
            value: 300.0,
            unit: "U/L",
            evaluatedAt: FakeClock.DefaultStart,
            refHigh: 50.0
        );

        result.Grade.Should().Be(CTCAEGrade.GRADE_3);
        result.IsCriticalAeTrigger.Should().BeTrue();
        result.AeDraft.Should().NotBeNull();
        result.AeDraft!.Severity.Should().Be("SEVERE");
        result.EvalTimestamp.Should().Be("2026-01-01T00:00:00.0000000Z");
        result.AeDraft.DraftTimestamp.Should().Be(result.EvalTimestamp);
    }

    [Fact]
    public void LabToxicityGradingEngine_EvaluateHysLaw_DetectsCriticalHysLawTriad()
    {
        var eval = LabToxicityGradingEngine.EvaluateHysLaw(
            alt: 200, ast: 180, tbil: 4.5, alkp: 100,
            altUln: 50, astUln: 50, tbilUln: 1.2, alkpUln: 120,
            evaluatedAt: FakeClock.DefaultStart
        );

        eval.IsHysLawMet.Should().BeTrue();
        eval.AlertLevel.Should().Be("HYS_LAW_CRITICAL");
        eval.EvalTimestamp.Should().Be("2026-01-01T00:00:00.0000000Z");
    }

    [Fact]
    public void RuleEvaluator_EvaluatesAstAndJsonLogic()
    {
        var evaluator = new RuleEvaluator(FakeClock.DefaultStart.UtcDateTime);

        var astNode = new ExpressionNode
        {
            Type = "logical",
            Operator = "and",
            Operands = new List<ExpressionNode>
            {
                new() { Type = "comparison", Operator = ">=", Operands = new List<ExpressionNode> { new() { Type = "variable", VariableName = "age" }, new() { Type = "constant", Value = 18 } } },
                new() { Type = "comparison", Operator = "==", Operands = new List<ExpressionNode> { new() { Type = "variable", VariableName = "consent" }, new() { Type = "constant", Value = true } } }
            }
        };

        var context = new Dictionary<string, object?>
        {
            { "age", 25 },
            { "consent", true }
        };

        var result = evaluator.Evaluate(astNode, context);
        result.Should().Be(true);
    }
}
