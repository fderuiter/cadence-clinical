using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Sentinels;
using Cadence.Sentinels.Checks;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class FraudSentinelTests
{
    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Benford_ExtractLeadingDigit_ExtractsCorrectDigit()
    {
        BenfordDigitPreferenceSentinelCheck.ExtractLeadingDigit(120.5).Should().Be(1);
        BenfordDigitPreferenceSentinelCheck.ExtractLeadingDigit(0.00345).Should().Be(3);
        BenfordDigitPreferenceSentinelCheck.ExtractLeadingDigit(-85.2).Should().Be(8);
        BenfordDigitPreferenceSentinelCheck.ExtractLeadingDigit(0.0).Should().Be(0);
    }

    [Fact]
    public async Task Benford_ConformingDistribution_PassesCheck()
    {
        var check = new BenfordDigitPreferenceSentinelCheck();

        var obs = new List<FraudObservationValue>();
        void AddDigits(double baseVal, int count)
        {
            for (int i = 0; i < count; i++)
            {
                obs.Add(new FraudObservationValue("VSSBP", baseVal + (i * 0.1)));
            }
        }

        AddDigits(100.0, 30);
        AddDigits(200.0, 18);
        AddDigits(300.0, 12);
        AddDigits(400.0, 10);
        AddDigits(500.0, 8);
        AddDigits(600.0, 7);
        AddDigits(700.0, 6);
        AddDigits(800.0, 5);
        AddDigits(900.0, 4);

        var baseline = new StatisticalBaseline(new Dictionary<int, long>(), 0, new Dictionary<string, FieldBaselineStats>());
        var context = new FraudEvaluationContext("SUB-1", "STUDY-1", "SUBJ-1", "SITE-1", "FORM-1", "VISIT-1", obs, baseline);

        var result = await check.EvaluateAsync(context, Ct);

        result.Passed.Should().BeTrue();
        result.Violations.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Benford_FabricatedDataDistribution_FailsCheckAndProducesViolation()
    {
        var check = new BenfordDigitPreferenceSentinelCheck();

        var obs = new List<FraudObservationValue>();
        for (int i = 0; i < 50; i++)
        {
            obs.Add(new FraudObservationValue("VSSBP", 99.0 + i));
            obs.Add(new FraudObservationValue("VSSBP", 88.0 + i));
        }

        var baseline = new StatisticalBaseline(new Dictionary<int, long>(), 0, new Dictionary<string, FieldBaselineStats>());
        var context = new FraudEvaluationContext("SUB-1", "STUDY-1", "SUBJ-1", "SITE-1", "FORM-1", "VISIT-1", obs, baseline);

        var result = await check.EvaluateAsync(context, Ct);

        result.Passed.Should().BeFalse();
        result.Violations.Should().NotBeNullOrEmpty();
        result.Violations![0].Should().Contain("Benford's Law Chi-Square digit preference anomaly detected");
    }

    [Fact]
    public async Task SiteVariance_NormalVariance_PassesCheck()
    {
        var check = new SiteVarianceSentinelCheck();

        var obs = new List<FraudObservationValue>
        {
            new("VSSBP", 120.0),
            new("VSSBP", 125.0),
            new("VSSBP", 118.0),
            new("VSSBP", 122.0)
        };

        var fieldStats = new Dictionary<string, FieldBaselineStats>(StringComparer.OrdinalIgnoreCase)
        {
            ["VSSBP"] = new FieldBaselineStats(Mean: 121.0, Variance: 10.0, SampleSize: 50)
        };

        var baseline = new StatisticalBaseline(new Dictionary<int, long>(), 0, fieldStats);
        var context = new FraudEvaluationContext("SUB-1", "STUDY-1", "SUBJ-1", "SITE-1", "FORM-1", "VISIT-1", obs, baseline);

        var result = await check.EvaluateAsync(context, Ct);

        result.Passed.Should().BeTrue();
        result.Violations.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task SiteVariance_ShrinkageAnomaly_FailsCheckAndProducesViolation()
    {
        var check = new SiteVarianceSentinelCheck();

        var obs = new List<FraudObservationValue>
        {
            new("VSSBP", 120.0),
            new("VSSBP", 120.0),
            new("VSSBP", 120.0),
            new("VSSBP", 120.0)
        };

        var fieldStats = new Dictionary<string, FieldBaselineStats>(StringComparer.OrdinalIgnoreCase)
        {
            ["VSSBP"] = new FieldBaselineStats(Mean: 120.0, Variance: 25.0, SampleSize: 100)
        };

        var baseline = new StatisticalBaseline(new Dictionary<int, long>(), 0, fieldStats);
        var context = new FraudEvaluationContext("SUB-1", "STUDY-1", "SUBJ-1", "SITE-1", "FORM-1", "VISIT-1", obs, baseline);

        var result = await check.EvaluateAsync(context, Ct);

        result.Passed.Should().BeFalse();
        result.Violations.Should().NotBeNullOrEmpty();
        result.Violations![0].Should().Contain("Site variance shrinkage anomaly detected");
    }
}
