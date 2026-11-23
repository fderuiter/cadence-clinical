using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Cgt;

public interface ICgtApplicationService
{
    Task<CgtChainOfCustodyResponse> InitializeApheresisAsync(CgtApheresisCreateRequest request, string? callerUserId = null, CancellationToken cancellationToken = default);
    Task<CgtMilestoneResponse> RecordMilestoneAsync(CgtMilestoneCreateRequest request, string? callerUserId = null, CancellationToken cancellationToken = default);
    Task<CgtChainOfCustodyResponse> BindManufacturingBatchAsync(CgtManufacturingBindRequest request, string? callerUserId = null, CancellationToken cancellationToken = default);
    Task<CgtBedsideVerificationResponse> VerifyBedsideAsync(CgtBedsideVerificationRequest request, string callerUserId, CancellationToken cancellationToken = default);
    Task<CgtChainOfCustodyResponse> ExecuteInfusionAsync(CgtInfusionExecuteRequest request, string callerUserId, CancellationToken cancellationToken = default);
    Task<CgtVialReturnReconciliationResponse> LogVialReturnAsync(CgtVialReturnReconciliationRequest request, string? callerUserId = null, CancellationToken cancellationToken = default);
    Task<CgtDestructionCertificateResponse> GenerateDestructionCertificateAsync(CgtDestructionCertificateCreateRequest request, string callerUserId, CancellationToken cancellationToken = default);
    CgtBarcodeVerifyResponse VerifyBarcode(CgtBarcodeVerifyRequest request);
    Task<CgtFullTraceabilityResponse> GetFullTraceabilityAsync(string studyId, string subjectId, CancellationToken cancellationToken = default);
    Task<List<CgtDestructionCertificateResponse>> ListDestructionCertificatesAsync(string studyId, CancellationToken cancellationToken = default);
    Task<List<CgtVialReturnReconciliationResponse>> ListVialReconciliationsAsync(string studyId, CancellationToken cancellationToken = default);
    Task<(string StudyId, string SiteId)?> GetChainOfCustodyScopeAsync(Guid chainOfCustodyId, CancellationToken cancellationToken = default) =>
        Task.FromResult<(string StudyId, string SiteId)?>(null);
    Task<(string StudyId, string SiteId)?> GetSubjectChainOfCustodyScopeAsync(string studyId, string subjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<(string StudyId, string SiteId)?>(null);
}
