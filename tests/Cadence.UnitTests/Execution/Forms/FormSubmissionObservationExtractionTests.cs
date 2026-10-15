using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Forms;

/// <summary>
/// Unit tests verifying that eCRF form observation extraction filters empty/null payload keys
/// and avoids persisting phantom observations while preserving numeric zero values.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-002
/// </summary>
public sealed class FormSubmissionObservationExtractionTests
{
    private readonly InMemoryFormSubmissionRepository _formRepo = new();
    private readonly InMemoryObservationRepository _obsRepo = new();
    private readonly InMemorySubjectRepository _subjectRepo = new();
    private readonly UcumNormalizationService _ucumService = new();
    private readonly ICurrentAuditContextAccessor _auditAccessor;

    public FormSubmissionObservationExtractionTests()
    {
        _auditAccessor = Substitute.For<ICurrentAuditContextAccessor>();
        _auditAccessor.UserId.Returns("test-user");
        _auditAccessor.ChangeReason.Returns("Observation extraction test");
    }

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
    public async Task CreateAsync_WithNullAndEmptyPayloadKeys_IgnoresEmptyKeysAndPersistsNoPhantomObservations()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-101", "STUDY-001");
        var service = CreateService();
        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: "1.0.0",
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": null, \"vsdpb\": \"\", \"pulse\": \"   \"}");

        // Act
        var result = await service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var activeObs = await _obsRepo.ListBySubjectAsync(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-101"), CancellationToken.None);
        activeObs.Where(o => o.IsActive).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithNumericZeroPayloadValues_PreservesZeroAsValidObservation()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-102", "STUDY-001");
        var service = CreateService();
        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-102",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: "1.0.0",
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 0, \"vsdpb\": \"0\"}");

        // Act
        var result = await service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var activeObs = await _obsRepo.ListBySubjectAsync(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-102"), CancellationToken.None);
        activeObs.Where(o => o.IsActive).Should().HaveCount(2);

        var ssbpObs = activeObs.First(o => o.TestCode.Value == "VSSBP");
        ssbpObs.Value.Should().Be(0);

        var sdpbObs = activeObs.First(o => o.TestCode.Value == "VSDPB");
        sdpbObs.Value.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_WithStructuredObservationsArrayContainingNulls_SkipsNullObservations()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-103", "STUDY-001");
        var service = CreateService();
        var payload = """
        {
          "observation_date": "2026-01-01T08:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic Blood Pressure",
              "value": null,
              "value_string": null,
              "unit": "mmHg"
            },
            {
              "domain": "VS",
              "test_code": "VSDPB",
              "test_name": "Diastolic Blood Pressure",
              "value": 80,
              "value_string": "80",
              "unit": "mmHg"
            }
          ]
        }
        """;

        var request = new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-103",
            FormId: "FORM-VS",
            SiteId: "SITE-01",
            VisitId: "VISIT-01",
            ProtocolVersion: "1.0.0",
            Payload: payload);

        // Act
        var result = await service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var activeObs = await _obsRepo.ListBySubjectAsync(new StudyCode("STUDY-001"), new SubjectCode("SUBJ-103"), CancellationToken.None);
        var active = activeObs.Where(o => o.IsActive).ToList();
        active.Should().HaveCount(1);
        active[0].TestCode.Value.Should().Be("VSDPB");
        active[0].Value.Should().Be(80);
    }
}
