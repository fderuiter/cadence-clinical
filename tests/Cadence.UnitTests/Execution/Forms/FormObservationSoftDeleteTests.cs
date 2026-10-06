using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Application.Queries;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Forms;

/// <summary>
/// Unit tests verifying observation soft deletion and invalidation on form payload updates.
/// Ensures clinical data integrity by soft-deleting previous observations upon form edits while preserving historical records for audit trails.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-042
/// @req:SRS-EDC-001
/// </summary>
public sealed class FormObservationSoftDeleteTests
{
    private readonly InMemoryFormSubmissionRepository _formRepo = new();
    private readonly InMemoryObservationRepository _obsRepo = new();
    private readonly InMemorySubjectRepository _subjectRepo = new();
    private readonly UcumNormalizationService _ucumService = new();
    private readonly FakeCurrentAuditContextAccessor _auditAccessor = new("test-user", "Unit test soft delete");

    private FormSubmissionApplicationService CreateService() =>
        new(_formRepo, _obsRepo, _ucumService, _auditAccessor, _subjectRepo);

    private async Task SeedSubjectAsync(string subjectCodeStr, string studyCodeStr)
    {
        var subject = ClinicalSubject.Create(
            subjectCode: new SubjectCode(subjectCodeStr),
            studyCode: new StudyCode(studyCodeStr),
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 0,
            encryptedDemographics: null,
            stratFactors: null);

        await _subjectRepo.AddAsync(subject, CancellationToken.None);
    }

    [Fact]
    public void ClinicalObservation_CreateAndDeactivate_UpdatesIsActiveProperty()
    {
        // Arrange
        var submissionId = FormSubmissionId.New();
        var obsResult = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic Blood Pressure",
            observationDate: DateTime.Parse("2026-10-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal),
            value: 120,
            unit: "mmHg",
            formSubmissionId: submissionId,
            isActive: true);

        obsResult.IsSuccess.Should().BeTrue();
        var obs = obsResult.Value!;
        obs.IsActive.Should().BeTrue();
        obs.FormSubmissionId.Should().Be(submissionId);

        // Act
        obs.Deactivate();

        // Assert
        obs.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdatePayloadAsync_WhenPayloadUpdated_DeactivatesPreviousObservationsAndCreatesNewActiveObservations()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-101", "STUDY-001");
        var service = CreateService();

        string initialPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "SYSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "mmHg"
            }
          ]
        }
        """;

        var createRequest = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            FormId: "VS_FORM",
            Payload: initialPayload,
            SiteId: "SITE-01",
            VisitId: "VISIT-01");

        var createResult = await service.CreateAsync(createRequest, CancellationToken.None);
        createResult.IsSuccess.Should().BeTrue();
        string submissionIdStr = createResult.Value!.Id;
        var submissionId = FormSubmissionId.Parse(submissionIdStr);

        var initialObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: true, CancellationToken.None);
        initialObs.Should().HaveCount(1);
        initialObs[0].IsActive.Should().BeTrue();
        initialObs[0].Value.Should().Be(120);

        string updatedPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "SYSBP",
              "test_name": "Systolic BP",
              "value": 135,
              "unit": "mmHg"
            }
          ]
        }
        """;

        // Act
        var updateResult = await service.UpdatePayloadAsync(submissionIdStr, updatedPayload, CancellationToken.None);

        // Assert
        updateResult.IsSuccess.Should().BeTrue();

        var allObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: true, CancellationToken.None);
        allObs.Should().HaveCount(2);

        var inactiveObs = allObs.Where(o => !o.IsActive).ToList();
        var activeObs = allObs.Where(o => o.IsActive).ToList();

        inactiveObs.Should().HaveCount(1);
        inactiveObs[0].Value.Should().Be(120);

        activeObs.Should().HaveCount(1);
        activeObs[0].Value.Should().Be(135);
        activeObs[0].FormSubmissionId.Should().Be(submissionId);

        var defaultQueryObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        defaultQueryObs.Should().HaveCount(1);
        defaultQueryObs[0].Value.Should().Be(135);
    }

    [Fact]
    public async Task RepositoryQueries_ByDefault_ExcludeInactiveObservations()
    {
        // Arrange
        var studyCode = new StudyCode("STUDY-001");
        var subjectCode = new SubjectCode("SUBJ-102");
        var submissionId = FormSubmissionId.New();
        var baseDate = DateTime.Parse("2026-10-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);

        var activeObs = ClinicalObservation.Create(
            studyId: studyCode,
            subjectId: subjectCode,
            domain: new DomainCode("LB"),
            testCode: new TestCode("GLUC"),
            testName: "Glucose",
            observationDate: baseDate,
            value: 95,
            unit: "mg/dL",
            formSubmissionId: submissionId,
            isActive: true).Value!;

        var inactiveObs = ClinicalObservation.Create(
            studyId: studyCode,
            subjectId: subjectCode,
            domain: new DomainCode("LB"),
            testCode: new TestCode("GLUC"),
            testName: "Glucose",
            observationDate: baseDate.AddHours(-1),
            value: 150,
            unit: "mg/dL",
            formSubmissionId: submissionId,
            isActive: true).Value!;

        inactiveObs.Deactivate();

        await _obsRepo.AddAsync(activeObs, CancellationToken.None);
        await _obsRepo.AddAsync(inactiveObs, CancellationToken.None);

        // Act & Assert - GetByIdAsync
        (await _obsRepo.GetByIdAsync(activeObs.Id, CancellationToken.None)).Should().NotBeNull();
        (await _obsRepo.GetByIdAsync(inactiveObs.Id, CancellationToken.None)).Should().BeNull();
        (await _obsRepo.GetByIdAsync(inactiveObs.Id, includeInactive: true, CancellationToken.None)).Should().NotBeNull();

        // Act & Assert - ListBySubjectAsync
        var activeSubjectList = await _obsRepo.ListBySubjectAsync(studyCode, subjectCode, CancellationToken.None);
        activeSubjectList.Should().HaveCount(1);
        activeSubjectList[0].Id.Should().Be(activeObs.Id);

        var allSubjectList = await _obsRepo.ListBySubjectAsync(studyCode, subjectCode, includeInactive: true, CancellationToken.None);
        allSubjectList.Should().HaveCount(2);

        // Act & Assert - ListByCoordinatesAsync
        var activeCoordList = await _obsRepo.ListByCoordinatesAsync(studyCode, subjectCode, null, new DomainCode("LB"), new TestCode("GLUC"), CancellationToken.None);
        activeCoordList.Should().HaveCount(1);
        activeCoordList[0].Id.Should().Be(activeObs.Id);

        var allCoordList = await _obsRepo.ListByCoordinatesAsync(studyCode, subjectCode, null, new DomainCode("LB"), new TestCode("GLUC"), includeInactive: true, CancellationToken.None);
        allCoordList.Should().HaveCount(2);
    }

    [Fact]
    public void BatchEvaluationContext_FiltersOutInactiveObservations()
    {
        // Arrange
        var studyCode = new StudyCode("STUDY-001");
        var subjectCode = new SubjectCode("SUBJ-103");

        var activeObs = ClinicalObservation.Create(
            studyId: studyCode,
            subjectId: subjectCode,
            domain: new DomainCode("VS"),
            testCode: new TestCode("PULSE"),
            testName: "Pulse Rate",
            observationDate: DateTime.Parse("2026-10-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal),
            value: 72,
            unit: "beats/min",
            isActive: true).Value!;

        var inactiveObs = ClinicalObservation.Create(
            studyId: studyCode,
            subjectId: subjectCode,
            domain: new DomainCode("VS"),
            testCode: new TestCode("PULSE"),
            testName: "Pulse Rate",
            observationDate: DateTime.Parse("2026-10-01T09:30:00Z", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal),
            value: 180,
            unit: "beats/min",
            isActive: true).Value!;

        inactiveObs.Deactivate();

        // Act
        var context = new BatchEvaluationContext(
            studyId: studyCode.Value,
            subjectId: subjectCode.Value,
            observations: [activeObs, inactiveObs],
            formSubmissions: []);

        // Assert
        context.Observations.Should().HaveCount(1);
        context.Observations[0].Id.Should().Be(activeObs.Id);
        context.GetObservationsByTestCode("PULSE").Should().HaveCount(1);
        context.GetObservationsByTestCode("PULSE")[0].Value.Should().Be(72);
    }

    [Fact]
    public async Task UpdatePayloadAsync_WhenFieldRemoved_AutomaticallyCancelsOpenQueriesAndDeactivatesObservations()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-201", "STUDY-001");

        var queryRepo = new InMemoryClinicalQueryRepository();
        var editCheckEngine = new AstEditCheckEngine();
        var dispatchService = new AutoQueryDispatchService(queryRepo, new FakeClock(), _auditAccessor);
        var service = new FormSubmissionApplicationService(
            _formRepo,
            _obsRepo,
            _ucumService,
            _auditAccessor,
            _subjectRepo,
            editCheckEngine,
            dispatchService);

        string initialPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "INVALID_UNIT"
            },
            {
              "domain": "VS",
              "test_code": "VSDPB",
              "test_name": "Diastolic BP",
              "value": 80,
              "unit": "INVALID_UNIT"
            }
          ]
        }
        """;

        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-201",
            FormId: "VS_FORM",
            Payload: initialPayload,
            SiteId: "SITE-01",
            VisitId: "VISIT-01"), CancellationToken.None);

        createResult.IsSuccess.Should().BeTrue();
        string submissionIdStr = createResult.Value!.Id;

        var queriesBefore = await queryRepo.ListBySubjectAsync(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-201"), CancellationToken.None);
        queriesBefore.Should().HaveCount(2);
        var sbpQuery = queriesBefore.First(q => q.TestCode.Value.Equals("VSSBP", StringComparison.OrdinalIgnoreCase));
        sbpQuery.Status.Should().Be(QueryStatus.Open);

        // Act: Update form removing vssbp field (only vsdpb remains with corrected unit)
        string updatedPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSDPB",
              "test_name": "Diastolic BP",
              "value": 80,
              "unit": "mmHg"
            }
          ]
        }
        """;

        var updateResult = await service.UpdatePayloadAsync(submissionIdStr, updatedPayload, CancellationToken.None);
        updateResult.IsSuccess.Should().BeTrue();

        // Assert: SBP query should be cancelled and DBP query resolved (unit corrected)
        sbpQuery.Status.Should().Be(QueryStatus.Cancelled);
        sbpQuery.CancellationReason.Should().Contain("field removal");

        var dbpQuery = queriesBefore.First(q => q.TestCode.Value.Equals("VSDPB", StringComparison.OrdinalIgnoreCase));
        dbpQuery.Status.Should().Be(QueryStatus.Closed);

        var activeObs = await _obsRepo.ListBySubjectAsync(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-201"), includeInactive: false, CancellationToken.None);
        activeObs.Should().ContainSingle(o => o.TestCode.Value == "VSDPB");
    }

    [Fact]
    public async Task UpdatePayloadAsync_WhenPayloadCleared_DeactivatesObservationsAndCancelsOpenQueries()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-202", "STUDY-001");

        var queryRepo = new InMemoryClinicalQueryRepository();
        var editCheckEngine = new AstEditCheckEngine();
        var dispatchService = new AutoQueryDispatchService(queryRepo, new FakeClock(), _auditAccessor);
        var service = new FormSubmissionApplicationService(
            _formRepo,
            _obsRepo,
            _ucumService,
            _auditAccessor,
            _subjectRepo,
            editCheckEngine,
            dispatchService);

        string initialPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "INVALID_UNIT"
            }
          ]
        }
        """;

        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-202",
            FormId: "VS_FORM",
            Payload: initialPayload,
            SiteId: "SITE-01",
            VisitId: "VISIT-01"), CancellationToken.None);

        createResult.IsSuccess.Should().BeTrue();
        string submissionIdStr = createResult.Value!.Id;

        var queriesBefore = await queryRepo.ListBySubjectAsync(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-202"), CancellationToken.None);
        var openQuery = queriesBefore.Single(q => q.Status == QueryStatus.Open);

        // Act: Clear form payload (empty payload)
        var updateResult = await service.UpdatePayloadAsync(submissionIdStr, "", CancellationToken.None);
        updateResult.IsSuccess.Should().BeTrue();

        // Assert: Observations deactivated, query cancelled
        var activeObs = await _obsRepo.ListBySubjectAsync(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-202"), includeInactive: false, CancellationToken.None);
        activeObs.Should().BeEmpty();

        openQuery.Status.Should().Be(QueryStatus.Cancelled);
        openQuery.CancellationReason.Should().Contain("field removal");
    }
}
