using System;
using System.Collections.Generic;

namespace Cadence.Apps.Execution.Application.Devices;

public record UdiParseRequest(string RawBarcode);

public record ParsedUdiResponse(
    string RawBarcode,
    string UdiCarrierFormat,
    string UdiDi,
    string? LotNumber,
    string? SerialNumber,
    string? ExpirationDate,
    string? ManufacturingDate,
    string? SoftwareVersion,
    bool IsValid,
    IReadOnlyList<string> ValidationErrors);

public record DeviceInventoryCreateRequest(
    string StudyId,
    string SiteId,
    string BrandName,
    string UdiDi,
    string LotNumber,
    string? SerialNumber = null,
    string? DeviceIdentifier = null,
    string? DeviceModel = null,
    string DeviceType = "THERAPEUTIC",
    string DeviceClassification = "CLASS_IIA",
    string? RawBarcode = null,
    DateTimeOffset? ExpirationDate = null,
    DateTimeOffset? ManufacturingDate = null,
    string? SoftwareVersion = null,
    int Quantity = 1,
    string ReasonForChange = "Investigational medical device inventory intake");

public record DeviceInventoryResponse(
    string Id,
    string StudyId,
    string SiteId,
    string DeviceIdentifier,
    string BrandName,
    string? DeviceModel,
    string DeviceType,
    string DeviceClassification,
    string UdiDi,
    string? UdiPi,
    string UdiCarrierFormat,
    string LotNumber,
    string? SerialNumber,
    DateTimeOffset? ExpirationDate,
    DateTimeOffset? ManufacturingDate,
    string? SoftwareVersion,
    string Status,
    bool IsDispensationBlocked,
    string? AllocatedSubjectId,
    DateTimeOffset? DispensedAt,
    string? DispensedBy,
    DateTimeOffset? QuarantinedAt,
    string? QuarantinedBy,
    string? QuarantineReason,
    DateTime CreatedAt,
    string CreatedBy,
    string ReasonForChange,
    long VersionIndex);

public record DeviceDispensationRequest(
    string StudyId,
    string SiteId,
    string SubjectId,
    string? DeviceInventoryId = null,
    string? SerialNumber = null,
    string? ScannedBarcode = null,
    string ReasonForChange = "Investigational device dispensation to participant");

public record DeviceDispensationResponse(
    string Status,
    string Message,
    DeviceInventoryResponse Device,
    DateTimeOffset DispensedAt);

public record DeviceDeficiencyCreateRequest(
    string StudyId,
    string SiteId,
    string UdiDi,
    string LotNumber,
    string DeficiencyDescription,
    DateTimeOffset IncidentDate,
    DateTimeOffset AwarenessDate,
    string? SubjectId = null,
    string? DeviceInventoryId = null,
    string? SerialNumber = null,
    string DeviceName = "Investigational Medical Device",
    string DeficiencyType = "MALFUNCTION",
    bool CouldLeadToSade = false,
    bool PatientInvolved = false,
    string HealthImpact = "NONE",
    string ActionTakenDevice = "QUARANTINED_AT_SITE",
    bool DeviceReturnedToSponsor = false,
    string? ReturnTrackingNumber = null,
    string QuarantineScope = "DEVICE_SERIAL",
    string ReasonForChange = "Report investigational medical device deficiency / malfunction",
    string? SignatureToken = null);

public record DeviceDeficiencyResponse(
    string Id,
    string StudyId,
    string SiteId,
    string? SubjectId,
    string? DeviceInventoryId,
    string UdiDi,
    string LotNumber,
    string? SerialNumber,
    string DeviceName,
    string DeficiencyType,
    string DeficiencyDescription,
    DateTimeOffset IncidentDate,
    DateTimeOffset AwarenessDate,
    bool CouldLeadToSade,
    bool PatientInvolved,
    string HealthImpact,
    string ActionTakenDevice,
    bool DeviceReturnedToSponsor,
    string? ReturnTrackingNumber,
    string? RootCauseAnalysis,
    string? SponsorEvaluation,
    bool QuarantineTriggered,
    string QuarantineScope,
    string? SafetyCaseId,
    string? SafetyClassification,
    string InvestigationStatus,
    DateTime CreatedAt,
    string CreatedBy,
    string ReasonForChange,
    long VersionIndex);

public record DeviceQuarantineCreateRequest(
    string StudyId,
    string SiteId,
    string Reason,
    string Scope = "DEVICE_SERIAL",
    string? DeviceInventoryId = null,
    string? LotNumber = null,
    string? SerialNumber = null,
    string ReasonForChange = "Initiate medical device quarantine");

public record DeviceQuarantineReleaseRequest(
    string QuarantineId,
    string ReleaseJustification,
    string SignatureToken,
    string ReasonForChange = "Authorized medical device quarantine release");

public record DeviceQuarantineResponse(
    string Id,
    string StudyId,
    string SiteId,
    string? DeficiencyId,
    string? DeviceInventoryId,
    string Scope,
    string? LotNumber,
    string? SerialNumber,
    string Reason,
    string Status,
    DateTimeOffset QuarantinedAt,
    string QuarantinedBy,
    DateTimeOffset? ReleasedAt,
    string? ReleasedBy,
    string? ReleaseJustification,
    DateTime CreatedAt,
    string CreatedBy,
    string ReasonForChange,
    long VersionIndex);

public record DeviceInvestigationSummaryResponse(
    string StudyId,
    int TotalDevices,
    int AvailableDevices,
    int DispensedDevices,
    int QuarantinedDevices,
    int TotalDeficiencies,
    int OpenSafetyCases,
    int SadeCount,
    int ActiveQuarantines);
