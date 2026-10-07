using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Devices;

/// <summary>
/// Medical device deficiency, malfunction, or usability error reported per ISO 14155:2020 Clause 3.15 and EU MDR/IVDR.
/// Triggers automated quarantine and expedited safety investigation case creation when SADE potential is indicated.
/// @req:PRD-DEV-002
/// </summary>
public sealed class DeviceDeficiency : AggregateRoot<Guid>
{
    public string StudyId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;
    public string? SubjectId { get; private set; }
    public string? DeviceInventoryId { get; private set; }
    public string UdiDi { get; private set; } = string.Empty;
    public string LotNumber { get; private set; } = string.Empty;
    public string? SerialNumber { get; private set; }
    public string DeviceName { get; private set; } = "Investigational Medical Device";
    public string DeficiencyType { get; private set; } = "MALFUNCTION";
    public string DeficiencyDescription { get; private set; } = string.Empty;
    public DateTimeOffset IncidentDate { get; private set; }
    public DateTimeOffset AwarenessDate { get; private set; }
    public bool CouldLeadToSade { get; private set; }
    public bool PatientInvolved { get; private set; }
    public string HealthImpact { get; private set; } = "NONE";
    public string ActionTakenDevice { get; private set; } = "QUARANTINED_AT_SITE";
    public bool DeviceReturnedToSponsor { get; private set; }
    public string? ReturnTrackingNumber { get; private set; }
    public string? RootCauseAnalysis { get; private set; }
    public string? SponsorEvaluation { get; private set; }
    public bool QuarantineTriggered { get; private set; }
    public string QuarantineScope { get; private set; } = "DEVICE_SERIAL";
    public string? SafetyCaseId { get; private set; }
    public string? SafetyClassification { get; private set; }
    public string InvestigationStatus { get; private set; } = "UNDER_INVESTIGATION";

    private DeviceDeficiency() { } // EF Core

    public DeviceDeficiency(
        Guid id,
        string studyId,
        string siteId,
        string udiDi,
        string lotNumber,
        string deficiencyDescription,
        DateTimeOffset incidentDate,
        DateTimeOffset awarenessDate,
        string? subjectId = null,
        string? deviceInventoryId = null,
        string? serialNumber = null,
        string deviceName = "Investigational Medical Device",
        string deficiencyType = "MALFUNCTION",
        bool couldLeadToSade = false,
        bool patientInvolved = false,
        string healthImpact = "NONE",
        string actionTakenDevice = "QUARANTINED_AT_SITE",
        bool deviceReturnedToSponsor = false,
        string? returnTrackingNumber = null,
        string quarantineScope = "DEVICE_SERIAL",
        bool quarantineTriggered = true,
        string? safetyCaseId = null,
        string? safetyClassification = null)
        : base(id)
    {
        StudyId = studyId;
        SiteId = siteId;
        SubjectId = subjectId;
        DeviceInventoryId = deviceInventoryId;
        UdiDi = udiDi;
        LotNumber = lotNumber;
        SerialNumber = serialNumber;
        DeviceName = deviceName;
        DeficiencyType = deficiencyType;
        DeficiencyDescription = deficiencyDescription;
        IncidentDate = incidentDate;
        AwarenessDate = awarenessDate;
        CouldLeadToSade = couldLeadToSade;
        PatientInvolved = patientInvolved;
        HealthImpact = healthImpact;
        ActionTakenDevice = actionTakenDevice;
        DeviceReturnedToSponsor = deviceReturnedToSponsor;
        ReturnTrackingNumber = returnTrackingNumber;
        QuarantineScope = quarantineScope;
        QuarantineTriggered = quarantineTriggered;
        SafetyCaseId = safetyCaseId;
        SafetyClassification = safetyClassification ?? (couldLeadToSade ? "SADE_POTENTIAL" : "NON_SERIOUS_DEFICIENCY");
        InvestigationStatus = "UNDER_INVESTIGATION";
    }

    public void AttachSafetyCase(string caseId, string classification)
    {
        SafetyCaseId = caseId;
        SafetyClassification = classification;
    }

    public void UpdateInvestigation(string status, string? rootCause, string? sponsorEval)
    {
        InvestigationStatus = status;
        if (!string.IsNullOrEmpty(rootCause)) RootCauseAnalysis = rootCause;
        if (!string.IsNullOrEmpty(sponsorEval)) SponsorEvaluation = sponsorEval;
    }
}
