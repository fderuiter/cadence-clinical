using System;

using Cadence.Core.Domain;
using Cadence.Kernel.Domain.Bitemporal;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Canonical Observation aggregate root capturing clinical data with full bitemporal intervals under 21 CFR Part 11.
/// @req:PRD-SYS-053
/// </summary>
public class Observation : AggregateRoot<ObservationId>
{
    public TenantId TenantId { get; private set; }
    public StudyId StudyId { get; private set; }
    public ParticipantId ParticipantId { get; private set; }
    public string ObservationType { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public TemporalPeriod Period { get; private set; }
    public bool IsActive { get; private set; } = true;

    protected Observation() { }

    public Observation(
        ObservationId id,
        TenantId tenantId,
        StudyId studyId,
        ParticipantId participantId,
        string observationType,
        string value,
        TemporalPeriod period,
        string createdBy,
        string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
        ParticipantId = participantId;
        ObservationType = observationType;
        Value = value;
        Period = period;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
    }

    public void Supersede(string newValue, DateTimeOffset supersededAt, string updatedBy, string reasonForChange)
    {
        Value = newValue;
        Period = Period.Supersede(supersededAt);
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
        VersionIndex++;
    }
}
