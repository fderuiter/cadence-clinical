using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Testing;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Forms;

/// <summary>
/// Unit test coverage for subject active protocol version resolution and validation during form submission creation.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-002
/// </summary>
public sealed class FormSubmissionProtocolVersionTests
{
    private readonly InMemoryFormSubmissionRepository _formRepo = new();
    private readonly InMemoryObservationRepository _obsRepo = new();
    private readonly InMemorySubjectRepository _subjectRepo = new();
    private readonly UcumNormalizationService _ucumService = new();
    private readonly FakeCurrentAuditContextAccessor _auditAccessor = new("test-user", "unit test");

    private FormSubmissionApplicationService CreateService() =>
        new(_formRepo, _obsRepo, _ucumService, _auditAccessor, _subjectRepo);

    private async Task<ClinicalSubject> SeedSubjectAsync(string subjectCodeStr, string studyCodeStr, string protocolVersion)
    {
        var subjectCode = new SubjectCode(subjectCodeStr);
        var studyCode = new StudyCode(studyCodeStr);
        var subject = ClinicalSubject.Create(
            subjectCode: subjectCode,
            studyCode: studyCode,
            siteCode: new SiteCode("SITE-01"),
            enrollmentIndex: 0,
            encryptedDemographics: null,
            stratFactors: null);

        subject.SetActiveProtocolVersion(protocolVersion);
        await _subjectRepo.AddAsync(subject, CancellationToken.None);
        return subject;
    }

    [Fact]
    public async Task CreateAsync_WhenProtocolVersionOmitted_InheritsSubjectActiveProtocolVersion()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-200", "STUDY-001", "2.0.0");
        var service = CreateService();

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-200",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: null,
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}");

        // Act
        var result = await service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ProtocolVersion.Should().Be("2.0.0");

        var formId = FormSubmissionId.Parse(result.Value.Id);
        var persistedForm = await _formRepo.GetByIdAsync(formId, CancellationToken.None);
        persistedForm.Should().NotBeNull();
        persistedForm!.ProtocolVersion.Should().Be("2.0.0");
    }

    [Fact]
    public async Task CreateAsync_WhenExplicitProtocolVersionMatchesActive_SucceedsAndPersistsSpecifiedVersion()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-200", "STUDY-001", "2.0.0");
        var service = CreateService();

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-200",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: "2.0.0",
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}");

        // Act
        var result = await service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ProtocolVersion.Should().Be("2.0.0");
    }

    [Fact]
    public async Task CreateAsync_WhenExplicitProtocolVersionMismatchesActive_FailsWithValidationError()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-200", "STUDY-001", "2.0.0");
        var service = CreateService();

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-200",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: "1.0.0",
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}");

        // Act
        var result = await service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PROTOCOL_VERSION_MISMATCH");
        result.Error.Description.Should().Contain("1.0.0").And.Contain("2.0.0");
    }

    [Fact]
    public async Task CreateAsync_WhenSubjectNotFoundInRepository_FallsBackToVersion100()
    {
        // Arrange (no subject seeded)
        var service = CreateService();

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-UNKNOWN",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: null,
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}");

        // Act
        var result = await service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ProtocolVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task CreateAsync_WhenSubjectNotFoundInRepository_ExplicitMismatch_FailsWithValidationError()
    {
        // Arrange (no subject seeded; fallback active version is "1.0.0")
        var service = CreateService();

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-UNKNOWN",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: "2.0.0",
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}");

        // Act
        var result = await service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PROTOCOL_VERSION_MISMATCH");
    }

    [Fact]
    public async Task CreateAsync_ExtractedObservations_StoreResolvedSubjectProtocolVersion()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-200", "STUDY-001", "2.0.0");
        var service = CreateService();

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-200",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: null,
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120, \"vsdpb\": 80}");

        // Act
        var result = await service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var studyCode = new StudyCode("STUDY-001");
        var subjectCode = new SubjectCode("SUBJ-200");
        var observations = await _obsRepo.ListBySubjectAsync(studyCode, subjectCode, CancellationToken.None);

        observations.Should().NotBeEmpty();
        foreach (var obs in observations)
        {
            obs.ProtocolVersionTag.Should().Be("2.0.0");
        }
    }
}
