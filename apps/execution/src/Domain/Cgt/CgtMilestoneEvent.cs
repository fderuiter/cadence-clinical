using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Cgt;

public sealed class CgtMilestoneEvent : AggregateRoot<Guid>
{
    public string StudyId { get; private set; } = string.Empty;
    public Guid ChainOfCustodyId { get; private set; }
    public string Stage { get; private set; } = string.Empty;
    public string MilestoneType { get; private set; } = string.Empty;
    public DateTimeOffset Timestamp { get; private set; }
    public string? LocationName { get; private set; }
    public string? FacilityId { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? TemperatureCelsius { get; private set; }
    public string? TemperatureStatus { get; private set; }
    public string? CustodianId { get; private set; }
    public string? CustodianName { get; private set; }
    public string? CustodianRole { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingNumber { get; private set; }
    public bool SealIntact { get; private set; } = true;
    public string? SealId { get; private set; }
    public string? DryShipperId { get; private set; }
    public string? Notes { get; private set; }

    private CgtMilestoneEvent() { } // EF Core

    public CgtMilestoneEvent(
        Guid id,
        string studyId,
        Guid chainOfCustodyId,
        string stage,
        string milestoneType,
        DateTimeOffset timestamp,
        string? locationName = null,
        string? facilityId = null,
        double? latitude = null,
        double? longitude = null,
        double? temperatureCelsius = null,
        string? temperatureStatus = null,
        string? custodianId = null,
        string? custodianName = null,
        string? custodianRole = null,
        string? carrier = null,
        string? trackingNumber = null,
        bool sealIntact = true,
        string? sealId = null,
        string? dryShipperId = null,
        string? notes = null,
        string createdBy = "svc:execution",
        string reasonForChange = "Milestone logged")
        : base(id)
    {
        StudyId = studyId;
        ChainOfCustodyId = chainOfCustodyId;
        Stage = stage;
        MilestoneType = milestoneType;
        Timestamp = timestamp;
        LocationName = locationName;
        FacilityId = facilityId;
        Latitude = latitude;
        Longitude = longitude;
        TemperatureCelsius = temperatureCelsius;
        TemperatureStatus = temperatureStatus;
        CustodianId = custodianId;
        CustodianName = custodianName;
        CustodianRole = custodianRole;
        Carrier = carrier;
        TrackingNumber = trackingNumber;
        SealIntact = sealIntact;
        SealId = sealId;
        DryShipperId = dryShipperId;
        Notes = notes;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }
}
