using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

using Cadence.Apps.Ctms.Domain.Events;
using Cadence.Core.Domain;

namespace Cadence.Apps.Ctms.Domain;

public sealed class CtmsAuditLog : AggregateRoot<string>
{
    public required DateTimeOffset Timestamp { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;

    public CtmsAuditLog() : base(Guid.NewGuid().ToString()) { }
    public CtmsAuditLog(string id) : base(id) { }
}

public sealed class CtmsClinicalQuery : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string? VisitId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Explanation { get; set; }

    public CtmsClinicalQuery() : base(Guid.NewGuid().ToString()) { }
    public CtmsClinicalQuery(string id) : base(id) { }
}

public sealed class CtmsCraAllocation : AggregateRoot<string>
{
    public string CraId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset EffectiveStartDate { get; set; }
    public DateTimeOffset? EffectiveEndDate { get; set; }

    public CtmsCraAllocation() : base(Guid.NewGuid().ToString()) { }
    public CtmsCraAllocation(string id) : base(id) { }
}

public sealed class CtmsDefeatedMonitoringVisit : AggregateRoot<string>
{
    public string VisitId { get; set; } = string.Empty;
    public DateTimeOffset? ActualDate { get; set; }
    public string? Findings { get; set; }
    public DateTimeOffset DeviceTimestamp { get; set; }
    public string OfflineSyncMarkers { get; set; } = "{}";
    public string Status { get; set; } = string.Empty;

    public CtmsDefeatedMonitoringVisit() : base(Guid.NewGuid().ToString()) { }
    public CtmsDefeatedMonitoringVisit(string id) : base(id) { }
}

public sealed class CtmsDelegation : AggregateRoot<string>
{
    public string SiteId { get; set; } = string.Empty;
    public string StaffUserId { get; set; } = string.Empty;
    public string TaskCodes { get; set; } = "[]";
    public string StartDate { get; set; } = string.Empty;
    public string? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool SignedOff { get; set; }

    public CtmsDelegation() : base(Guid.NewGuid().ToString()) { }
    public CtmsDelegation(string id) : base(id) { }
}

public sealed class CtmsGeneratedLetter : AggregateRoot<string>
{
    public string VisitId { get; set; } = string.Empty;
    public string LetterType { get; set; } = string.Empty;
    public string RenderedContent { get; set; } = string.Empty;
    public string? PdfObjectKey { get; set; }
    public string? PdfSha256Hash { get; set; }
    public string Status { get; set; } = "DRAFT";
    public bool IsSigned { get; set; }
    public string? SignatureManifestation { get; set; }
    public string? EtmfDocumentId { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public string? RecipientEmail { get; set; }

    public CtmsGeneratedLetter() : base(Guid.NewGuid().ToString()) { }
    public CtmsGeneratedLetter(string id) : base(id) { }
}

public sealed class CtmsInvestigatorGrant : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public double TotalBudget { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = string.Empty;

    public CtmsInvestigatorGrant() : base(Guid.NewGuid().ToString()) { }
    public CtmsInvestigatorGrant(string id) : base(id) { }
}

public sealed class CtmsMonitoringVisitFinding : AggregateRoot<string>
{
    private readonly List<object> _domainEvents = [];

    public string VisitId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public FindingResolutionStatus ResolutionStatus { get; private set; } = FindingResolutionStatus.Open;
    public string? OfflineSyncMarkers { get; set; }
    public string? SyncStatus { get; set; }

    [NotMapped]
    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();

    public CtmsMonitoringVisitFinding() : base(Guid.NewGuid().ToString()) { }
    public CtmsMonitoringVisitFinding(string id) : base(id) { }
    public CtmsMonitoringVisitFinding(string id, FindingResolutionStatus initialResolutionStatus) : base(id)
    {
        ResolutionStatus = initialResolutionStatus;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void RecordDomainEvent(object @event) => _domainEvents.Add(@event);

    public Result<FindingResolutionStatus> TransitionTo(
        FindingResolutionStatus target,
        DateTimeOffset timestamp,
        string userId,
        string changeReason)
    {
        var guardResult = FindingResolutionTransitionGraph.Guard(ResolutionStatus, target);
        if (guardResult.IsFailure)
        {
            return guardResult;
        }

        var previous = ResolutionStatus;
        ResolutionStatus = guardResult.Value;
        ReasonForChange = changeReason;
        VersionIndex++;

        RecordDomainEvent(new MonitoringVisitFindingStatusChangedDomainEvent(
            Id,
            VisitId,
            previous,
            ResolutionStatus,
            timestamp,
            userId,
            changeReason));

        return ResolutionStatus;
    }

    public Result<FindingResolutionStatus> SubmitForReview(DateTimeOffset timestamp, string userId, string changeReason) =>
        TransitionTo(FindingResolutionStatus.UnderReview, timestamp, userId, changeReason);

    public Result<FindingResolutionStatus> Resolve(DateTimeOffset timestamp, string userId, string changeReason) =>
        TransitionTo(FindingResolutionStatus.Resolved, timestamp, userId, changeReason);

    public Result<FindingResolutionStatus> Close(DateTimeOffset timestamp, string userId, string changeReason) =>
        TransitionTo(FindingResolutionStatus.Closed, timestamp, userId, changeReason);
}

public sealed class CtmsMonitoringVisit : AggregateRoot<string>
{
    private readonly List<object> _domainEvents = [];

    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string CraId { get; set; } = string.Empty;
    public string VisitType { get; set; } = string.Empty;
    public DateTimeOffset ScheduledDate { get; set; }
    public DateTimeOffset? ActualDate { get; set; }
    public MonitoringVisitStatus Status { get; private set; } = MonitoringVisitStatus.Scheduled;
    public string? SignatureManifestation { get; set; }
    public string? Signer { get; set; }
    public DateTimeOffset? SigningTimestamp { get; set; }
    public string? OfflineSyncMarkers { get; set; }
    public string? SyncStatus { get; set; }

    [NotMapped]
    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();

    public CtmsMonitoringVisit() : base(Guid.NewGuid().ToString()) { }
    public CtmsMonitoringVisit(string id) : base(id) { }
    public CtmsMonitoringVisit(string id, MonitoringVisitStatus initialStatus) : base(id)
    {
        Status = initialStatus;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void RecordDomainEvent(object @event) => _domainEvents.Add(@event);

    public Result<MonitoringVisitStatus> TransitionTo(
        MonitoringVisitStatus target,
        DateTimeOffset timestamp,
        string userId,
        string changeReason)
    {
        var guardResult = MonitoringVisitTransitionGraph.Guard(Status, target);
        if (guardResult.IsFailure)
        {
            return guardResult;
        }

        var previous = Status;
        Status = guardResult.Value;
        ReasonForChange = changeReason;
        VersionIndex++;

        RecordDomainEvent(new MonitoringVisitStatusChangedDomainEvent(
            Id,
            previous,
            Status,
            timestamp,
            userId,
            changeReason));

        return Status;
    }

    public Result<MonitoringVisitStatus> StartVisit(DateTimeOffset timestamp, string userId, string changeReason)
    {
        var result = TransitionTo(MonitoringVisitStatus.InProgress, timestamp, userId, changeReason);
        if (result.IsFailure)
        {
            return result;
        }

        RecordDomainEvent(new MonitoringVisitStartedDomainEvent(Id, timestamp, userId, changeReason));
        return result;
    }

    public Result<MonitoringVisitStatus> CompleteVisit(
        DateTimeOffset actualDate,
        DateTimeOffset timestamp,
        string userId,
        string changeReason)
    {
        var result = TransitionTo(MonitoringVisitStatus.Completed, timestamp, userId, changeReason);
        if (result.IsFailure)
        {
            return result;
        }

        ActualDate = actualDate;
        RecordDomainEvent(new MonitoringVisitCompletedDomainEvent(Id, actualDate, timestamp, userId, changeReason));
        return result;
    }

    public Result<MonitoringVisitStatus> DraftReport(DateTimeOffset timestamp, string userId, string changeReason)
    {
        var result = TransitionTo(MonitoringVisitStatus.ReportDrafted, timestamp, userId, changeReason);
        if (result.IsFailure)
        {
            return result;
        }

        RecordDomainEvent(new MonitoringVisitReportDraftedDomainEvent(Id, timestamp, userId, changeReason));
        return result;
    }

    public Result<MonitoringVisitStatus> Approve(
        string signer,
        string signatureManifestationJson,
        DateTimeOffset signingTimestamp,
        DateTimeOffset timestamp,
        string userId,
        string changeReason)
    {
        var result = TransitionTo(MonitoringVisitStatus.Approved, timestamp, userId, changeReason);
        if (result.IsFailure)
        {
            return result;
        }

        Signer = signer;
        SignatureManifestation = signatureManifestationJson;
        SigningTimestamp = signingTimestamp;

        RecordDomainEvent(new MonitoringVisitApprovedDomainEvent(
            Id,
            signer,
            signingTimestamp,
            timestamp,
            userId,
            changeReason));

        return result;
    }

    public Result<MonitoringVisitStatus> Cancel(DateTimeOffset timestamp, string userId, string changeReason)
    {
        var result = TransitionTo(MonitoringVisitStatus.Cancelled, timestamp, userId, changeReason);
        if (result.IsFailure)
        {
            return result;
        }

        RecordDomainEvent(new MonitoringVisitCancelledDomainEvent(Id, timestamp, userId, changeReason));
        return result;
    }
}

public sealed class CtmsRecruitmentRecord : AggregateRoot<string>
{
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public int ScreenedCount { get; set; }
    public int EnrolledCount { get; set; }
    public int TargetCount { get; set; }
    public DateTimeOffset AsOfDate { get; set; }

    public CtmsRecruitmentRecord() : base(Guid.NewGuid().ToString()) { }
    public CtmsRecruitmentRecord(string id) : base(id) { }
}

public sealed class CtmsRegulatoryForm : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string FormType { get; set; } = string.Empty;
    public string RenderedContent { get; set; } = string.Empty;
    public string ApprovalStatus { get; set; } = string.Empty;
    public string? SignatureManifestation { get; set; }
    public string? Signer { get; set; }
    public DateTimeOffset? SigningTimestamp { get; set; }

    public CtmsRegulatoryForm() : base(Guid.NewGuid().ToString()) { }
    public CtmsRegulatoryForm(string id) : base(id) { }
}

public sealed class CtmsSiteMilestone : AggregateRoot<string>
{
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string MilestoneType { get; set; } = string.Empty;
    public DateTimeOffset? PlannedDate { get; set; }
    public DateTimeOffset? ActualDate { get; set; }
    public string Status { get; set; } = string.Empty;

    public CtmsSiteMilestone() : base(Guid.NewGuid().ToString()) { }
    public CtmsSiteMilestone(string id) : base(id) { }
}


public sealed class CtmsBudgetLineItem : AggregateRoot<string>
{
    public string GrantId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Amount { get; set; }

    public CtmsBudgetLineItem() : base(Guid.NewGuid().ToString()) { }
    public CtmsBudgetLineItem(string id) : base(id) { }
}

public sealed class CtmsPaymentMilestone : AggregateRoot<string>
{
    public string GrantId { get; set; } = string.Empty;
    public string MilestoneName { get; set; } = string.Empty;
    public string TriggerCondition { get; set; } = string.Empty;
    public double Amount { get; set; }
    public bool IsTriggered { get; set; }
    public DateTimeOffset? TriggeredAt { get; set; }

    public CtmsPaymentMilestone() : base(Guid.NewGuid().ToString()) { }
    public CtmsPaymentMilestone(string id) : base(id) { }
}

public sealed class DoaDelegationRecord : AggregateRoot<string>
{
    public string SiteId { get; set; } = string.Empty;
    public string StaffUserId { get; set; } = string.Empty;
    public string TaskCode { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PiSignatureHash { get; set; }
    public DateTimeOffset? PiApprovedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }

    public DoaDelegationRecord() : base(Guid.NewGuid().ToString()) { }
    public DoaDelegationRecord(string id) : base(id) { }
}

public sealed class CtmsInvestigatorPayable : AggregateRoot<string>
{
    public string GrantId { get; set; } = string.Empty;
    public string? MilestoneId { get; set; }
    public double Amount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    public CtmsInvestigatorPayable() : base(Guid.NewGuid().ToString()) { }
    public CtmsInvestigatorPayable(string id) : base(id) { }
}

public sealed class CtmsSiteGreenlightGate : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string OverallStatus { get; set; } = "PENDING";
    public int ReadinessPercentage { get; set; }
    public bool FacetRegulatoryIrb { get; set; }
    public bool FacetCtaBudget { get; set; }
    public bool FacetEisfBaseline { get; set; }
    public bool FacetDoaLog { get; set; }
    public bool FacetStaffTraining { get; set; }
    public bool FacetIpRelease { get; set; }
    public string? GreenlightToken { get; set; }
    public string? GreenlightCertifiedBy { get; set; }
    public DateTimeOffset? GreenlightCertifiedAt { get; set; }
    public bool IsOverridden { get; set; }
    public string? OverrideSponsorUserId { get; set; }
    public string? OverrideMedicalUserId { get; set; }
    public string? OverrideJustification { get; set; }
    public DateTimeOffset? OverrideAuthorizedAt { get; set; }
    public string? UnfulfilledReasons { get; set; }

    public CtmsSiteGreenlightGate() : base(Guid.NewGuid().ToString()) { }
    public CtmsSiteGreenlightGate(string id) : base(id) { }
}

public record SiteVisitFindingModel(
    string Text,
    string Severity,
    string ResolutionStatus
);

public record SiteVisitLetterModel(
    string CraId,
    string CreatedAt,
    string StudyId,
    string SiteId,
    string VisitType = "Monitoring",
    string? ScheduledDate = null,
    string? ActualDate = null,
    IReadOnlyList<SiteVisitFindingModel>? Findings = null,
    string? VisitId = null,
    bool Finalize = false,
    string? SignerName = null,
    string? SigningPurpose = null,
    string? InvestigatorEmail = null,
    string? ReasonForChange = null
);
