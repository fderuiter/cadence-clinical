using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Cgt;

public interface ICgtApplicationService
{
    Task<CgtChainOfCustodyResponse> InitializeApheresisAsync(CgtApheresisCreateRequest request, CancellationToken cancellationToken = default);
    Task<CgtMilestoneResponse> RecordMilestoneAsync(CgtMilestoneCreateRequest request, CancellationToken cancellationToken = default);
    Task<CgtChainOfCustodyResponse> BindManufacturingBatchAsync(CgtManufacturingBindRequest request, CancellationToken cancellationToken = default);
    Task<CgtBedsideVerificationResponse> VerifyBedsideAsync(CgtBedsideVerificationRequest request, CancellationToken cancellationToken = default);
    Task<CgtChainOfCustodyResponse> ExecuteInfusionAsync(CgtInfusionExecuteRequest request, CancellationToken cancellationToken = default);
    Task<CgtVialReturnReconciliationResponse> LogVialReturnAsync(CgtVialReturnReconciliationRequest request, CancellationToken cancellationToken = default);
    Task<CgtDestructionCertificateResponse> GenerateDestructionCertificateAsync(CgtDestructionCertificateCreateRequest request, CancellationToken cancellationToken = default);
    CgtBarcodeVerifyResponse VerifyBarcode(CgtBarcodeVerifyRequest request);
    Task<CgtFullTraceabilityResponse> GetFullTraceabilityAsync(string studyId, string subjectId, CancellationToken cancellationToken = default);
    Task<List<CgtDestructionCertificateResponse>> ListDestructionCertificatesAsync(string studyId, CancellationToken cancellationToken = default);
    Task<List<CgtVialReturnReconciliationResponse>> ListVialReconciliationsAsync(string studyId, CancellationToken cancellationToken = default);
}
