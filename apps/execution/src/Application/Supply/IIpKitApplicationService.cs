using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Supply;

public interface IIpKitApplicationService
{
    Task<IpKitScanValidateResponse> ValidateScannedBarcodeAsync(IpKitScanValidateRequest request, CancellationToken ct = default);

    Task<IpKitReconciliationBatchResponse> ProcessBatchReconciliationAsync(IpKitBatchReconciliationRequest request, CancellationToken ct = default);

    Task<IpKitCertificateResponse?> GetCertificateAsync(string certificateId, CancellationToken ct = default);

    Task<List<IpKitInventoryResponse>> GetSiteInventoryAsync(string studyId, string siteId, CancellationToken ct = default);

    Task<List<IpKitInventoryResponse>> SeedSiteInventoryAsync(string studyId, string siteId, List<IpKitInventoryResponse> kits, CancellationToken ct = default);
}
