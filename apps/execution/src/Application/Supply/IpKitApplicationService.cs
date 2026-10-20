using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Application.Supply;

public sealed class IpKitApplicationService(
    ExecutionDbContext dbContext,
    IClock clock,
    IAllocationProtector allocationProtector) : IIpKitApplicationService
{
    private readonly ExecutionDbContext _dbContext = dbContext;
    private readonly IClock _clock = clock;

    public async Task<IpKitScanValidateResponse> ValidateScannedBarcodeAsync(
        IpKitScanValidateRequest request,
        CancellationToken ct = default)
    {
        var existing = await _dbContext.IpKitInventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.KitNumber == request.KitNumber && x.StudyId == request.StudyId, ct);

        if (existing == null)
        {
            string fallbackLot = !string.IsNullOrWhiteSpace(request.ScannedLotNumber)
                ? request.ScannedLotNumber
                : $"LOT-STD-{(Math.Abs(request.KitNumber.GetHashCode()) % 1000):D3}";

            return new IpKitScanValidateResponse(
                KitNumber: request.KitNumber,
                ExpectedLotNumber: fallbackLot,
                ScannedLotNumber: request.ScannedLotNumber,
                IsLotMatch: string.IsNullOrEmpty(request.ScannedLotNumber) || request.ScannedLotNumber == fallbackLot,
                IsValid: true,
                Status: "UNKNOWN_ACTIVE_AD_HOC",
                Message: "Barcode validated (ad-hoc scanned item)");
        }

        bool isLotMatch = string.IsNullOrWhiteSpace(request.ScannedLotNumber)
            || string.Equals(request.ScannedLotNumber, existing.LotNumber, StringComparison.OrdinalIgnoreCase);

        return new IpKitScanValidateResponse(
            KitNumber: existing.KitNumber,
            ExpectedLotNumber: existing.LotNumber,
            ScannedLotNumber: request.ScannedLotNumber,
            IsLotMatch: isLotMatch,
            IsValid: isLotMatch,
            Status: existing.Status,
            Message: isLotMatch ? "Barcode validated successfully" : $"Lot discrepancy: Expected '{existing.LotNumber}', scanned '{request.ScannedLotNumber}'");
    }

    public async Task<IpKitReconciliationBatchResponse> ProcessBatchReconciliationAsync(
        IpKitBatchReconciliationRequest request,
        CancellationToken ct = default)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw new ArgumentException("Reconciliation batch must contain at least one kit item.", nameof(request));
        }

        // One unit of work inside the execution strategy; a transient failure re-runs it whole (#5383).
        return await _dbContext.ExecuteInTransactionAsync(async _ =>
        {
            var now = _clock.UtcNow;
            string batchGuidStr = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            string batchNumber = $"KIT-RECON-{now:yyyyMMdd}-{batchGuidStr}";
            Guid batchId = Guid.NewGuid();
            string certId = $"CERT-RECON-{now:yyyyMMdd}-{batchGuidStr}";
            string processedBy = request.ProcessedBy ?? "site_pharmacist";

            int quarantinedCount = 0;
            var itemResponses = new List<IpKitItemResponse>();
            var kitNumbers = new List<string>();

            foreach (var item in request.Items)
            {
                kitNumbers.Add(item.KitNumber);
                bool isQuarantined = string.Equals(item.Disposition, "QUARANTINED_FOR_DESTRUCTION", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(item.Disposition, "QUARANTINED", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(item.DispositionReason, "DAMAGED_SEAL", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(item.DispositionReason, "EXPIRED", StringComparison.OrdinalIgnoreCase);

                if (isQuarantined)
                {
                    quarantinedCount++;
                }

                string effectiveDisposition = isQuarantined ? "QUARANTINED_FOR_DESTRUCTION" : item.Disposition;

                var inventoryItem = await _dbContext.IpKitInventoryItems
                    .FirstOrDefaultAsync(x => x.KitNumber == item.KitNumber && x.StudyId == request.StudyId, ct);

                if (inventoryItem == null)
                {
                    inventoryItem = new IpKitInventoryItem(
                        id: Guid.NewGuid(),
                        kitNumber: item.KitNumber,
                        studyId: request.StudyId,
                        siteId: request.SiteId,
                        lotNumber: !string.IsNullOrWhiteSpace(item.LotNumber) ? item.LotNumber : item.ExpectedLotNumber,
                        expirationDate: now.AddYears(1),
                        status: effectiveDisposition,
                        residualVolumeMl: item.ResidualVolumeMl,
                        residualVolumePercentage: item.ResidualVolumePercentage,
                        createdBy: processedBy,
                        reasonForChange: request.ReasonForChange);

                    _dbContext.IpKitInventoryItems.Add(inventoryItem);
                }
                else
                {
                    inventoryItem.ReconcileStatus(
                        newStatus: effectiveDisposition,
                        dispositionReason: item.DispositionReason,
                        residualVolumeMl: item.ResidualVolumeMl,
                        residualVolumePercentage: item.ResidualVolumePercentage,
                        batchId: batchId.ToString(),
                        reconciledBy: processedBy,
                        reconciledAt: now,
                        reasonForChange: request.ReasonForChange);
                }

                itemResponses.Add(new IpKitItemResponse(
                    KitNumber: item.KitNumber,
                    LotNumber: item.LotNumber,
                    ExpectedLotNumber: item.ExpectedLotNumber,
                    ResidualVolumeMl: item.ResidualVolumeMl,
                    ResidualVolumePercentage: item.ResidualVolumePercentage,
                    Disposition: effectiveDisposition,
                    DispositionReason: item.DispositionReason,
                    IsQuarantined: isQuarantined));
            }

            string itemsJson = JsonSerializer.Serialize(itemResponses);
            string kitNumbersJson = JsonSerializer.Serialize(kitNumbers);

            var batchRecord = new IpKitReconciliationBatch(
                id: batchId,
                batchNumber: batchNumber,
                studyId: request.StudyId,
                siteId: request.SiteId,
                processedBy: processedBy,
                processedAt: now,
                totalKits: request.Items.Count,
                quarantinedKits: quarantinedCount,
                certificateId: certId,
                itemsJson: itemsJson,
                notes: request.Notes,
                createdBy: processedBy,
                reasonForChange: request.ReasonForChange);

            _dbContext.IpKitReconciliationBatches.Add(batchRecord);

            var summary = new
            {
                total_kits = request.Items.Count,
                quarantined_kits = quarantinedCount,
                returned_to_site = request.Items.Count(i => string.Equals(i.Disposition, "RETURNED_TO_SITE", StringComparison.OrdinalIgnoreCase)),
                returned_to_depot = request.Items.Count(i => string.Equals(i.Disposition, "RETURNED_TO_DEPOT", StringComparison.OrdinalIgnoreCase)),
                processed_by = processedBy,
                processed_at = now
            };
            string summaryJson = JsonSerializer.Serialize(summary);

            string signatureRaw = $"{certId}:{batchNumber}:{request.StudyId}:{request.SiteId}:{request.Items.Count}:{now:O}";
            string digitalSignature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signatureRaw)));

            var certificateRecord = new IpKitReconciliationCertificate(
                id: Guid.NewGuid(),
                certificateId: certId,
                batchId: batchId,
                batchNumber: batchNumber,
                studyId: request.StudyId,
                siteId: request.SiteId,
                issuedBy: processedBy,
                issuedAt: now,
                kitNumbersJson: kitNumbersJson,
                summaryJson: summaryJson,
                digitalSignature: digitalSignature,
                createdBy: processedBy,
                reasonForChange: $"Digital certificate issued for batch reconciliation {batchNumber}");

            _dbContext.IpKitReconciliationCertificates.Add(certificateRecord);

            await _dbContext.SaveChangesAsync(ct);

            var certResponse = new IpKitCertificateResponse(
                CertificateId: certId,
                BatchId: batchId.ToString(),
                BatchNumber: batchNumber,
                StudyId: request.StudyId,
                SiteId: request.SiteId,
                IssuedBy: processedBy,
                IssuedAt: now,
                KitNumbers: kitNumbers,
                DigitalSignature: digitalSignature);

            return new IpKitReconciliationBatchResponse(
                BatchId: batchId.ToString(),
                BatchNumber: batchNumber,
                StudyId: request.StudyId,
                SiteId: request.SiteId,
                ProcessedBy: processedBy,
                ProcessedAt: now,
                TotalKits: request.Items.Count,
                QuarantinedKits: quarantinedCount,
                CertificateId: certId,
                Items: itemResponses,
                Certificate: certResponse);
        }, cancellationToken: ct);
    }

    public async Task<IpKitCertificateResponse?> GetCertificateAsync(string certificateId, CancellationToken ct = default)
    {
        var cert = await _dbContext.IpKitReconciliationCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CertificateId == certificateId, ct);

        if (cert == null) return null;

        var kitNumbers = JsonSerializer.Deserialize<List<string>>(cert.KitNumbersJson) ?? [];

        return new IpKitCertificateResponse(
            CertificateId: cert.CertificateId,
            BatchId: cert.BatchId.ToString(),
            BatchNumber: cert.BatchNumber,
            StudyId: cert.StudyId,
            SiteId: cert.SiteId,
            IssuedBy: cert.IssuedBy,
            IssuedAt: cert.IssuedAt,
            KitNumbers: kitNumbers,
            DigitalSignature: cert.DigitalSignature);
    }

    public async Task<List<IpKitInventoryResponse>> GetSiteInventoryAsync(string studyId, string siteId, CancellationToken ct = default)
    {
        var items = await _dbContext.IpKitInventoryItems
            .AsNoTracking()
            .Where(x => x.StudyId == studyId && x.SiteId == siteId)
            .ToListAsync(ct);

        return items.Select(x => new IpKitInventoryResponse(
            KitNumber: x.KitNumber,
            StudyId: x.StudyId,
            SiteId: x.SiteId,
            LotNumber: x.LotNumber,
            ExpirationDate: x.ExpirationDate,
            Status: x.Status,
            ResidualVolumeMl: x.ResidualVolumeMl,
            ResidualVolumePercentage: x.ResidualVolumePercentage)).ToList();
    }

    public async Task<List<IpKitInventoryResponse>> SeedSiteInventoryAsync(string studyId, string siteId, List<IpKitSeedItem> kits, CancellationToken ct = default)
    {
        var results = new List<IpKitInventoryResponse>();
        foreach (var k in kits)
        {
            var existing = await _dbContext.IpKitInventoryItems
                .FirstOrDefaultAsync(x => x.KitNumber == k.KitNumber && x.StudyId == studyId, ct);

            var expiration = k.ExpirationDate != default ? k.ExpirationDate : _clock.UtcNow.AddMonths(12);
            string status = k.Status ?? IpKitInventoryItem.ActiveAtSiteStatus;
            if (existing == null)
            {
                var item = new IpKitInventoryItem(
                    id: Guid.NewGuid(),
                    kitNumber: k.KitNumber,
                    studyId: studyId,
                    siteId: siteId,
                    lotNumber: k.LotNumber,
                    expirationDate: expiration,
                    status: status,
                    residualVolumeMl: k.ResidualVolumeMl,
                    residualVolumePercentage: k.ResidualVolumePercentage,
                    createdBy: "seed_script",
                    reasonForChange: "Seeded IP kit inventory");

                // The arm label is encrypted under the randomisation key and bound to this kit (#5607).
                if (!string.IsNullOrWhiteSpace(k.TreatmentArm))
                {
                    item.LabelTreatment(allocationProtector.Protect(k.TreatmentArm.Trim(), IpKitInventoryItem.ArmPurpose(studyId, k.KitNumber)));
                }

                _dbContext.IpKitInventoryItems.Add(item);
            }

            results.Add(new IpKitInventoryResponse(k.KitNumber, studyId, siteId, k.LotNumber, expiration, status,
                k.ResidualVolumeMl, k.ResidualVolumePercentage));
        }

        await _dbContext.SaveChangesAsync(ct);
        return results;
    }
}
