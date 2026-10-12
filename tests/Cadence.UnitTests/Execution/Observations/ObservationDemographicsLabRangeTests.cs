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
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Observations;

/// <summary>
/// Verifies subject demographics resolution (sex and age) during observation ingestion
/// and reference range evaluation against sex-stratified normal bounds.
/// </summary>
public sealed class ObservationDemographicsLabRangeTests
{
    private static readonly DateTimeOffset FixedNow = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (ObservationApplicationService service, InMemorySubjectRepository subjectRepo) CreateServiceWithDemographics(
        Dictionary<string, object?>? demographicsPayload = null,
        bool simulateDecryptFailure = false)
    {
        var repo = new InMemoryObservationRepository();
        var subjectRepo = new InMemorySubjectRepository();
        var ucum = new UcumNormalizationService();
        var labRange = new LabRangeService();
        var editChecks = new AstEditCheckEngine();
        var clock = new FakeClock(FixedNow);
        var audit = new FakeCurrentAuditContextAccessor();

        IDemographicsEncryptionService encService = simulateDecryptFailure
            ? FakeDemographicsEncryptionService.ThrowingOnDecrypt()
            : FakeDemographicsEncryptionService.ReturningPayload(demographicsPayload ?? []);

        var demographicsService = new DemographicsNormalizationService(encService, clock);

        var service = new ObservationApplicationService(
            repo,
            subjectRepo,
            ucum,
            labRange,
            editChecks,
            clock,
            audit,
            demographicsNormalizationService: demographicsService);

        return (service, subjectRepo);
    }

    [Fact]
    public async Task IngestObservation_ForRegisteredFemaleSubject_EvaluatesAgainstFemaleRange()
    {
        var payload = new Dictionary<string, object?>
        {
            ["gender"] = "FEMALE",
            ["birthdate"] = "1994-01-01"
        };

        var (service, subjectRepo) = CreateServiceWithDemographics(payload);

        var subject = ClinicalSubject.Create(
            subjectCode: new SubjectCode("SUBJ-F01"),
            studyCode: new StudyCode("STUDY-101"),
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 1,
            encryptedDemographics: "enc_female_ciphertext",
            stratFactors: null);

        await subjectRepo.AddAsync(subject, CancellationToken.None);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-101",
            SubjectId: "SUBJ-F01",
            Domain: "LB",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            ObservationDate: FixedNow.UtcDateTime,
            Value: 38.0,
            Unit: "U/L");

        var response = await service.CreateAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var obs = response.Value!;
        obs.IsOutOfRange.Should().BeTrue();
        obs.RangeIndicator.Should().Be("HIGH");
        obs.ReferenceRangeLow.Should().Be(7.0);
        obs.ReferenceRangeHigh.Should().Be(35.0);
    }

    [Fact]
    public async Task IngestObservation_ForRegisteredMaleSubject_EvaluatesAgainstMaleRange()
    {
        var payload = new Dictionary<string, object?>
        {
            ["gender"] = "MALE",
            ["birthdate"] = "1994-01-01"
        };

        var (service, subjectRepo) = CreateServiceWithDemographics(payload);

        var subject = ClinicalSubject.Create(
            subjectCode: new SubjectCode("SUBJ-M01"),
            studyCode: new StudyCode("STUDY-101"),
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 2,
            encryptedDemographics: "enc_male_ciphertext",
            stratFactors: null);

        await subjectRepo.AddAsync(subject, CancellationToken.None);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-101",
            SubjectId: "SUBJ-M01",
            Domain: "LB",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            ObservationDate: FixedNow.UtcDateTime,
            Value: 38.0,
            Unit: "U/L");

        var response = await service.CreateAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var obs = response.Value!;
        obs.IsOutOfRange.Should().BeFalse();
        obs.RangeIndicator.Should().Be("NORMAL");
        obs.ReferenceRangeLow.Should().Be(10.0);
        obs.ReferenceRangeHigh.Should().Be(40.0);
    }

    [Fact]
    public async Task IngestObservation_ForUnregisteredSubject_FallsBackToUnstratifiedRange()
    {
        var (service, _) = CreateServiceWithDemographics();

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-101",
            SubjectId: "SUBJ-UNREG",
            Domain: "LB",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            ObservationDate: FixedNow.UtcDateTime,
            Value: 38.0,
            Unit: "U/L");

        var response = await service.CreateAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var obs = response.Value!;
        obs.IsOutOfRange.Should().BeFalse();
        obs.RangeIndicator.Should().Be("NORMAL");
        obs.ReferenceRangeLow.Should().Be(10.0);
        obs.ReferenceRangeHigh.Should().Be(40.0);
    }

    [Fact]
    public async Task IngestObservation_ForSubjectWithMissingOrFailedDemographics_FallsBackToUnstratifiedRange()
    {
        var (service, subjectRepo) = CreateServiceWithDemographics(simulateDecryptFailure: true);

        var subject = ClinicalSubject.Create(
            subjectCode: new SubjectCode("SUBJ-CORRUPT"),
            studyCode: new StudyCode("STUDY-101"),
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 3,
            encryptedDemographics: "corrupt_ciphertext",
            stratFactors: null);

        await subjectRepo.AddAsync(subject, CancellationToken.None);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-101",
            SubjectId: "SUBJ-CORRUPT",
            Domain: "LB",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            ObservationDate: FixedNow.UtcDateTime,
            Value: 38.0,
            Unit: "U/L");

        var response = await service.CreateAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var obs = response.Value!;
        obs.IsOutOfRange.Should().BeFalse();
        obs.RangeIndicator.Should().Be("NORMAL");
    }

    private sealed class TrackingLabRangeService : ILabRangeService
    {
        public string? CapturedSex { get; private set; }
        public int? CapturedAgeYears { get; private set; }

        public LabAssessmentResult Evaluate(
            TestCode testCode,
            double? value,
            string? sex = null,
            int? ageYears = null,
            string? labSource = null,
            string? studyId = null,
            string? siteId = null)
        {
            CapturedSex = sex;
            CapturedAgeYears = ageYears;
            return new LabAssessmentResult("NORMAL", false, null, 10.0, 40.0);
        }

        public LabAssessmentResult EvaluateRange(
            string testCode,
            double? value,
            int? ageYears = null,
            string? sex = null,
            string? labSource = null,
            string? studyId = null,
            string? siteId = null) =>
            Evaluate(new TestCode(testCode), value, sex, ageYears, labSource, studyId, siteId);

        public void UpdateActiveRanges(IEnumerable<LabReferenceRange> ranges)
        {
            // No-op for mock tracking service
        }
    }

    [Fact]
    public async Task IngestObservation_ConvertsDecimalAgeToIntegerYearsForLabRangeService()
    {
        var repo = new InMemoryObservationRepository();
        var subjectRepo = new InMemorySubjectRepository();
        var ucum = new UcumNormalizationService();
        var trackingLabRange = new TrackingLabRangeService();
        var editChecks = new AstEditCheckEngine();
        var clock = new FakeClock(FixedNow);
        var audit = new FakeCurrentAuditContextAccessor();

        // 30 years and ~9 months prior to FixedNow (2024-01-01) -> birthdate 1993-04-01 -> age ~30.75
        var payload = new Dictionary<string, object?>
        {
            ["gender"] = "FEMALE",
            ["birthdate"] = "1993-04-01"
        };

        var encService = FakeDemographicsEncryptionService.ReturningPayload(payload);
        var demographicsService = new DemographicsNormalizationService(encService, clock);

        var service = new ObservationApplicationService(
            repo,
            subjectRepo,
            ucum,
            trackingLabRange,
            editChecks,
            clock,
            audit,
            demographicsNormalizationService: demographicsService);

        var subject = ClinicalSubject.Create(
            subjectCode: new SubjectCode("SUBJ-AGE-01"),
            studyCode: new StudyCode("STUDY-101"),
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 4,
            encryptedDemographics: "enc_ciphertext",
            stratFactors: null);

        await subjectRepo.AddAsync(subject, CancellationToken.None);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-101",
            SubjectId: "SUBJ-AGE-01",
            Domain: "LB",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            ObservationDate: FixedNow.UtcDateTime,
            Value: 38.0,
            Unit: "U/L");

        var response = await service.CreateAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        trackingLabRange.CapturedSex.Should().Be("F");
        trackingLabRange.CapturedAgeYears.Should().Be(30);
    }

    [Fact]
    public async Task IngestObservation_WithBackdatedObservationDate_ComputesAgeRelativeToObservationDateNotCurrentClock()
    {
        var repo = new InMemoryObservationRepository();
        var subjectRepo = new InMemorySubjectRepository();
        var ucum = new UcumNormalizationService();
        var trackingLabRange = new TrackingLabRangeService();
        var editChecks = new AstEditCheckEngine();
        var clock = new FakeClock(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)); // 2020-01-01
        var audit = new FakeCurrentAuditContextAccessor();

        // Subject born on 2000-01-01. Age at current clock (2020-01-01) is 20.
        var payload = new Dictionary<string, object?>
        {
            ["gender"] = "MALE",
            ["birthdate"] = "2000-01-01"
        };

        var encService = FakeDemographicsEncryptionService.ReturningPayload(payload);
        var demographicsService = new DemographicsNormalizationService(encService, clock);

        var service = new ObservationApplicationService(
            repo,
            subjectRepo,
            ucum,
            trackingLabRange,
            editChecks,
            clock,
            audit,
            demographicsNormalizationService: demographicsService);

        var subject = ClinicalSubject.Create(
            subjectCode: new SubjectCode("SUBJ-HIST-01"),
            studyCode: new StudyCode("STUDY-101"),
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 5,
            encryptedDemographics: "enc_ciphertext",
            stratFactors: null);

        await subjectRepo.AddAsync(subject, CancellationToken.None);

        // Observation collected 4 years prior on 2016-01-01 (when subject was 16)
        var backdatedObservationDate = new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var request = new ObservationCreateRequest(
            StudyId: "STUDY-101",
            SubjectId: "SUBJ-HIST-01",
            Domain: "LB",
            TestCode: "ALT",
            TestName: "Alanine Aminotransferase",
            ObservationDate: backdatedObservationDate,
            Value: 38.0,
            Unit: "U/L");

        var response = await service.CreateAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        trackingLabRange.CapturedSex.Should().Be("M");
        trackingLabRange.CapturedAgeYears.Should().Be(16);
    }
}
