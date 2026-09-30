using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Observations;

/// <summary>
/// Golden master parity tests verifying clinical observation numerical stability,
/// unit conversions, lab reference boundaries, and SDV state machine behavior against Python EDC baseline.
/// </summary>
public sealed class ObservationGoldenMasterTests
{
    /// <summary>
    /// Validates UCUM canonical normalization across canonical test vectors matching Python baseline.
    /// </summary>
    [Theory]
    [InlineData(98.6, "[degF]", 37.0, "Cel")]
    [InlineData(100.4, "[degF]", 38.0, "Cel")]
    [InlineData(37.0, "Cel", 37.0, "Cel")]
    [InlineData(154.32, "[lb_av]", 70.0, "kg")]
    [InlineData(70.0, "kg", 70.0, "kg")]
    [InlineData(120.0, "mm[Hg]", 120.0, "mm[Hg]")]
    public void UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(
        double inputValue,
        string inputUnit,
        double expectedNormValue,
        string expectedNormUnit)
    {
        var ucum = new UcumNormalizationService();
        var (normVal, normUnit) = ucum.GetNormalizedRepresentation(inputValue, inputUnit);

        normVal.Should().NotBeNull();
        Math.Round(normVal!.Value, 1).Should().Be(expectedNormValue);
        normUnit.Should().Be(expectedNormUnit);
    }

    /// <summary>
    /// Validates lab range classification across demographics vectors matching Python baseline.
    /// </summary>
    [Theory]
    [InlineData("GLUC", 90.0, 30, "M", false, "NORMAL")]
    [InlineData("GLUC", 55.0, 30, "M", true, "LOW")]
    [InlineData("GLUC", 180.0, 30, "M", true, "HIGH")]
    [InlineData("K", 4.0, 50, "F", false, "NORMAL")]
    [InlineData("K", 2.5, 50, "F", true, "LOW")]
    [InlineData("K", 6.5, 50, "F", true, "HIGH")]
    [InlineData("WBC", 6.5, 25, "M", false, "NORMAL")]
    [InlineData("WBC", 2.0, 25, "M", true, "LOW")]
    [InlineData("WBC", 16.0, 25, "M", true, "HIGH")]
    public void LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(
        string testCode,
        double value,
        int age,
        string sex,
        bool expectedOutOfRange,
        string expectedIndicator)
    {
        var labRange = new LabRangeService();
        var result = labRange.EvaluateRange(testCode, value, age, sex);

        result.IsOutOfRange.Should().Be(expectedOutOfRange);
        result.Indicator.Should().Be(expectedIndicator);
    }

    /// <summary>
    /// Validates SDV state transitions and immutable audit properties under golden master scenarios.
    /// </summary>
    [Fact]
    public void SdvVerification_StateTransitions_MaintainStrictPart11Audit()
    {
        var obs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            value: 120.0,
            observationDate: FakeClock.DefaultStart.UtcDateTime).Value!;

        obs.IsSdvVerified.Should().BeFalse();
        obs.VersionIndex.Should().Be(1);

        var verifyTime = FakeClock.DefaultStart;
        var verifyRes = obs.VerifySdv("cra_user_alpha", verifyTime);
        verifyRes.IsSuccess.Should().BeTrue();
        obs.IsSdvVerified.Should().BeTrue();
        obs.SdvVerifiedBy.Should().Be("cra_user_alpha");
        obs.SdvVerifiedAt.Should().Be(verifyTime);

        // Flagging resets verification
        var flagRes = obs.FlagSdv("Query raised on source doc");
        flagRes.IsSuccess.Should().BeTrue();
        obs.IsSdvVerified.Should().BeFalse();
        obs.SdvVerifiedBy.Should().BeNull();
        obs.ReasonForChange.Should().Be("Query raised on source doc");
    }
}
