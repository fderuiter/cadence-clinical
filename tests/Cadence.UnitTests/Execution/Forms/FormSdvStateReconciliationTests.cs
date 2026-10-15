using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Kernel;
using Cadence.Testing;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Forms;

/// <summary>
/// Unit tests verifying SDV state reconciliation, re-verification flagging, and audit trail logging during eCRF form updates.
/// </summary>
public sealed class FormSdvStateReconciliationTests
{
    private readonly InMemoryFormSubmissionRepository _formRepo = new();
    private readonly InMemoryObservationRepository _obsRepo = new();
    private readonly InMemorySubjectRepository _subjectRepo = new();
    private readonly UcumNormalizationService _ucumService = new();
    private readonly FakeCurrentAuditContextAccessor _auditAccessor = new("cra-user-01", "SDV reconciliation test");
    private readonly TestAuditLogWriter _auditWriter = new();

    private FormSubmissionApplicationService CreateService() =>
        new(_formRepo, _obsRepo, _ucumService, _auditAccessor, _subjectRepo, auditLogWriter: _auditWriter);

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
    [GxPRequirement("PRD-QRY-005")]
    public async Task UpdatePayloadAsync_WhenVerifiedFieldUnchanged_RetainsSdvVerificationState()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-301", "STUDY-001");
        var service = CreateService();

        string initialPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "mmHg"
            },
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

        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-301",
            FormId: "VS_FORM",
            Payload: initialPayload,
            SiteId: "SITE-01",
            VisitId: "VISIT-01"), CancellationToken.None);

        createResult.IsSuccess.Should().BeTrue();
        string submissionIdStr = createResult.Value!.Id;
        var submissionId = FormSubmissionId.Parse(submissionIdStr);

        var initialObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        initialObs.Should().HaveCount(2);

        var sbpObs = initialObs.First(o => o.TestCode.Value == "VSSBP");
        var verifiedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        sbpObs.VerifySdv("cra-monitor-99", verifiedAt);

        string updatedPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "mmHg"
            },
            {
              "domain": "VS",
              "test_code": "VSDPB",
              "test_name": "Diastolic BP",
              "value": 85,
              "unit": "mmHg"
            }
          ]
        }
        """;

        // Act
        var updateResult = await service.UpdatePayloadAsync(submissionIdStr, updatedPayload, CancellationToken.None);

        // Assert
        updateResult.IsSuccess.Should().BeTrue();

        var activeObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        activeObs.Should().HaveCount(2);

        var newSbpObs = activeObs.First(o => o.TestCode.Value == "VSSBP");
        newSbpObs.IsSdvVerified.Should().BeTrue();
        newSbpObs.SdvVerifiedBy.Should().Be("cra-monitor-99");
        newSbpObs.SdvVerifiedAt.Should().BeCloseTo(verifiedAt, TimeSpan.FromMilliseconds(1));
        newSbpObs.IsSdvFlagged.Should().BeFalse();

        var newDbpObs = activeObs.First(o => o.TestCode.Value == "VSDPB");
        newDbpObs.Value.Should().Be(85);
        newDbpObs.IsSdvVerified.Should().BeFalse();
        newDbpObs.IsSdvFlagged.Should().BeFalse();

        _auditWriter.RecordedAudits.Should().NotContain(a => a.Action == "SDV_STATUS_RESET" && a.RecordId == newSbpObs.Id.ToString());
    }

    [Fact]
    [GxPRequirement("PRD-QRY-006")]
    public async Task UpdatePayloadAsync_WhenVerifiedFieldModified_ResetsSdvAndSetsFlagAndDispatchesAudit()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-302", "STUDY-001");
        var service = CreateService();

        string initialPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "mmHg"
            }
          ]
        }
        """;

        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-302",
            FormId: "VS_FORM",
            Payload: initialPayload,
            SiteId: "SITE-01",
            VisitId: "VISIT-01"), CancellationToken.None);

        string submissionIdStr = createResult.Value!.Id;
        var submissionId = FormSubmissionId.Parse(submissionIdStr);

        var initialObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        var sbpObs = initialObs.Single();
        sbpObs.VerifySdv("cra-monitor-99", new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        string updatedPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
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

        var activeObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        var newSbpObs = activeObs.Single();

        newSbpObs.Value.Should().Be(135);
        newSbpObs.IsSdvVerified.Should().BeFalse();
        newSbpObs.SdvVerifiedBy.Should().BeNull();
        newSbpObs.SdvVerifiedAt.Should().BeNull();
        newSbpObs.IsSdvFlagged.Should().BeTrue();
        newSbpObs.SdvFlagReason.Should().Be("Field changed after SDV verification. Re-verification required");

        var auditEvent = _auditWriter.RecordedAudits.SingleOrDefault(a => a.Action == "SDV_STATUS_RESET");
        auditEvent.Should().NotBeNull();
        auditEvent!.EntityName.Should().Be("ClinicalObservation");
        auditEvent.RecordId.Should().Be(newSbpObs.Id.ToString());
        auditEvent.UserId.Should().Be("cra-user-01");
        auditEvent.ChangeReason.Should().Be("Field changed after SDV verification. Re-verification required");
        auditEvent.OldValues.Should().ContainKey("Value").WhoseValue.Should().Be(120.0);
        auditEvent.NewValues.Should().ContainKey("Value").WhoseValue.Should().Be(135.0);
    }

    [Fact]
    public async Task UpdatePayloadAsync_WhenUnverifiedFieldModified_DoesNotSetFlagOrAudit()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-303", "STUDY-001");
        var service = CreateService();

        string initialPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "mmHg"
            }
          ]
        }
        """;

        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-303",
            FormId: "VS_FORM",
            Payload: initialPayload,
            SiteId: "SITE-01",
            VisitId: "VISIT-01"), CancellationToken.None);

        string submissionIdStr = createResult.Value!.Id;
        var submissionId = FormSubmissionId.Parse(submissionIdStr);

        string updatedPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 125,
              "unit": "mmHg"
            }
          ]
        }
        """;

        // Act
        var updateResult = await service.UpdatePayloadAsync(submissionIdStr, updatedPayload, CancellationToken.None);

        // Assert
        updateResult.IsSuccess.Should().BeTrue();

        var activeObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        var newSbpObs = activeObs.Single();

        newSbpObs.IsSdvVerified.Should().BeFalse();
        newSbpObs.IsSdvFlagged.Should().BeFalse();
        _auditWriter.RecordedAudits.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdatePayloadAsync_WhenVerifiedFieldRemoved_DeactivatesAndDispatchesDeactivatedAudit()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-304", "STUDY-001");
        var service = CreateService();

        string initialPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "mmHg"
            },
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

        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-304",
            FormId: "VS_FORM",
            Payload: initialPayload,
            SiteId: "SITE-01",
            VisitId: "VISIT-01"), CancellationToken.None);

        string submissionIdStr = createResult.Value!.Id;
        var submissionId = FormSubmissionId.Parse(submissionIdStr);

        var initialObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        var sbpObs = initialObs.First(o => o.TestCode.Value == "VSSBP");
        sbpObs.VerifySdv("cra-monitor-99", new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        // Act: remove VSSBP, keep VSDPB
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

        // Assert
        updateResult.IsSuccess.Should().BeTrue();

        var activeObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        activeObs.Should().ContainSingle(o => o.TestCode.Value == "VSDPB");

        var allObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: true, CancellationToken.None);
        var inactiveSbp = allObs.First(o => o.TestCode.Value == "VSSBP");
        inactiveSbp.IsActive.Should().BeFalse();

        var auditEvent = _auditWriter.RecordedAudits.SingleOrDefault(a => a.Action == "SDV_RECORD_DEACTIVATED");
        auditEvent.Should().NotBeNull();
        auditEvent!.EntityName.Should().Be("ClinicalObservation");
        auditEvent.RecordId.Should().Be(sbpObs.Id.ToString());
        auditEvent.NewValues.Should().ContainKey("IsActive").WhoseValue.Should().Be(false);
    }

    [Fact]
    public async Task UpdatePayloadAsync_NormalizesObservationDatesToUtcForExactMatchComparison()
    {
        // Arrange
        await SeedSubjectAsync("SUBJ-305", "STUDY-001");
        var service = CreateService();

        string initialPayload = """
        {
          "observation_date": "2026-10-01T10:00:00Z",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "mmHg"
            }
          ]
        }
        """;

        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-305",
            FormId: "VS_FORM",
            Payload: initialPayload,
            SiteId: "SITE-01",
            VisitId: "VISIT-01"), CancellationToken.None);

        string submissionIdStr = createResult.Value!.Id;
        var submissionId = FormSubmissionId.Parse(submissionIdStr);

        var initialObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        var sbpObs = initialObs.Single();
        sbpObs.VerifySdv("cra-monitor-99", new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        // Same instant represented with explicit +00:00 UTC offset string
        string updatedPayload = """
        {
          "observation_date": "2026-10-01T10:00:00+00:00",
          "observations": [
            {
              "domain": "VS",
              "test_code": "VSSBP",
              "test_name": "Systolic BP",
              "value": 120,
              "unit": "mmHg"
            }
          ]
        }
        """;

        // Act
        var updateResult = await service.UpdatePayloadAsync(submissionIdStr, updatedPayload, CancellationToken.None);

        // Assert
        updateResult.IsSuccess.Should().BeTrue();

        var activeObs = await _obsRepo.ListByFormSubmissionAsync(submissionId, includeInactive: false, CancellationToken.None);
        var newSbpObs = activeObs.Single();

        newSbpObs.IsSdvVerified.Should().BeTrue();
        newSbpObs.SdvVerifiedBy.Should().Be("cra-monitor-99");
        newSbpObs.IsSdvFlagged.Should().BeFalse();
        _auditWriter.RecordedAudits.Should().BeEmpty();
    }

    private sealed class TestAuditLogWriter : IAuditLogWriter
    {
        public List<RecordedAudit> RecordedAudits { get; } = new();

        public Task WriteAsync(
            string entityName,
            string recordId,
            string action,
            string userId,
            string changeReason,
            IReadOnlyDictionary<string, object?>? oldValues,
            IReadOnlyDictionary<string, object?>? newValues,
            CancellationToken ct)
        {
            RecordedAudits.Add(new RecordedAudit(entityName, recordId, action, userId, changeReason, oldValues, newValues));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedAudit(
        string EntityName,
        string RecordId,
        string Action,
        string UserId,
        string ChangeReason,
        IReadOnlyDictionary<string, object?>? OldValues,
        IReadOnlyDictionary<string, object?>? NewValues);
}
