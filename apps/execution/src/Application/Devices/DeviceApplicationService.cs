using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Devices;
using Cadence.Apps.Execution.Domain.Devices.Errors;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Kernel.Time;

using MassTransit;

namespace Cadence.Apps.Execution.Application.Devices;

/// <summary>
/// Application service implementing UDI barcode parsing, serialized inventory intake,
/// dispensation gating, deficiency reporting, and automated RTSM quarantine workflows.
/// @req:PRD-DEV-001
/// @req:PRD-DEV-002
/// @req:PRD-DEV-003
/// </summary>
public sealed class DeviceApplicationService(
    IDeviceRepository repository,
    IClock clock,
    ICurrentAuditContextAccessor auditContext,
    IPublishEndpoint publishEndpoint) : IDeviceApplicationService
{
    public ParsedUdiResponse ParseUdi(UdiParseRequest request)
    {
        var result = UdiParser.Parse(request.RawBarcode);
        return MapToParsedResponse(result);
    }

    public async Task<IReadOnlyList<DeviceInventoryResponse>> RegisterInventoryAsync(
        DeviceInventoryCreateRequest request,
        CancellationToken ct = default)
    {
        string currentUserId = !string.IsNullOrEmpty(auditContext.UserId) ? auditContext.UserId : "svc:execution-inventory";
        string reasonForChange = !string.IsNullOrEmpty(request.ReasonForChange) ? request.ReasonForChange : "Investigational medical device inventory intake";

        string udiDi = request.UdiDi;
        string lotNumber = request.LotNumber;
        string? serialNumber = request.SerialNumber;
        string? softwareVer = request.SoftwareVersion;
        string carrierFormat = "GS1_128";

        if (!string.IsNullOrEmpty(request.RawBarcode))
        {
            var parsed = UdiParser.Parse(request.RawBarcode);
            if (parsed.IsValid)
            {
                if (string.IsNullOrEmpty(udiDi)) udiDi = parsed.UdiDi;
                if (string.IsNullOrEmpty(lotNumber)) lotNumber = parsed.LotNumber ?? lotNumber;
                if (string.IsNullOrEmpty(serialNumber)) serialNumber = parsed.SerialNumber ?? serialNumber;
                if (!request.ExpirationDate.HasValue &&
                    !string.IsNullOrEmpty(parsed.ExpirationDate) &&
                    DateTimeOffset.TryParse(parsed.ExpirationDate, CultureInfo.InvariantCulture, out var parsedExp))
                {
                    request = request with { ExpirationDate = parsedExp };
                }
                if (!request.ManufacturingDate.HasValue &&
                    !string.IsNullOrEmpty(parsed.ManufacturingDate) &&
                    DateTimeOffset.TryParse(parsed.ManufacturingDate, CultureInfo.InvariantCulture, out var parsedMfg))
                {
                    request = request with { ManufacturingDate = parsedMfg };
                }
                if (string.IsNullOrEmpty(softwareVer)) softwareVer = parsed.SoftwareVersion;
                carrierFormat = parsed.UdiCarrierFormat;
            }
        }

        var createdItems = new List<DeviceInventoryItem>();
        int count = Math.Max(1, request.Quantity);

        for (int i = 0; i < count; i++)
        {
            string? itemSerial = serialNumber;
            if (count > 1 && !string.IsNullOrEmpty(serialNumber))
            {
                itemSerial = $"{serialNumber}-{(i + 1):D2}";
            }
            else if (count > 1 && string.IsNullOrEmpty(serialNumber))
            {
                itemSerial = $"SN-AUTO-{(i + 1):D4}";
            }

            var item = new DeviceInventoryItem(
                id: Guid.NewGuid(),
                studyId: request.StudyId,
                siteId: request.SiteId,
                brandName: request.BrandName,
                udiDi: udiDi,
                lotNumber: lotNumber,
                serialNumber: itemSerial,
                deviceModel: request.DeviceModel,
                deviceType: request.DeviceType,
                deviceClassification: request.DeviceClassification,
                udiCarrierFormat: carrierFormat,
                expirationDate: request.ExpirationDate,
                manufacturingDate: request.ManufacturingDate,
                softwareVersion: softwareVer,
                createdBy: currentUserId,
                reasonForChange: reasonForChange);

            createdItems.Add(item);
        }

        await repository.AddInventoryRangeAsync(createdItems, ct).ConfigureAwait(false);
        await repository.SaveChangesAsync(ct).ConfigureAwait(false);

        return createdItems.Select(MapToInventoryResponse).ToList();
    }

    public async Task<IReadOnlyList<DeviceInventoryResponse>> ListInventoryAsync(
        string studyId,
        string? siteId,
        string? status,
        string? udiDi,
        string? lotNumber,
        string? serialNumber,
        CancellationToken ct = default)
    {
        var items = await repository.ListInventoryAsync(studyId, siteId, status, udiDi, lotNumber, serialNumber, ct).ConfigureAwait(false);
        return items.Select(MapToInventoryResponse).ToList();
    }

    public async Task<DeviceDispensationResponse> DispenseDeviceAsync(
        DeviceDispensationRequest request,
        CancellationToken ct = default)
    {
        string currentUserId = !string.IsNullOrEmpty(auditContext.UserId) ? auditContext.UserId : "svc:execution-dispensation";

        DeviceInventoryItem? device = null;

        if (!string.IsNullOrEmpty(request.DeviceInventoryId) && Guid.TryParse(request.DeviceInventoryId, out var devGuid))
        {
            device = await repository.GetInventoryByIdAsync(devGuid, ct).ConfigureAwait(false);
        }
        else if (!string.IsNullOrEmpty(request.ScannedBarcode))
        {
            var parsed = UdiParser.Parse(request.ScannedBarcode);
            device = await repository.GetInventoryBySerialOrDiAsync(request.StudyId, parsed.UdiDi, parsed.SerialNumber, parsed.LotNumber, ct).ConfigureAwait(false);
        }
        else if (!string.IsNullOrEmpty(request.SerialNumber))
        {
            var items = await repository.ListInventoryAsync(request.StudyId, request.SiteId, null, null, null, request.SerialNumber, ct).ConfigureAwait(false);
            device = items.Count > 0 ? items[0] : null;
        }

        if (device is null)
        {
            throw DeviceErrors.NotFound(request.DeviceInventoryId ?? request.SerialNumber ?? request.ScannedBarcode ?? "UNKNOWN");
        }

        if (device.Status == "QUARANTINED" || device.IsDispensationBlocked)
        {
            throw DeviceErrors.Quarantined(device.Id.ToString(), device.QuarantineReason ?? "Device is under active quality quarantine.");
        }

        if (device.IsExpired(clock.UtcNow))
        {
            throw DeviceErrors.Expired(device.Id.ToString(), device.ExpirationDate!.Value);
        }

        if (device.Status == "DISPENSED")
        {
            throw DeviceErrors.AlreadyDispensed(device.Id.ToString());
        }

        device.Dispense(request.SubjectId, currentUserId, clock.UtcNow);
        await repository.SaveChangesAsync(ct).ConfigureAwait(false);

        var deviceResp = MapToInventoryResponse(device);
        return new DeviceDispensationResponse(
            Status: "SUCCESS",
            Message: $"Device '{device.DeviceIdentifier}' successfully dispensed to subject '{request.SubjectId}'.",
            Device: deviceResp,
            DispensedAt: clock.UtcNow);
    }

    public async Task<DeviceDeficiencyResponse> LogDeficiencyAsync(
        DeviceDeficiencyCreateRequest request,
        CancellationToken ct = default)
    {
        string currentUserId = !string.IsNullOrEmpty(auditContext.UserId) ? auditContext.UserId : "svc:execution-deficiency";
        DateTimeOffset now = clock.UtcNow;

        string? safetyCaseId = null;
        string? safetyClassification = null;

        if (request.CouldLeadToSade || string.Equals(request.DeficiencyType, "SADE", StringComparison.OrdinalIgnoreCase))
        {
            safetyCaseId = $"CASE-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()}";
            safetyClassification = "EXPEDITED_SADE_INVESTIGATION";
        }

        var deficiency = new DeviceDeficiency(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            siteId: request.SiteId,
            udiDi: request.UdiDi,
            lotNumber: request.LotNumber,
            deficiencyDescription: request.DeficiencyDescription,
            incidentDate: request.IncidentDate,
            awarenessDate: request.AwarenessDate,
            subjectId: request.SubjectId,
            deviceInventoryId: request.DeviceInventoryId,
            serialNumber: request.SerialNumber,
            deviceName: request.DeviceName,
            deficiencyType: request.DeficiencyType,
            couldLeadToSade: request.CouldLeadToSade,
            patientInvolved: request.PatientInvolved,
            healthImpact: request.HealthImpact,
            actionTakenDevice: request.ActionTakenDevice,
            deviceReturnedToSponsor: request.DeviceReturnedToSponsor,
            returnTrackingNumber: request.ReturnTrackingNumber,
            quarantineScope: request.QuarantineScope,
            quarantineTriggered: true,
            safetyCaseId: safetyCaseId,
            safetyClassification: safetyClassification);

        await repository.AddDeficiencyAsync(deficiency, ct).ConfigureAwait(false);

        // Quarantine matching inventory items
        var matchingDevices = await repository.FindMatchingDevicesForQuarantineAsync(
            request.StudyId,
            request.SiteId,
            request.QuarantineScope,
            request.UdiDi,
            request.LotNumber,
            request.SerialNumber,
            request.DeviceInventoryId,
            ct).ConfigureAwait(false);

        string quarantineReason = $"Automated quarantine: {request.DeficiencyType} - {request.DeficiencyDescription}";

        foreach (var dev in matchingDevices)
        {
            dev.Quarantine(quarantineReason, currentUserId, now);
        }

        var quarantineRecord = new DeviceQuarantineRecord(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            siteId: request.SiteId,
            reason: quarantineReason,
            scope: request.QuarantineScope,
            deficiencyId: deficiency.Id.ToString(),
            deviceInventoryId: request.DeviceInventoryId,
            lotNumber: request.LotNumber,
            serialNumber: request.SerialNumber,
            quarantinedBy: currentUserId,
            quarantinedAt: now);

        await repository.AddQuarantineRecordAsync(quarantineRecord, ct).ConfigureAwait(false);

        // Emit outbox events
        await publishEndpoint.Publish(new DeviceDeficiencyReportedEvent(
            DeficiencyId: deficiency.Id,
            StudyId: deficiency.StudyId,
            SiteId: deficiency.SiteId,
            SubjectId: deficiency.SubjectId,
            UdiDi: deficiency.UdiDi,
            LotNumber: deficiency.LotNumber,
            SerialNumber: deficiency.SerialNumber,
            DeficiencyType: deficiency.DeficiencyType,
            DeficiencyDescription: deficiency.DeficiencyDescription,
            CouldLeadToSade: deficiency.CouldLeadToSade,
            SafetyCaseId: deficiency.SafetyCaseId,
            ReportedAt: now,
            ReportedBy: currentUserId), ct).ConfigureAwait(false);

        await publishEndpoint.Publish(new DeviceQuarantinedEvent(
            QuarantineId: quarantineRecord.Id,
            StudyId: quarantineRecord.StudyId,
            SiteId: quarantineRecord.SiteId,
            Scope: quarantineRecord.Scope,
            UdiDi: deficiency.UdiDi,
            LotNumber: deficiency.LotNumber,
            SerialNumber: deficiency.SerialNumber,
            Reason: quarantineReason,
            QuarantinedAt: now,
            QuarantinedBy: currentUserId), ct).ConfigureAwait(false);

        await repository.SaveChangesAsync(ct).ConfigureAwait(false);

        return MapToDeficiencyResponse(deficiency);
    }

    public async Task<IReadOnlyList<DeviceDeficiencyResponse>> ListDeficienciesAsync(
        string studyId,
        string? siteId,
        string? status,
        CancellationToken ct = default)
    {
        var list = await repository.ListDeficienciesAsync(studyId, siteId, status, ct).ConfigureAwait(false);
        return list.Select(MapToDeficiencyResponse).ToList();
    }

    public async Task<DeviceDeficiencyResponse?> GetDeficiencyByIdAsync(Guid id, CancellationToken ct = default)
    {
        var item = await repository.GetDeficiencyByIdAsync(id, ct).ConfigureAwait(false);
        return item is null ? null : MapToDeficiencyResponse(item);
    }

    public async Task<DeviceQuarantineResponse> InitiateQuarantineAsync(
        DeviceQuarantineCreateRequest request,
        CancellationToken ct = default)
    {
        string currentUserId = !string.IsNullOrEmpty(auditContext.UserId) ? auditContext.UserId : "svc:execution-quarantine";
        DateTimeOffset now = clock.UtcNow;

        var matchingDevices = await repository.FindMatchingDevicesForQuarantineAsync(
            request.StudyId,
            request.SiteId,
            request.Scope,
            udiDi: string.Empty,
            lotNumber: request.LotNumber ?? string.Empty,
            serialNumber: request.SerialNumber,
            deviceInventoryId: request.DeviceInventoryId,
            ct).ConfigureAwait(false);

        foreach (var dev in matchingDevices)
        {
            dev.Quarantine(request.Reason, currentUserId, now);
        }

        var record = new DeviceQuarantineRecord(
            id: Guid.NewGuid(),
            studyId: request.StudyId,
            siteId: request.SiteId,
            reason: request.Reason,
            scope: request.Scope,
            deviceInventoryId: request.DeviceInventoryId,
            lotNumber: request.LotNumber,
            serialNumber: request.SerialNumber,
            quarantinedBy: currentUserId,
            quarantinedAt: now);

        await repository.AddQuarantineRecordAsync(record, ct).ConfigureAwait(false);

        await publishEndpoint.Publish(new DeviceQuarantinedEvent(
            QuarantineId: record.Id,
            StudyId: record.StudyId,
            SiteId: record.SiteId,
            Scope: record.Scope,
            UdiDi: null,
            LotNumber: record.LotNumber,
            SerialNumber: record.SerialNumber,
            Reason: record.Reason,
            QuarantinedAt: now,
            QuarantinedBy: currentUserId), ct).ConfigureAwait(false);

        await repository.SaveChangesAsync(ct).ConfigureAwait(false);

        return MapToQuarantineResponse(record);
    }

    public async Task<IReadOnlyList<DeviceQuarantineResponse>> ListQuarantinesAsync(
        string studyId,
        string? siteId,
        string? status,
        CancellationToken ct = default)
    {
        var list = await repository.ListQuarantinesAsync(studyId, siteId, status, ct).ConfigureAwait(false);
        return list.Select(MapToQuarantineResponse).ToList();
    }

    public async Task<DeviceQuarantineResponse> ReleaseQuarantineAsync(
        DeviceQuarantineReleaseRequest request,
        CancellationToken ct = default)
    {
        string currentUserId = !string.IsNullOrEmpty(auditContext.UserId) ? auditContext.UserId : "svc:execution-quarantine";
        if (!Guid.TryParse(request.QuarantineId, out var qGuid))
        {
            throw new KeyNotFoundException($"Quarantine record '{request.QuarantineId}' not found.");
        }

        var record = await repository.GetQuarantineByIdAsync(qGuid, ct).ConfigureAwait(false);
        if (record is null)
        {
            throw new KeyNotFoundException($"Quarantine record '{request.QuarantineId}' not found.");
        }

        record.Release(currentUserId, request.ReleaseJustification, clock.UtcNow);

        // Find associated devices and release them
        var matchingDevices = await repository.FindMatchingDevicesForQuarantineAsync(
            record.StudyId,
            record.SiteId,
            record.Scope,
            udiDi: string.Empty,
            lotNumber: record.LotNumber ?? string.Empty,
            serialNumber: record.SerialNumber,
            deviceInventoryId: record.DeviceInventoryId,
            ct).ConfigureAwait(false);

        foreach (var dev in matchingDevices)
        {
            dev.ReleaseQuarantine(currentUserId);
        }

        await repository.SaveChangesAsync(ct).ConfigureAwait(false);
        return MapToQuarantineResponse(record);
    }

    public async Task<DeviceInvestigationSummaryResponse> GetInvestigationSummaryAsync(
        string studyId,
        CancellationToken ct = default)
    {
        var devices = await repository.ListInventoryAsync(studyId, null, null, null, null, null, ct).ConfigureAwait(false);
        var deficiencies = await repository.ListDeficienciesAsync(studyId, null, null, ct).ConfigureAwait(false);
        var quarantines = await repository.ListQuarantinesAsync(studyId, null, null, ct).ConfigureAwait(false);

        int totalDevices = devices.Count;
        int availableDevices = devices.Count(d => d.Status == "AVAILABLE");
        int dispensedDevices = devices.Count(d => d.Status == "DISPENSED");
        int quarantinedDevices = devices.Count(d => d.Status == "QUARANTINED");
        int totalDeficiencies = deficiencies.Count;
        int openSafetyCases = deficiencies.Count(d => !string.IsNullOrEmpty(d.SafetyCaseId) && d.InvestigationStatus != "CLOSED");
        int sadeCount = deficiencies.Count(d => d.CouldLeadToSade);
        int activeQuarantines = quarantines.Count(q => q.Status == "ACTIVE");

        return new DeviceInvestigationSummaryResponse(
            StudyId: studyId,
            TotalDevices: totalDevices,
            AvailableDevices: availableDevices,
            DispensedDevices: dispensedDevices,
            QuarantinedDevices: quarantinedDevices,
            TotalDeficiencies: totalDeficiencies,
            OpenSafetyCases: openSafetyCases,
            SadeCount: sadeCount,
            ActiveQuarantines: activeQuarantines);
    }

    private static ParsedUdiResponse MapToParsedResponse(ParsedUdiResult result) => new(
        RawBarcode: result.RawBarcode,
        UdiCarrierFormat: result.UdiCarrierFormat,
        UdiDi: result.UdiDi,
        LotNumber: result.LotNumber,
        SerialNumber: result.SerialNumber,
        ExpirationDate: result.ExpirationDate,
        ManufacturingDate: result.ManufacturingDate,
        SoftwareVersion: result.SoftwareVersion,
        IsValid: result.IsValid,
        ValidationErrors: result.ValidationErrors);

    private static DeviceInventoryResponse MapToInventoryResponse(DeviceInventoryItem d) => new(
        Id: d.Id.ToString(),
        StudyId: d.StudyId,
        SiteId: d.SiteId,
        DeviceIdentifier: d.DeviceIdentifier,
        BrandName: d.BrandName,
        DeviceModel: d.DeviceModel,
        DeviceType: d.DeviceType,
        DeviceClassification: d.DeviceClassification,
        UdiDi: d.UdiDi,
        UdiPi: d.UdiPi,
        UdiCarrierFormat: d.UdiCarrierFormat,
        LotNumber: d.LotNumber,
        SerialNumber: d.SerialNumber,
        ExpirationDate: d.ExpirationDate,
        ManufacturingDate: d.ManufacturingDate,
        SoftwareVersion: d.SoftwareVersion,
        Status: d.Status,
        IsDispensationBlocked: d.IsDispensationBlocked,
        AllocatedSubjectId: d.AllocatedSubjectId,
        DispensedAt: d.DispensedAt,
        DispensedBy: d.DispensedBy,
        QuarantinedAt: d.QuarantinedAt,
        QuarantinedBy: d.QuarantinedBy,
        QuarantineReason: d.QuarantineReason,
        CreatedAt: d.CreatedAt,
        CreatedBy: d.CreatedBy,
        ReasonForChange: d.ReasonForChange,
        VersionIndex: d.VersionIndex);

    private static DeviceDeficiencyResponse MapToDeficiencyResponse(DeviceDeficiency d) => new(
        Id: d.Id.ToString(),
        StudyId: d.StudyId,
        SiteId: d.SiteId,
        SubjectId: d.SubjectId,
        DeviceInventoryId: d.DeviceInventoryId,
        UdiDi: d.UdiDi,
        LotNumber: d.LotNumber,
        SerialNumber: d.SerialNumber,
        DeviceName: d.DeviceName,
        DeficiencyType: d.DeficiencyType,
        DeficiencyDescription: d.DeficiencyDescription,
        IncidentDate: d.IncidentDate,
        AwarenessDate: d.AwarenessDate,
        CouldLeadToSade: d.CouldLeadToSade,
        PatientInvolved: d.PatientInvolved,
        HealthImpact: d.HealthImpact,
        ActionTakenDevice: d.ActionTakenDevice,
        DeviceReturnedToSponsor: d.DeviceReturnedToSponsor,
        ReturnTrackingNumber: d.ReturnTrackingNumber,
        RootCauseAnalysis: d.RootCauseAnalysis,
        SponsorEvaluation: d.SponsorEvaluation,
        QuarantineTriggered: d.QuarantineTriggered,
        QuarantineScope: d.QuarantineScope,
        SafetyCaseId: d.SafetyCaseId,
        SafetyClassification: d.SafetyClassification,
        InvestigationStatus: d.InvestigationStatus,
        CreatedAt: d.CreatedAt,
        CreatedBy: d.CreatedBy,
        ReasonForChange: d.ReasonForChange,
        VersionIndex: d.VersionIndex);

    private static DeviceQuarantineResponse MapToQuarantineResponse(DeviceQuarantineRecord q) => new(
        Id: q.Id.ToString(),
        StudyId: q.StudyId,
        SiteId: q.SiteId,
        DeficiencyId: q.DeficiencyId,
        DeviceInventoryId: q.DeviceInventoryId,
        Scope: q.Scope,
        LotNumber: q.LotNumber,
        SerialNumber: q.SerialNumber,
        Reason: q.Reason,
        Status: q.Status,
        QuarantinedAt: q.QuarantinedAt,
        QuarantinedBy: q.QuarantinedBy,
        ReleasedAt: q.ReleasedAt,
        ReleasedBy: q.ReleasedBy,
        ReleaseJustification: q.ReleaseJustification,
        CreatedAt: q.CreatedAt,
        CreatedBy: q.CreatedBy,
        ReasonForChange: q.ReasonForChange,
        VersionIndex: q.VersionIndex);
}
