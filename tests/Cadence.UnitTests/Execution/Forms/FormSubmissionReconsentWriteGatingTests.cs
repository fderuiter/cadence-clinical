using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Core.Domain;
using Cadence.Testing;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

using Xunit;

namespace Cadence.UnitTests.Execution.Forms;

public class FormSubmissionReconsentWriteGatingTests
{
    private sealed class FakeUnitOfWork
    {
        public List<ClinicalSubject> Subjects { get; } = [];
        public List<SubjectConsent> Consents { get; } = [];
        public List<FormSubmission> Forms { get; } = [];

        public static Task SaveChangesAsync() => Task.CompletedTask;
    }

    private sealed class FakeFormSubmissionRepository(FakeUnitOfWork uow) : IFormSubmissionRepository
    {
        public Task AddAsync(FormSubmission submission, CancellationToken cancellationToken)
        {
            uow.Forms.Add(submission);
            return Task.CompletedTask;
        }

        public Task<FormSubmission?> GetByIdAsync(FormSubmissionId id, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Forms.FirstOrDefault(f => f.Id == id));

        public Task<FormSubmission?> GetForUpdateAsync(FormSubmissionId id, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Forms.FirstOrDefault(f => f.Id == id));

        public Task<FormSubmission?> GetByCoordinatesAsync(StudyCode studyId, SubjectCode subjectId, string? visitId, FormId formId, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Forms.FirstOrDefault(f => f.StudyId == studyId && f.SubjectId == subjectId && f.FormId == formId && f.VisitId == visitId));

        public Task<IReadOnlyList<FormSubmission>> ListBySubjectAsync(StudyCode studyId, SubjectCode subjectId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FormSubmission>>(uow.Forms.Where(f => f.StudyId == studyId && f.SubjectId == subjectId).ToList());

        public Task<IReadOnlyList<FormSubmission>> ListByVisitAsync(StudyCode studyId, SubjectCode subjectId, string visitId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FormSubmission>>(uow.Forms.Where(f => f.StudyId == studyId && f.SubjectId == subjectId && f.VisitId == visitId).ToList());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => FakeUnitOfWork.SaveChangesAsync();
    }

    private sealed class FakeObservationRepository : IObservationRepository
    {
        public Task<ClinicalObservation?> GetByIdAsync(ObservationId id, CancellationToken cancellationToken) => Task.FromResult<ClinicalObservation?>(null);
        public Task<ClinicalObservation?> GetForUpdateAsync(ObservationId id, CancellationToken cancellationToken) => Task.FromResult<ClinicalObservation?>(null);
        public Task<IReadOnlyList<ClinicalObservation>> ListBySubjectAsync(StudyCode studyId, SubjectCode subjectId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClinicalObservation>>([]);
        public Task<IReadOnlyList<ClinicalObservation>> ListByCoordinatesAsync(StudyCode studyId, SubjectCode subjectId, string? visitId, DomainCode? domain, TestCode? testCode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClinicalObservation>>([]);
        public Task AddAsync(ClinicalObservation observation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeConsentRepository(FakeUnitOfWork uow) : IConsentRepository
    {
        public Task<bool> ExistsAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Consents.Any(c => c.SubjectCode == subjectCode && c.ProtocolVersion.StudyCode == studyCode && c.ProtocolVersion.VersionIndex == versionIndex));

        public Task<SubjectConsent?> GetAsync(SubjectCode subjectCode, StudyCode studyCode, int versionIndex, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Consents.FirstOrDefault(c => c.SubjectCode == subjectCode && c.ProtocolVersion.StudyCode == studyCode && c.ProtocolVersion.VersionIndex == versionIndex));

        public Task<IReadOnlyList<SubjectConsent>> GetOtherRequiringReconsentAsync(SubjectCode subjectCode, StudyCode studyCode, int excludeVersionIndex, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubjectConsent>>(uow.Consents.Where(c => c.SubjectCode == subjectCode && c.ProtocolVersion.StudyCode == studyCode && c.RequiresReconsent).ToList());

        public Task AddAsync(SubjectConsent consent, CancellationToken cancellationToken)
        {
            uow.Consents.Add(consent);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => FakeUnitOfWork.SaveChangesAsync();
    }

    private sealed class FakeSubjectRepository(FakeUnitOfWork uow) : ISubjectRepository
    {
        public Task<ClinicalSubject?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Subjects.FirstOrDefault(s => s.Id.ToString() == id));

        public Task<ClinicalSubject?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(uow.Subjects.FirstOrDefault(s => s.SubjectCode.Value == code));

        public Task<SubjectPage> ListAsync(SubjectListCriteria criteria, CancellationToken cancellationToken)
        {
            var items = uow.Subjects.Where(s => s.StudyCode == criteria.StudyCode).ToList();
            return Task.FromResult(new SubjectPage(items, items.Count));
        }

        public Task<int> EnrollNextIndexAsync(StudyCode studyCode, CancellationToken cancellationToken) => Task.FromResult(1);
        public Task AddAsync(ClinicalSubject subject, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken) => FakeUnitOfWork.SaveChangesAsync();
    }

    private sealed class PassThroughUcumService : IUcumNormalizationService
    {
        public string NormalizeUnitName(string unit) => unit;
        public double ConvertUnit(double value, string fromUnit, string toUnit) => value;
        public (double? NormalizedValue, string? NormalizedUnit) GetNormalizedRepresentation(double? value, string? unit) => (value, unit);
    }

    [Fact]
    public async Task CreateAsync_WhenSubjectRequiresReconsent_BlocksSubmissionWithReconsentRequiredError()
    {
        var uow = new FakeUnitOfWork();
        var subjectCode = new SubjectCode("SUBJ-GATED-01");
        var studyCode = new StudyCode("STUDY-GATED-01");

        var consent = SubjectConsent.Create(
            subjectCode: subjectCode,
            version: new ProtocolVersionRef(studyCode, "2.0.0", 2),
            icfSigned: false,
            icfSignedDate: null,
            requiresReconsent: true,
            isPaperOverride: false
        );
        uow.Consents.Add(consent);

        var service = new FormSubmissionApplicationService(
            formSubmissionRepository: new FakeFormSubmissionRepository(uow),
            observationRepository: new FakeObservationRepository(),
            ucumService: new PassThroughUcumService(),
            auditContextAccessor: new AmbientAuditContextAccessor(new HttpContextAccessor()),
            consentRepository: new FakeConsentRepository(uow),
            subjectRepository: new FakeSubjectRepository(uow)
        );

        var request = new FormSubmissionCreateRequest(
            StudyId: studyCode.Value,
            SubjectId: subjectCode.Value,
            FormId: "FORM-VS-01",
            SiteId: "SITE-01",
            VisitId: "VISIT-002",
            ProtocolVersion: "2.0.0",
            Payload: "{\"vssbp\": 120}"
        );

        var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("RECONSENT_REQUIRED");
    }
}
