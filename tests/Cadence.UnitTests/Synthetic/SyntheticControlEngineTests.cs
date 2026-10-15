using System;
using System.Collections.Generic;

using Cadence.Core.Synthetic;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Synthetic;

public class SyntheticControlEngineTests
{
    [Fact]
    public void MonteCarloCohortSampler_GeneratesRequestedCohortSize()
    {
        var cohort = MonteCarloCohortSampler.GenerateCohort(sampleSize: 100);

        cohort.Should().HaveCount(100);
        cohort[0].PatientId.Should().StartWith("SYNTH-PAT-");
        cohort[0].Age.Should().BeInRange(18.0, 90.0);
        cohort[0].Bmi.Should().BeInRange(15.0, 50.0);
        cohort[0].SystolicBp.Should().BeInRange(80.0, 200.0);
    }

    [Fact]
    public void ProtocolQualitySentinel_ValidatesEligibilityCriteria()
    {
        var validCriteria = new List<EligibilityCriterion>
        {
            new("CRIT-01", "Adults 18-75", "INCLUSION", "Age", "BETWEEN", "18", HighValue: "75"),
            new("CRIT-02", "Exclude Systolic BP > 180", "EXCLUSION", "SystolicBp", ">=", "180")
        };

        var result = ProtocolQualitySentinel.ValidateCriteria(validCriteria);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ProtocolQualitySentinel_DetectsInvalidOperatorAndDuplicateIds()
    {
        var invalidCriteria = new List<EligibilityCriterion>
        {
            new("CRIT-01", "Valid Criterion", "INCLUSION", "Age", ">=", "18"),
            new("CRIT-01", "Duplicate ID Criterion", "INCLUSION", "Bmi", "INVALID_OP", "25")
        };

        var result = ProtocolQualitySentinel.ValidateCriteria(invalidCriteria);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duplicate criterion ID"));
        result.Errors.Should().Contain(e => e.Contains("invalid or unsupported"));
    }

    [Fact]
    public void EligibilityWaterfallEngine_CalculatesAccurateAttritionWaterfall()
    {
        var criteria = new List<EligibilityCriterion>
        {
            new("CRIT-01", "Age 18-65", "INCLUSION", "Age", "BETWEEN", "18", HighValue: "65"),
            new("CRIT-02", "Systolic BP <= 140", "INCLUSION", "SystolicBp", "<=", "140")
        };

        var candidates = MonteCarloCohortSampler.GenerateCohort(sampleSize: 500);

        var waterfall = EligibilityWaterfallEngine.EvaluateWaterfall("PROT-TEST-01", criteria, candidates);

        waterfall.ProtocolId.Should().Be("PROT-TEST-01");
        waterfall.InitialPopulationCount.Should().Be(500);
        waterfall.Steps.Should().HaveCount(2);
        waterfall.FinalEligibleCount.Should().BeLessThanOrEqualTo(500);
        waterfall.OverallFeasibilityYieldPercentage.Should().BeInRange(0.0, 100.0);
    }

    [Fact]
    public void PropensityScoreMatchingEngine_MatchesCohortAndComputesBalanceMetrics()
    {
        var criteria = new List<EligibilityCriterion>
        {
            new("CRIT-01", "Age >= 18", "INCLUSION", "Age", ">=", "18")
        };

        var request = new PropensityMatchingRequest("STUDY-001", criteria, TargetSampleSize: 100, Caliper: 0.25);
        var rweCandidates = MonteCarloCohortSampler.GenerateCohort(sampleSize: 300);

        var result = PropensityScoreMatchingEngine.MatchSyntheticArm(request, rweCandidates);

        result.StudyId.Should().Be("STUDY-001");
        result.MatchedControlCount.Should().BeGreaterThan(0);
        result.MatchedSyntheticArm.Should().NotBeEmpty();
        result.CovariateBalance.Should().NotBeEmpty();
        result.CovariateBalance.Should().Contain(c => c.CovariateName == "Age");
        result.LatencyMs.Should().BeLessThan(3000);
    }
}
