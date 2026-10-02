using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Core.Domain;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Observations;

/// <summary>
/// Unit test coverage for clinical observation domain invariants, UCUM unit normalization,
/// age/sex lab reference ranges, outlier detection, and SDV verification workflows.
/// </summary>
public sealed class ObservationUnitTests
{

    /// <summary>
    /// Validates that creating a clinical observation sets initial properties, version index, and SDV unverified status.
    /// </summary>
    [Fact]
    public void CreateObservation_WithValidParameters_InitializesCorrectly()
    {
        var studyId = new StudyCode("STUDY-101");
        var subjectId = new SubjectCode("SUBJ-001");
        var domain = new DomainCode("VS");
        var testCode = new TestCode("SYSBP");
        var now = FakeClock.DefaultStart.UtcDateTime;

        var result = ClinicalObservation.Create(
            studyId: studyId,
            subjectId: subjectId,
            domain: domain,
            testCode: testCode,
            testName: "Systolic Blood Pressure",
            value: 120.0,
            valueString: "120",
            unit: "mmHg",
            observationDate: now,
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01");

        result.IsSuccess.Should().BeTrue();
        var obs = result.Value!;
        obs.StudyId.Should().Be(studyId);
        obs.SubjectId.Should().Be(subjectId);
        obs.Domain.Should().Be(domain);
        obs.TestCode.Should().Be(testCode);
        obs.Value.Should().Be(120.0);
        obs.Unit.Should().Be("mmHg");
        obs.IsSdvVerified.Should().BeFalse();
        obs.SdvVerifiedBy.Should().BeNull();
        obs.VersionIndex.Should().Be(1);
    }

    /// <summary>
    /// Validates that verifying SDV records verifier identity, timestamp, and advances audit state.
    /// </summary>
    [Fact]
    public void VerifySdv_WhenUnverified_TransitionsToVerified()
    {
        var obs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-101"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            value: 120.0,
            observationDate: FakeClock.DefaultStart.UtcDateTime).Value!;

        var verifyTime = FakeClock.DefaultStart;
        var result = obs.VerifySdv("cra_user_01", verifyTime);

        result.IsSuccess.Should().BeTrue();
        obs.IsSdvVerified.Should().BeTrue();
        obs.SdvVerifiedBy.Should().Be("cra_user_01");
        obs.SdvVerifiedAt.Should().Be(verifyTime);
    }

    /// <summary>
    /// Validates that attempting to re-verify an already verified observation returns a validation error.
    /// </summary>
    [Fact]
    public void VerifySdv_WhenAlreadyVerified_ReturnsError()
    {
        var obs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-101"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            value: 120.0,
            observationDate: FakeClock.DefaultStart.UtcDateTime).Value!;

        obs.VerifySdv("cra_user_01", FakeClock.DefaultStart);
        var secondResult = obs.VerifySdv("cra_user_02", FakeClock.DefaultStart);

        secondResult.IsFailure.Should().BeTrue();
        secondResult.Error!.Code.Should().Be("OBSERVATION_ALREADY_VERIFIED");
    }

    /// <summary>
    /// Validates that flagging an observation for SDV discrepancy resets verified status and records reason.
    /// </summary>
    [Fact]
    public void FlagSdv_WithReason_ResetsSdvVerification()
    {
        var obs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-101"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            value: 120.0,
            observationDate: FakeClock.DefaultStart.UtcDateTime).Value!;

        obs.VerifySdv("cra_user_01", FakeClock.DefaultStart);
        var flagResult = obs.FlagSdv("Source document legibility issue");

        flagResult.IsSuccess.Should().BeTrue();
        obs.IsSdvVerified.Should().BeFalse();
        obs.SdvVerifiedBy.Should().BeNull();
        obs.SdvVerifiedAt.Should().BeNull();
        obs.ReasonForChange.Should().Be("Source document legibility issue");
    }

    /// <summary>
    /// Validates that UCUM canonical normalization converts temperatures and weights accurately.
    /// </summary>
    [Fact]
    public void UcumNormalization_ConvertsUnitsCorrectly()
    {
        var ucum = new UcumNormalizationService();

        // Temperature: Fahrenheit to Celsius (98.6 F -> 37.0 C)
        var (normTemp, normTempUnit) = ucum.GetNormalizedRepresentation(98.6, "[degF]");
        normTemp.Should().NotBeNull();
        Math.Round(normTemp!.Value, 1).Should().Be(37.0);
        normTempUnit.Should().Be("Cel");

        // Weight: pounds to kg (154 lb -> ~69.85 kg)
        var (normMass, normMassUnit) = ucum.GetNormalizedRepresentation(154.0, "[lb_av]");
        normMass.Should().NotBeNull();
        Math.Round(normMass!.Value, 2).Should().Be(69.85);
        normMassUnit.Should().Be("kg");

        // Pressure: mmHg (identity)
        var (normPres, normPresUnit) = ucum.GetNormalizedRepresentation(120.0, "mm[Hg]");
        normPres.Should().Be(120.0);
        normPresUnit.Should().Be("mm[Hg]");
    }

    /// <summary>
    /// Validates that LabRangeService determines whether lab values fall within age/sex-adjusted normal ranges.
    /// </summary>
    [Fact]
    public void LabRangeService_EvaluatesNormalAndOutOfRangeValues()
    {
        var labRange = new LabRangeService();

        // Normal glucose (85 mg/dL)
        var normalResult = labRange.EvaluateRange("GLUC", 85.0, 35, "M");
        normalResult.IsOutOfRange.Should().BeFalse();
        normalResult.Indicator.Should().Be("NORMAL");

        // High glucose (150 mg/dL)
        var highResult = labRange.EvaluateRange("GLUC", 150.0, 35, "M");
        highResult.IsOutOfRange.Should().BeTrue();
        highResult.Indicator.Should().Be("HIGH");

        // Low potassium (3.0 mmol/L)
        var lowResult = labRange.EvaluateRange("K", 3.0, 45, "F");
        lowResult.IsOutOfRange.Should().BeTrue();
        lowResult.Indicator.Should().Be("LOW");
    }

    /// <summary>
    /// Validates that OutlierDetectionService flags statistical outliers using the 3.5 IQR threshold.
    /// </summary>
    [Fact]
    public void OutlierDetection_IdentifiesStatisticalOutliers()
    {
        var outlierService = new OutlierDetectionService();
        var cohort = new List<double> { 118, 120, 122, 119, 121, 125, 120, 123 };

        // Value within normal cohort spread
        outlierService.IsOutlier(124.0, cohort).Should().BeFalse();

        // Extreme physiological value (extreme high)
        outlierService.IsOutlier(250.0, cohort).Should().BeTrue();

        // Extreme physiological value (extreme low)
        outlierService.IsOutlier(30.0, cohort).Should().BeTrue();

        // Small cohort (< 4 points) does not flag outliers
        var smallCohort = new List<double> { 120, 122 };
        outlierService.IsOutlier(300.0, smallCohort).Should().BeFalse();
    }

    /// <summary>
    /// Validates that ObservationApplicationService normalizes units and stores observation in repository.
    /// </summary>
    [Fact]
    public async Task ObservationApplicationService_Create_PersistsAndReturnsNormalizedObservation()
    {
        var repo = new InMemoryObservationRepository();
        var subjectRepo = new InMemorySubjectRepository();
        var ucum = new UcumNormalizationService();
        var labRange = new LabRangeService();
        var editChecks = new AstEditCheckEngine();
        var clock = new FakeClock();
        var audit = new FakeCurrentAuditContextAccessor();

        var service = new ObservationApplicationService(
            repo,
            subjectRepo,
            ucum,
            labRange,
            editChecks,
            clock,
            audit);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-101",
            SubjectId: "SUBJ-001",
            Domain: "VS",
            TestCode: "TEMP",
            TestName: "Body Temperature",
            ObservationDate: FakeClock.DefaultStart.UtcDateTime,
            Value: 98.6,
            Unit: "degF");

        var response = await service.CreateAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var val = response.Value!;
        val.TestCode.Should().Be("TEMP");
        val.Value.Should().Be(98.6);
        val.NormalizedValue.Should().NotBeNull();
        Math.Round(val.NormalizedValue!.Value, 1).Should().Be(37.0);
        val.NormalizedUnit.Should().Be("Cel");

        var persisted = await repo.GetByIdAsync(ObservationId.Parse(val.Id), CancellationToken.None);
        persisted.Should().NotBeNull();
        persisted!.NormalizedValue.Should().NotBeNull();
    }

    /// <summary>
    /// Validates that UCUM normalization returns IsNormalized = false, NormalizedValue = null, NormalizedUnit = null,
    /// and StatusCode = "UNMAPPED" for unrecognized unit strings.
    /// @req:SRS-EDC-001
    /// </summary>
    [Fact]
    public void UcumNormalization_UnmappedUnit_ReturnsUnmappedResult()
    {
        var ucum = new UcumNormalizationService();

        var result = ucum.GetNormalizedRepresentation(100.0, "INVALID_LOCAL_UNIT");

        result.IsNormalized.Should().BeFalse();
        result.NormalizedValue.Should().BeNull();
        result.NormalizedUnit.Should().BeNull();
        result.StatusCode.Should().Be("UNMAPPED");
    }

    /// <summary>
    /// Validates that LabRangeService sets Indicator = "UNNORMALIZED_UNIT" and OutOfRange = false
    /// when given an observation with an unmapped or unnormalized unit.
    /// @req:SRS-EDC-001
    /// </summary>
    [Fact]
    public void LabRangeService_UnmappedUnit_ReturnsUnnormalizedIndicatorAndNoRangeWarning()
    {
        var labRange = new LabRangeService();

        var result = labRange.EvaluateRange("GLUC", 150.0, 35, "M", isNormalized: false);

        result.Indicator.Should().Be("UNNORMALIZED_UNIT");
        result.IsOutOfRange.Should().BeFalse();
        result.MatchedBoundsJson.Should().BeNull();
    }

    /// <summary>
    /// Validates that OutlierDetectionService excludes observations with IsNormalized = false or NormalizedValue = null
    /// from statistical cohort distributions.
    /// @req:SRS-EDC-001
    /// </summary>
    [Fact]
    public void OutlierDetection_UnmappedUnit_ExcludesFromCohortDistribution()
    {
        var outlierService = new OutlierDetectionService();

        var validObs1 = ClinicalObservation.Create(new StudyCode("S1"), new SubjectCode("SUBJ-1"), new DomainCode("VS"), new TestCode("WT"), "Weight", FakeClock.DefaultStart.UtcDateTime, 70.0, normalizedValue: 70.0, normalizedUnit: "kg").Value!;
        var validObs2 = ClinicalObservation.Create(new StudyCode("S1"), new SubjectCode("SUBJ-2"), new DomainCode("VS"), new TestCode("WT"), "Weight", FakeClock.DefaultStart.UtcDateTime, 72.0, normalizedValue: 72.0, normalizedUnit: "kg").Value!;
        var validObs3 = ClinicalObservation.Create(new StudyCode("S1"), new SubjectCode("SUBJ-3"), new DomainCode("VS"), new TestCode("WT"), "Weight", FakeClock.DefaultStart.UtcDateTime, 71.0, normalizedValue: 71.0, normalizedUnit: "kg").Value!;
        var validObs4 = ClinicalObservation.Create(new StudyCode("S1"), new SubjectCode("SUBJ-4"), new DomainCode("VS"), new TestCode("WT"), "Weight", FakeClock.DefaultStart.UtcDateTime, 69.0, normalizedValue: 69.0, normalizedUnit: "kg").Value!;

        // Unmapped unit observation submitted with raw value 500 in unknown unit "stones_x"
        var unmappedProps = new ObservationAdditionalProperties { UnitNormalizationStatus = "UNMAPPED" };
        var unmappedObs = ClinicalObservation.Create(new StudyCode("S1"), new SubjectCode("SUBJ-5"), new DomainCode("VS"), new TestCode("WT"), "Weight", FakeClock.DefaultStart.UtcDateTime, 500.0, unit: "stones_x", normalizedValue: null, normalizedUnit: null, additionalProperties: unmappedProps).Value!;

        var cohort = new List<ClinicalObservation> { validObs1, validObs2, validObs3, validObs4, unmappedObs };

        int outlierCount = outlierService.RecalculateOutliers(cohort);

        unmappedObs.IsOutlier.Should().BeFalse();
        outlierCount.Should().Be(0);
    }

    /// <summary>
    /// Validates ingestion of observation with unmapped local unit: sets NormalizedValue and NormalizedUnit to null,
    /// persists unit_normalization_status = "UNMAPPED", sets LabIndicator = "UNNORMALIZED_UNIT", and prevents false range warnings.
    /// @req:SRS-EDC-001
    /// </summary>
    [Fact]
    public async Task ObservationApplicationService_UnmappedUnit_IngestsObservationWithNullNormalizedProjectionsAndUnmappedStatus()
    {
        var repo = new InMemoryObservationRepository();
        var subjectRepo = new InMemorySubjectRepository();
        var ucum = new UcumNormalizationService();
        var labRange = new LabRangeService();
        var editChecks = new AstEditCheckEngine();
        var clock = new FakeClock();
        var audit = new FakeCurrentAuditContextAccessor();

        var service = new ObservationApplicationService(
            repo,
            subjectRepo,
            ucum,
            labRange,
            editChecks,
            clock,
            audit);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-101",
            SubjectId: "SUBJ-001",
            Domain: "LB",
            TestCode: "GLUC",
            TestName: "Glucose",
            ObservationDate: FakeClock.DefaultStart.UtcDateTime,
            Value: 180.0,
            Unit: "UNRECOGNIZED_LAB_UNIT");

        var response = await service.CreateAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var obs = response.Value!;
        obs.Value.Should().Be(180.0);
        obs.Unit.Should().Be("UNRECOGNIZED_LAB_UNIT");
        obs.NormalizedValue.Should().BeNull();
        obs.NormalizedUnit.Should().BeNull();

        var persisted = await repo.GetByIdAsync(ObservationId.Parse(obs.Id), CancellationToken.None);
        persisted.Should().NotBeNull();
        persisted!.NormalizedValue.Should().BeNull();
        persisted.NormalizedUnit.Should().BeNull();
        persisted.AdditionalProperties.UnitNormalizationStatus.Should().Be("UNMAPPED");
        persisted.LabIndicator.Should().Be("UNNORMALIZED_UNIT");
        persisted.LabOutOfRange.Should().BeFalse();
    }
}
