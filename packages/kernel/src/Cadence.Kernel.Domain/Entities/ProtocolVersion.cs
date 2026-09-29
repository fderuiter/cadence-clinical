using System;

using Cadence.Core.Domain;
using Cadence.Kernel.Domain.Bitemporal;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// Canonical ProtocolVersion snapshot aggregate root capturing CDISC USDM study definitions in PostgreSQL.
/// Conforms to ADR-2245, ADR-2246, and PRD-SYS-053.
/// </summary>
public class ProtocolVersion : AggregateRoot<ProtocolVersionId>, IBitemporalEntity
{
    public TenantId TenantId { get; private set; }
    public StudyId StudyId { get; private set; }
    public string VersionTag { get; private set; } = string.Empty;
    public ProtocolVersionId? PredecessorVersionId { get; private set; }
    public ProtocolApprovalStatus Status { get; private set; } = ProtocolApprovalStatus.Draft;
    public string UsdmPayloadJson { get; private set; } = "{}";
    public TemporalPeriod Period { get; private set; }
    public bool IsActive { get; private set; } = true;

    protected ProtocolVersion() { }

    public ProtocolVersion(
        ProtocolVersionId id,
        TenantId tenantId,
        StudyId studyId,
        string versionTag,
        long versionIndex,
        ProtocolVersionId? predecessorVersionId,
        string usdmPayloadJson,
        TemporalPeriod period,
        string createdBy,
        string reasonForChange)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
        VersionTag = string.IsNullOrWhiteSpace(versionTag) ? throw new ArgumentException("VersionTag cannot be empty", nameof(versionTag)) : versionTag;
        VersionIndex = versionIndex > 0 ? versionIndex : 1;
        PredecessorVersionId = predecessorVersionId;
        UsdmPayloadJson = string.IsNullOrWhiteSpace(usdmPayloadJson) ? "{}" : usdmPayloadJson;
        Period = period;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;
        Status = ProtocolApprovalStatus.Draft;
    }

    /// <summary>
    /// Updates the draft USDM payload before formal approval.
    /// </summary>
    public void UpdateDraftPayload(string newPayloadJson, string updatedBy, string reasonForChange)
    {
        if (Status == ProtocolApprovalStatus.Approved || Status == ProtocolApprovalStatus.Superseded)
        {
            throw new InvalidOperationException($"Approved or superseded protocol versions are permanently immutable under 21 CFR Part 11 (PRD-SYS-053). Fork a new draft version instead.");
        }

        UsdmPayloadJson = newPayloadJson;
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
    }

    /// <summary>
    /// Transitions draft protocol to InReview state.
    /// </summary>
    public void SubmitForReview(string updatedBy, string reasonForChange)
    {
        if (Status != ProtocolApprovalStatus.Draft)
        {
            throw new InvalidOperationException($"Only Draft protocol versions can be submitted for review. Current status: {Status}");
        }

        Status = ProtocolApprovalStatus.InReview;
        CreatedBy = updatedBy;
        ReasonForChange = reasonForChange;
    }

    /// <summary>
    /// Formally approves and locks the protocol version, stamping the approval timestamp and setting bitemporal valid period.
    /// </summary>
    public void Approve(DateTimeOffset approvedAt, string approvedBy, string reasonForChange)
    {
        if (Status != ProtocolApprovalStatus.Draft && Status != ProtocolApprovalStatus.InReview)
        {
            throw new InvalidOperationException($"Protocol version cannot be approved from status: {Status}");
        }

        Status = ProtocolApprovalStatus.Approved;
        CreatedBy = approvedBy;
        ReasonForChange = reasonForChange;
    }

    /// <summary>
    /// Forks a new immutable draft version from this approved protocol version (e.g. for a protocol amendment).
    /// </summary>
    public ProtocolVersion ForkAmendment(
        ProtocolVersionId newVersionId,
        string newVersionTag,
        DateTimeOffset forkedAt,
        string forkedBy,
        string reasonForChange)
    {
        if (Status != ProtocolApprovalStatus.Approved)
        {
            throw new InvalidOperationException($"Only approved protocol versions can be amended. Current status: {Status}");
        }

        // Supersede the recorded interval of the prior version as active truth
        Status = ProtocolApprovalStatus.Superseded;
        Period = Period.Supersede(forkedAt);

        var nextPeriod = TemporalPeriod.CreateCurrent(forkedAt, forkedAt);
        return new ProtocolVersion(
            newVersionId,
            TenantId,
            StudyId,
            newVersionTag,
            VersionIndex + 1,
            Id,
            UsdmPayloadJson,
            nextPeriod,
            forkedBy,
            reasonForChange);
    }
}
