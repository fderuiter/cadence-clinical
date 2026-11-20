using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Cgt;
using Cadence.Apps.Execution.Domain.Cgt.Errors;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Supply;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Application.Cgt;

public sealed class CgtApplicationService(
    ExecutionDbContext dbContext,
    IClock clock) : ICgtApplicationService
{
    private readonly ExecutionDbContext _dbContext = dbContext;
    private readonly IClock _clock = clock;

    public async Task<CgtChainOfCustodyResponse> InitializeApheresisAsync(CgtApheresisCreateRequest request, CancellationToken cancellationToken = default)
    {
        string din = request.Din ?? string.Empty;
        if (string.IsNullOrWhiteSpace(din))
        {
            din = Isbt128Engine.GenerateDin(
                facilityCode: request.CollectionSiteId,
                year: _clock.UtcNow.Year % 100);
        }
        else if (!Isbt128Engine.ValidateDin(din))
        {
            throw new InvalidDinException($"Invalid ISBT 128 Donation Identification Number (DIN): '{din}'. Failed ISO 7064 Mod 37-2 verification.");
        }

        var coc = new CgtChainOfCustody(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            siteId: request.CollectionSiteId,
            subjectId: request.SubjectId,
            din: din,
            collectionSiteId: request.CollectionSiteId,
            harvestedAt: request.CollectionTimestamp ?? _clock.UtcNow,
            createdBy: request.CustodianId ?? "system",
            reasonForChange: request.Notes ?? "Apheresis harvest initialized");

        _dbContext.CGTChainsOfCustody.Add(coc);

        var milestone = new CgtMilestoneEvent(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            chainOfCustodyId: coc.Id,
            stage: "APHERESIS",
            milestoneType: "HARVEST_COMPLETED",
            timestamp: request.CollectionTimestamp ?? _clock.UtcNow,
            locationName: "Collection Facility",
            facilityId: request.CollectionSiteId,
            custodianId: request.CustodianId,
            custodianName: request.CustodianName,
            custodianRole: request.CustodianRole,
            notes: request.Notes,
            createdBy: request.CustodianId ?? "system",
            reasonForChange: "Apheresis harvest milestone logged");

        _dbContext.CGTMilestoneEvents.Add(milestone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapCocToResponse(coc);
    }

    public async Task<CgtMilestoneResponse> RecordMilestoneAsync(CgtMilestoneCreateRequest request, CancellationToken cancellationToken = default)
    {
        var coc = await _dbContext.CGTChainsOfCustody
            .FirstOrDefaultAsync(x => x.Id == request.ChainOfCustodyId, cancellationToken)
            ?? throw new ChainOfCustodyNotFoundException($"Chain of Custody record '{request.ChainOfCustodyId}' not found.");

        DateTimeOffset timestamp = request.Timestamp ?? _clock.UtcNow;
        bool isExcursion = false;
        string? tempStatus = "OPTIMAL";

        if (request.TemperatureCelsius.HasValue)
        {
            var zone = TemperatureZoneClassifier.ClassifyZone(request.TemperatureCelsius.Value);
            tempStatus = zone.ToString();
            if (request.TemperatureCelsius.Value > -70.0 || request.TemperatureCelsius.Value < -196.0)
            {
                isExcursion = true;
                tempStatus = "EXCURSION";
            }
        }

        coc.RecordMilestone(
            stage: request.Stage,
            milestoneType: request.MilestoneType,
            tempCelsius: request.TemperatureCelsius,
            isExcursion: isExcursion,
            updatedBy: request.CustodianId ?? "system",
            reasonForChange: request.Notes ?? $"Milestone {request.MilestoneType} logged");

        var milestone = new CgtMilestoneEvent(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            chainOfCustodyId: coc.Id,
            stage: request.Stage,
            milestoneType: request.MilestoneType,
            timestamp: timestamp,
            locationName: request.LocationName,
            facilityId: request.FacilityId,
            latitude: request.Latitude,
            longitude: request.Longitude,
            temperatureCelsius: request.TemperatureCelsius,
            temperatureStatus: tempStatus,
            custodianId: request.CustodianId,
            custodianName: request.CustodianName,
            custodianRole: request.CustodianRole,
            carrier: request.Carrier,
            trackingNumber: request.TrackingNumber,
            sealIntact: request.SealIntact,
            sealId: request.SealId,
            dryShipperId: request.DryShipperId,
            notes: request.Notes,
            createdBy: request.CustodianId ?? "system",
            reasonForChange: $"Milestone {request.MilestoneType} logged");

        _dbContext.CGTMilestoneEvents.Add(milestone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapMilestoneToResponse(milestone);
    }

    public async Task<CgtChainOfCustodyResponse> BindManufacturingBatchAsync(CgtManufacturingBindRequest request, CancellationToken cancellationToken = default)
    {
        var coc = await _dbContext.CGTChainsOfCustody
            .FirstOrDefaultAsync(x => x.Id == request.ChainOfCustodyId, cancellationToken)
            ?? throw new ChainOfCustodyNotFoundException($"Chain of Custody record '{request.ChainOfCustodyId}' not found.");

        coc.BindManufacturingBatch(
            batchId: request.BatchId,
            manufacturingFacilityId: request.ManufacturingFacilityId,
            updatedBy: request.QualifiedPersonSignoffId ?? "system",
            reasonForChange: request.Notes ?? $"Manufacturing batch '{request.BatchId}' bound");

        var milestone = new CgtMilestoneEvent(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            chainOfCustodyId: coc.Id,
            stage: "MANUFACTURING",
            milestoneType: "BATCH_SYNTHESIS_BOUND",
            timestamp: request.SynthesisEnd ?? _clock.UtcNow,
            facilityId: request.ManufacturingFacilityId,
            custodianId: request.QualifiedPersonSignoffId,
            notes: request.Notes,
            createdBy: request.QualifiedPersonSignoffId ?? "system",
            reasonForChange: $"Batch '{request.BatchId}' bound");

        _dbContext.CGTMilestoneEvents.Add(milestone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapCocToResponse(coc);
    }

    public async Task<CgtBedsideVerificationResponse> VerifyBedsideAsync(CgtBedsideVerificationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.Equals(request.PrimaryVerifierId?.Trim(), request.SecondaryVerifierId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DuplicateVerifierException(
                $"GxP Compliance Violation: Bedside verification requires dual signatures from two distinct staff members. " +
                $"Primary verifier ({request.PrimaryVerifierId}) and secondary verifier ({request.SecondaryVerifierId}) cannot be the same user.");
        }

        var (isValid, failureReason, _) = CgtBarcodeEngine.VerifyCryptoBarcode(
            barcodePayload: request.BagBarcodePayload,
            expectedSubjectId: request.SubjectId,
            expectedDin: request.Din,
            expectedBatchId: request.BatchId);

        if (!isValid)
        {
            throw new BarcodeVerificationException($"Bedside verification failed: {failureReason}");
        }

        if (!string.Equals(request.PatientWristbandId?.Trim(), request.SubjectId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new BarcodeVerificationException(
                $"Bedside verification failed: patient wristband ID '{request.PatientWristbandId}' does not match expected subject ID '{request.SubjectId}'.");
        }

        DateTimeOffset now = _clock.UtcNow;
        string unlockToken = $"UNLOCK-{Guid.NewGuid():N}"[..24].ToUpperInvariant();

        var verification = new CgtBedsideVerification(
            id: Guid.NewGuid(),
            studyId: request.StudyId ?? "",
            siteId: request.SiteId ?? "",
            subjectId: request.SubjectId ?? "",
            din: request.Din ?? "",
            batchId: request.BatchId,
            patientWristbandId: request.PatientWristbandId ?? "",
            primaryVerifierId: request.PrimaryVerifierId ?? "",
            primaryVerifierName: request.PrimaryVerifierName,
            primaryVerifierRole: request.PrimaryVerifierRole,
            primarySignatureToken: request.PrimarySignatureToken ?? "",
            secondaryVerifierId: request.SecondaryVerifierId ?? "",
            secondaryVerifierName: request.SecondaryVerifierName,
            secondaryVerifierRole: request.SecondaryVerifierRole,
            secondarySignatureToken: request.SecondarySignatureToken ?? "",
            unlockToken: unlockToken,
            verifiedAt: now,
            isMatch: true,
            notes: request.Notes,
            createdBy: request.PrimaryVerifierId ?? "system",
            reasonForChange: request.Notes ?? "Bedside dual verification completed");

        _dbContext.CGTBedsideVerifications.Add(verification);

        var coc = await _dbContext.CGTChainsOfCustody
            .FirstOrDefaultAsync(x => x.StudyId == request.StudyId && x.SubjectId == request.SubjectId, cancellationToken);

        if (coc != null)
        {
            coc.SetBedsideVerified(unlockToken, request.PrimaryVerifierId ?? "system", "Bedside verification completed");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CgtBedsideVerificationResponse(
            VerificationId: verification.Id,
            StudyId: request.StudyId ?? "",
            SiteId: request.SiteId ?? "",
            SubjectId: request.SubjectId ?? "",
            Din: request.Din ?? "",
            BatchId: request.BatchId,
            PatientWristbandId: request.PatientWristbandId ?? "",
            PrimaryVerifierId: request.PrimaryVerifierId ?? "",
            SecondaryVerifierId: request.SecondaryVerifierId ?? "",
            UnlockToken: unlockToken,
            VerifiedAt: now,
            IsMatch: true,
            Status: "BEDSIDE_VERIFIED");
    }

    public async Task<CgtChainOfCustodyResponse> ExecuteInfusionAsync(CgtInfusionExecuteRequest request, CancellationToken cancellationToken = default)
    {
        var coc = await _dbContext.CGTChainsOfCustody
            .FirstOrDefaultAsync(x => x.Id == request.ChainOfCustodyId, cancellationToken)
            ?? throw new ChainOfCustodyNotFoundException($"Chain of Custody record '{request.ChainOfCustodyId}' not found.");

        if (!string.Equals(coc.UnlockToken, request.UnlockToken, StringComparison.Ordinal) || coc.Status != "BEDSIDE_VERIFIED")
        {
            throw new InvalidOperationException("Infusion locked: valid two-person bedside verification unlock token is required.");
        }

        DateTimeOffset infusedAt = request.InfusionEnd ?? _clock.UtcNow;
        coc.SetInfused(infusedAt, request.PrimaryVerifierId, request.Notes ?? "Drug infusion executed");

        var milestone = new CgtMilestoneEvent(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            chainOfCustodyId: coc.Id,
            stage: "INFUSION",
            milestoneType: "INFUSION_COMPLETED",
            timestamp: infusedAt,
            custodianId: request.PrimaryVerifierId,
            notes: request.Notes,
            createdBy: request.PrimaryVerifierId,
            reasonForChange: "Infusion completed");

        _dbContext.CGTMilestoneEvents.Add(milestone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapCocToResponse(coc);
    }

    public async Task<CgtVialReturnReconciliationResponse> LogVialReturnAsync(CgtVialReturnReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        int totalVials = request.IntactVials + request.UsedVials;
        double adherencePct = totalVials > 0 ? Math.Round((request.UsedVials / (double)totalVials) * 100.0, 2) : 100.0;
        DateTimeOffset now = _clock.UtcNow;

        var recon = new CgtVialReturnReconciliation(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            siteId: request.SiteId,
            subjectId: request.SubjectId,
            kitNumber: request.KitNumber,
            vialIdentifier: request.VialIdentifier,
            returnedQuantity: request.ReturnedQuantity,
            intactVials: request.IntactVials,
            usedVials: request.UsedVials,
            residualVolumeMl: request.ResidualVolumeMl,
            adherencePercentage: adherencePct,
            disposition: "QUARANTINED_FOR_DESTRUCTION",
            loggedBy: request.LoggedBy ?? "system",
            loggedAt: now,
            notes: request.Notes,
            createdBy: request.LoggedBy ?? "system",
            reasonForChange: request.Notes ?? $"Vial return reconciliation logged for kit {request.KitNumber}");

        _dbContext.CGTVialReturnReconciliations.Add(recon);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CgtVialReturnReconciliationResponse(
            Id: recon.Id,
            StudyId: recon.StudyId,
            SiteId: recon.SiteId,
            SubjectId: recon.SubjectId,
            KitNumber: recon.KitNumber,
            VialIdentifier: recon.VialIdentifier,
            ReturnedQuantity: recon.ReturnedQuantity,
            IntactVials: recon.IntactVials,
            UsedVials: recon.UsedVials,
            ResidualVolumeMl: recon.ResidualVolumeMl,
            AdherencePercentage: recon.AdherencePercentage,
            Disposition: recon.Disposition,
            LoggedBy: recon.LoggedBy,
            LoggedAt: recon.LoggedAt,
            CertificateId: recon.CertificateId,
            Notes: recon.Notes);
    }

    public async Task<CgtDestructionCertificateResponse> GenerateDestructionCertificateAsync(CgtDestructionCertificateCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.Equals(request.Witness1Id?.Trim(), request.Witness2Id?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DuplicateWitnessException(
                $"GxP Compliance Violation: Certificate of Destruction requires dual witness signatures from two distinct staff members. " +
                $"Witness 1 ({request.Witness1Id}) and Witness 2 ({request.Witness2Id}) cannot be the same user.");
        }

        DateTimeOffset now = _clock.UtcNow;
        string certNum = $"COD-{now.Year}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        var sealData = new
        {
            certificate_number = certNum,
            study_id = request.StudyId,
            site_id = request.SiteId,
            lot_number = request.LotNumber,
            kit_numbers = request.KitNumbers,
            vial_identifiers = request.VialIdentifiers,
            quantity_destroyed = request.QuantityDestroyed,
            witness_1 = request.Witness1Id,
            witness_2 = request.Witness2Id,
            timestamp = now.ToString("o")
        };

        byte[] sealBytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(sealData)));
        string sealHex = Convert.ToHexString(sealBytes).ToLowerInvariant();

        var cert = new CgtDestructionCertificate(
            id: Guid.NewGuid(),
            studyId: request.StudyId ?? "",
            siteId: request.SiteId ?? "",
            certificateNumber: certNum,
            chainOfCustodyId: request.CocId,
            subjectId: request.SubjectId,
            din: request.Din,
            lotNumber: request.LotNumber ?? "",
            kitNumbers: request.KitNumbers,
            vialIdentifiers: request.VialIdentifiers,
            quantityDestroyed: request.QuantityDestroyed,
            residualVolumeMl: request.ResidualVolumeMl,
            destructionReason: request.DestructionReason ?? "",
            destructionMethod: request.DestructionMethod ?? "",
            destructionFacility: request.DestructionFacility ?? "",
            destructionTimestamp: now,
            witness1Id: request.Witness1Id ?? "",
            witness1Name: request.Witness1Name,
            witness1Role: request.Witness1Role,
            witness1SignatureToken: request.Witness1SignatureToken ?? "",
            witness2Id: request.Witness2Id ?? "",
            witness2Name: request.Witness2Name,
            witness2Role: request.Witness2Role,
            witness2SignatureToken: request.Witness2SignatureToken ?? "",
            cryptographicSeal: sealHex,
            status: "COMPLETED",
            reconciliationNotes: request.Notes,
            createdBy: request.Witness1Id ?? "system",
            reasonForChange: request.DestructionReason ?? "Certificate of Destruction");

        _dbContext.CGTDestructionCertificates.Add(cert);

        if (request.CocId.HasValue)
        {
            var coc = await _dbContext.CGTChainsOfCustody
                .FirstOrDefaultAsync(x => x.Id == request.CocId.Value, cancellationToken);
            if (coc != null)
            {
                coc.SetDestroyed(now, request.Witness1Id ?? "system", request.DestructionReason ?? "Certificate of Destruction");
            }
        }

        if (request.VialIdentifiers != null && request.VialIdentifiers.Count > 0)
        {
            var recons = await _dbContext.CGTVialReturnReconciliations
                .Where(x => x.StudyId == request.StudyId && request.VialIdentifiers.Contains(x.VialIdentifier))
                .ToListAsync(cancellationToken);

            foreach (var recon in recons)
            {
                recon.MarkDestroyed(cert.Id.ToString(), request.Witness1Id ?? "system", "Destroyed under certificate " + certNum);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CgtDestructionCertificateResponse(
            Id: cert.Id,
            CertificateNumber: cert.CertificateNumber,
            StudyId: cert.StudyId,
            SiteId: cert.SiteId,
            ChainOfCustodyId: cert.ChainOfCustodyId,
            SubjectId: cert.SubjectId,
            Din: cert.Din,
            LotNumber: cert.LotNumber,
            KitNumbers: cert.GetKitNumbers(),
            VialIdentifiers: cert.GetVialIdentifiers(),
            QuantityDestroyed: cert.QuantityDestroyed,
            UnitOfMeasure: cert.UnitOfMeasure,
            ResidualVolumeMl: cert.ResidualVolumeMl,
            DestructionReason: cert.DestructionReason,
            DestructionMethod: cert.DestructionMethod,
            DestructionFacility: cert.DestructionFacility,
            DestructionTimestamp: cert.DestructionTimestamp,
            Witness1Id: cert.Witness1Id,
            Witness2Id: cert.Witness2Id,
            CryptographicSeal: cert.CryptographicSeal,
            Status: cert.Status,
            Notes: cert.ReconciliationNotes);
    }

    public CgtBarcodeVerifyResponse VerifyBarcode(CgtBarcodeVerifyRequest request)
    {
        var (isValid, failureReason, decodedData) = CgtBarcodeEngine.VerifyCryptoBarcode(
            barcodePayload: request.BarcodePayload,
            expectedSubjectId: request.ExpectedSubjectId,
            expectedDin: request.ExpectedDin,
            expectedBatchId: request.ExpectedBatchId);

        decodedData.TryGetValue("barcode_id", out var barcodeId);
        decodedData.TryGetValue("study_id", out var studyId);
        decodedData.TryGetValue("site_id", out var siteId);
        decodedData.TryGetValue("subject_id", out var subjectId);
        decodedData.TryGetValue("din", out var din);
        decodedData.TryGetValue("batch_id", out var batchId);

        return new CgtBarcodeVerifyResponse(
            IsValid: isValid,
            FailureReason: failureReason,
            BarcodeId: barcodeId,
            StudyId: studyId,
            SiteId: siteId,
            SubjectId: subjectId,
            Din: din,
            BatchId: batchId);
    }

    public async Task<CgtFullTraceabilityResponse> GetFullTraceabilityAsync(string studyId, string subjectId, CancellationToken cancellationToken = default)
    {
        var coc = await _dbContext.CGTChainsOfCustody
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StudyId == studyId && x.SubjectId == subjectId, cancellationToken);

        List<CgtMilestoneResponse> milestones = [];
        if (coc != null)
        {
            var msEntities = await _dbContext.CGTMilestoneEvents
                .AsNoTracking()
                .Where(x => x.StudyId == studyId && x.ChainOfCustodyId == coc.Id)
                .OrderBy(x => x.Timestamp)
                .ToListAsync(cancellationToken);

            milestones = msEntities.Select(MapMilestoneToResponse).ToList();
        }

        var verification = await _dbContext.CGTBedsideVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StudyId == studyId && x.SubjectId == subjectId, cancellationToken);

        var cert = await _dbContext.CGTDestructionCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StudyId == studyId && x.SubjectId == subjectId, cancellationToken);

        return new CgtFullTraceabilityResponse(
            ChainOfCustody: coc != null ? MapCocToResponse(coc) : null,
            Milestones: milestones,
            BedsideVerification: verification != null ? MapVerificationToResponse(verification) : null,
            DestructionCertificate: cert != null ? MapCertificateToResponse(cert) : null);
    }

    public async Task<List<CgtDestructionCertificateResponse>> ListDestructionCertificatesAsync(string studyId, CancellationToken cancellationToken = default)
    {
        var certs = await _dbContext.CGTDestructionCertificates
            .AsNoTracking()
            .Where(x => x.StudyId == studyId)
            .OrderByDescending(x => x.DestructionTimestamp)
            .ToListAsync(cancellationToken);

        return certs.Select(MapCertificateToResponse).ToList();
    }

    public async Task<List<CgtVialReturnReconciliationResponse>> ListVialReconciliationsAsync(string studyId, CancellationToken cancellationToken = default)
    {
        var recons = await _dbContext.CGTVialReturnReconciliations
            .AsNoTracking()
            .Where(x => x.StudyId == studyId)
            .OrderByDescending(x => x.LoggedAt)
            .ToListAsync(cancellationToken);

        return recons.Select(MapReconciliationToResponse).ToList();
    }

    public async Task<(string StudyId, string SiteId)?> GetChainOfCustodyScopeAsync(
        Guid chainOfCustodyId,
        CancellationToken cancellationToken = default)
    {
        var coc = await _dbContext.CGTChainsOfCustody
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == chainOfCustodyId, cancellationToken);

        return coc is null ? null : (coc.StudyId, coc.SiteId);
    }

    public async Task<(string StudyId, string SiteId)?> GetSubjectChainOfCustodyScopeAsync(
        string studyId,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var coc = await _dbContext.CGTChainsOfCustody
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StudyId == studyId && x.SubjectId == subjectId, cancellationToken);

        return coc is null ? null : (coc.StudyId, coc.SiteId);
    }

    private static CgtChainOfCustodyResponse MapCocToResponse(CgtChainOfCustody coc) =>
        new(
            Id: coc.Id,
            StudyId: coc.StudyId,
            SiteId: coc.SiteId,
            SubjectId: coc.SubjectId,
            Din: coc.Din,
            BatchId: coc.BatchId,
            CollectionSiteId: coc.CollectionSiteId,
            ManufacturingFacilityId: coc.ManufacturingFacilityId,
            Status: coc.Status,
            CurrentStage: coc.CurrentStage,
            CryoTempExcursionCount: coc.CryoTempExcursionCount,
            LowestTempCelsius: coc.LowestTempCelsius,
            UnlockToken: coc.UnlockToken,
            HarvestedAt: coc.HarvestedAt,
            InfusedAt: coc.InfusedAt,
            ReconciledAt: coc.ReconciledAt,
            CreatedAt: coc.CreatedAt,
            CreatedBy: coc.CreatedBy);

    private static CgtMilestoneResponse MapMilestoneToResponse(CgtMilestoneEvent m) =>
        new(
            Id: m.Id,
            StudyId: m.StudyId,
            ChainOfCustodyId: m.ChainOfCustodyId,
            Stage: m.Stage,
            MilestoneType: m.MilestoneType,
            Timestamp: m.Timestamp,
            LocationName: m.LocationName,
            FacilityId: m.FacilityId,
            Latitude: m.Latitude,
            Longitude: m.Longitude,
            TemperatureCelsius: m.TemperatureCelsius,
            TemperatureStatus: m.TemperatureStatus,
            CustodianId: m.CustodianId,
            CustodianName: m.CustodianName,
            CustodianRole: m.CustodianRole,
            Carrier: m.Carrier,
            TrackingNumber: m.TrackingNumber,
            SealIntact: m.SealIntact,
            SealId: m.SealId,
            DryShipperId: m.DryShipperId,
            Notes: m.Notes);

    private static CgtBedsideVerificationResponse MapVerificationToResponse(CgtBedsideVerification v) =>
        new(
            VerificationId: v.Id,
            StudyId: v.StudyId,
            SiteId: v.SiteId,
            SubjectId: v.SubjectId,
            Din: v.Din,
            BatchId: v.BatchId,
            PatientWristbandId: v.PatientWristbandId,
            PrimaryVerifierId: v.PrimaryVerifierId,
            SecondaryVerifierId: v.SecondaryVerifierId,
            UnlockToken: v.UnlockToken,
            VerifiedAt: v.VerifiedAt,
            IsMatch: v.IsMatch,
            Status: "BEDSIDE_VERIFIED");

    private static CgtDestructionCertificateResponse MapCertificateToResponse(CgtDestructionCertificate c) =>
        new(
            Id: c.Id,
            CertificateNumber: c.CertificateNumber,
            StudyId: c.StudyId,
            SiteId: c.SiteId,
            ChainOfCustodyId: c.ChainOfCustodyId,
            SubjectId: c.SubjectId,
            Din: c.Din,
            LotNumber: c.LotNumber,
            KitNumbers: c.GetKitNumbers(),
            VialIdentifiers: c.GetVialIdentifiers(),
            QuantityDestroyed: c.QuantityDestroyed,
            UnitOfMeasure: c.UnitOfMeasure,
            ResidualVolumeMl: c.ResidualVolumeMl,
            DestructionReason: c.DestructionReason,
            DestructionMethod: c.DestructionMethod,
            DestructionFacility: c.DestructionFacility,
            DestructionTimestamp: c.DestructionTimestamp,
            Witness1Id: c.Witness1Id,
            Witness2Id: c.Witness2Id,
            CryptographicSeal: c.CryptographicSeal,
            Status: c.Status,
            Notes: c.ReconciliationNotes);

    private static CgtVialReturnReconciliationResponse MapReconciliationToResponse(CgtVialReturnReconciliation r) =>
        new(
            Id: r.Id,
            StudyId: r.StudyId,
            SiteId: r.SiteId,
            SubjectId: r.SubjectId,
            KitNumber: r.KitNumber,
            VialIdentifier: r.VialIdentifier,
            ReturnedQuantity: r.ReturnedQuantity,
            IntactVials: r.IntactVials,
            UsedVials: r.UsedVials,
            ResidualVolumeMl: r.ResidualVolumeMl,
            AdherencePercentage: r.AdherencePercentage,
            Disposition: r.Disposition,
            LoggedBy: r.LoggedBy,
            LoggedAt: r.LoggedAt,
            CertificateId: r.CertificateId,
            Notes: r.Notes);
}
