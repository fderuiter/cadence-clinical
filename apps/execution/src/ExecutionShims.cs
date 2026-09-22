using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories
{
    public class SaveChangesResiliencePipelineFactory { public static object Create() => new(); }

    public class SubjectRepository : ISubjectRepository
    {
        Task<int> ISubjectRepository.EnrollNextIndexAsync(StudyCode studyCode, CancellationToken cancellationToken) => Task.FromResult(0);
        Task<ClinicalSubject?> ISubjectRepository.GetByIdAsync(string id, CancellationToken cancellationToken) => Task.FromResult<ClinicalSubject?>(null);
        Task<ClinicalSubject?> ISubjectRepository.GetByCodeAsync(string code, CancellationToken cancellationToken) => Task.FromResult<ClinicalSubject?>(null);
        Task ISubjectRepository.AddAsync(ClinicalSubject subject, CancellationToken cancellationToken) => Task.CompletedTask;
        Task ISubjectRepository.SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class ConsentRepository : IConsentRepository
    {
        Task<bool> IConsentRepository.ExistsAsync(SubjectCode subjectCode, StudyCode studyCode, int version, CancellationToken cancellationToken) => Task.FromResult(false);
        Task<SubjectConsent?> IConsentRepository.GetAsync(SubjectCode subjectCode, StudyCode studyCode, int version, CancellationToken cancellationToken) => Task.FromResult<SubjectConsent?>(null);
        Task<IReadOnlyList<SubjectConsent>> IConsentRepository.GetOtherRequiringReconsentAsync(SubjectCode subjectCode, StudyCode studyCode, int version, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SubjectConsent>>(Array.Empty<SubjectConsent>());
        Task IConsentRepository.AddAsync(SubjectConsent consent, CancellationToken cancellationToken) => Task.CompletedTask;
        Task IConsentRepository.SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class SiteComplianceRepository : ISiteComplianceRepository
    {
        Task<SiteComplianceCache?> ISiteComplianceRepository.GetAsync(StudyCode studyCode, SiteCode? siteCode, string version, CancellationToken cancellationToken) => Task.FromResult<SiteComplianceCache?>(null);
        Task ISiteComplianceRepository.AddAsync(SiteComplianceCache cache, CancellationToken cancellationToken) => Task.CompletedTask;
        Task ISiteComplianceRepository.SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class ObservationRepository : IObservationRepository
    {
        Task<ClinicalObservation?> IObservationRepository.GetByIdAsync(ObservationId id, CancellationToken cancellationToken) => Task.FromResult<ClinicalObservation?>(null);
        Task<IReadOnlyList<ClinicalObservation>> IObservationRepository.ListBySubjectAsync(StudyCode studyCode, SubjectCode subjectCode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClinicalObservation>>(Array.Empty<ClinicalObservation>());
        Task<IReadOnlyList<ClinicalObservation>> IObservationRepository.ListByCoordinatesAsync(StudyCode studyCode, SubjectCode subjectCode, string? visitId, DomainCode? domain, TestCode? testCode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClinicalObservation>>(Array.Empty<ClinicalObservation>());
        Task IObservationRepository.AddAsync(ClinicalObservation observation, CancellationToken cancellationToken) => Task.CompletedTask;
        Task IObservationRepository.SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class FormSubmissionRepository : IFormSubmissionRepository
    {
        Task<FormSubmission?> IFormSubmissionRepository.GetByIdAsync(FormSubmissionId id, CancellationToken cancellationToken) => Task.FromResult<FormSubmission?>(null);
        Task<FormSubmission?> IFormSubmissionRepository.GetByCoordinatesAsync(StudyCode studyCode, SubjectCode subjectCode, string? visitId, FormId formId, CancellationToken cancellationToken) => Task.FromResult<FormSubmission?>(null);
        Task<IReadOnlyList<FormSubmission>> IFormSubmissionRepository.ListBySubjectAsync(StudyCode studyCode, SubjectCode subjectCode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FormSubmission>>(Array.Empty<FormSubmission>());
        Task<IReadOnlyList<FormSubmission>> IFormSubmissionRepository.ListByVisitAsync(StudyCode studyCode, SubjectCode subjectCode, string visitId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FormSubmission>>(Array.Empty<FormSubmission>());
        Task IFormSubmissionRepository.AddAsync(FormSubmission submission, CancellationToken cancellationToken) => Task.CompletedTask;
        Task IFormSubmissionRepository.SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class ClinicalQueryRepository : IClinicalQueryRepository
    {
        Task<ClinicalQuery?> IClinicalQueryRepository.GetByIdAsync(QueryId id, CancellationToken cancellationToken) => Task.FromResult<ClinicalQuery?>(null);
        Task<ClinicalQuery?> IClinicalQueryRepository.GetActiveQueryOnCoordinatesAsync(StudyCode studyCode, SubjectCode subjectCode, string? visitId, DomainCode? domain, TestCode testCode, CancellationToken cancellationToken) => Task.FromResult<ClinicalQuery?>(null);
        Task<IReadOnlyList<ClinicalQuery>> IClinicalQueryRepository.ListBySubjectAsync(StudyCode studyCode, SubjectCode subjectCode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClinicalQuery>>(Array.Empty<ClinicalQuery>());
        Task<IReadOnlyList<ClinicalQuery>> IClinicalQueryRepository.ListAsync(StudyCode? studyCode, SubjectCode? subjectCode, string? visitId, QueryStatus? status, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClinicalQuery>>(Array.Empty<ClinicalQuery>());
        Task IClinicalQueryRepository.AddAsync(ClinicalQuery query, CancellationToken cancellationToken) => Task.CompletedTask;
        Task IClinicalQueryRepository.SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class EfAuditLogWriter : IAuditLogWriter
    {
        Task IAuditLogWriter.WriteAsync(string eventType, string entityType, string entityId, string action, string actor, IReadOnlyDictionary<string, object?>? beforeState, IReadOnlyDictionary<string, object?>? afterState, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class ConcurrencyTokenIncrementInterceptor { }
    public class GxPAuditInterceptor { public GxPAuditInterceptor(object? arg = null) { } }
}

namespace Cadence.Apps.Execution.Infrastructure.Consumers
{
    public class EConsentCompletedConsumer { }
}

namespace Cadence.Apps.Execution.Infrastructure.EditChecks
{
    public class BatchEvaluationContextLoader : IBatchEvaluationContextLoader
    {
        Task<BatchEvaluationContext> IBatchEvaluationContextLoader.LoadAsync(StudyCode studyCode, SubjectCode subjectCode, CancellationToken cancellationToken) => Task.FromResult<BatchEvaluationContext>(null!);
    }

    public class AstEditCheckEngine : IEditCheckEngine
    {
        object? IEditCheckEngine.EvaluateAst(object? astNode, IReadOnlyDictionary<string, object?> context) => null;
        IReadOnlyList<EditCheckViolation> IEditCheckEngine.EvaluateInlineConstraints(ClinicalObservation observation) => Array.Empty<EditCheckViolation>();
        IReadOnlyList<EditCheckViolation> IEditCheckEngine.EvaluateBatchRules(BatchEvaluationContext context) => Array.Empty<EditCheckViolation>();
    }
}
