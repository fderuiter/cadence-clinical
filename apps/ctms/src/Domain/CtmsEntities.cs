using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Apps.Ctms.Domain;

public enum CtmsClinicalQueryStatus
{
    Open,
    Answered,
    Closed,
    Cancelled
}

public enum CtmsCraAllocationStatus
{
    Assigned,
    Active,
    Released,
    Terminated
}

public enum CtmsDefeatedMonitoringVisitStatus
{
    Draft,
    Submitted,
    Reviewed,
    Archived
}

public enum CtmsGeneratedLetterStatus
{
    Draft,
    Generated,
    Dispatched,
    Signed,
    Finalized,
    Archived
}

public enum CtmsInvestigatorGrantStatus
{
    Draft,
    Active,
    Suspended,
    Closed
}

public enum CtmsMonitoringVisitStatus
{
    Scheduled,
    InProgress,
    Completed,
    Signed_Off,
    Cancelled
}

public enum CtmsMonitoringVisitTrigger
{
    Start,
    Complete,
    SignOff,
    Cancel
}

public enum CtmsRegulatoryFormStatus
{
    Draft,
    PendingReview,
    Approved,
    Rejected
}

public enum CtmsSiteMilestoneStatus
{
    Planned,
    Active,
    Achieved,
    Completed,
    Delayed,
    Cancelled
}

public enum CtmsSiteMilestoneTrigger
{
    Activate,
    Achieve,
    Complete,
    Delay,
    Cancel
}

public enum DoaDelegationStatus
{
    Pending,
    Active,
    Expired,
    Revoked
}

public enum CtmsPaymentStatus
{
    Pending,
    Approved,
    Paid,
    Cancelled
}

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
    public CtmsClinicalQueryStatus Status { get; private set; } = CtmsClinicalQueryStatus.Open;
    public string? Explanation { get; set; }

    public CtmsClinicalQuery() : base(Guid.NewGuid().ToString()) { }
    public CtmsClinicalQuery(string id, CtmsClinicalQueryStatus initialStatus = CtmsClinicalQueryStatus.Open) : base(id)
    {
        Status = initialStatus;
    }
    internal void SetStatusForTesting(CtmsClinicalQueryStatus status) => Status = status;
}

public sealed class CtmsCraAllocation : AggregateRoot<string>
{
    public string CraId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public CtmsCraAllocationStatus Status { get; private set; } = CtmsCraAllocationStatus.Assigned;
    public DateTimeOffset EffectiveStartDate { get; set; }
    public DateTimeOffset? EffectiveEndDate { get; set; }

    public CtmsCraAllocation() : base(Guid.NewGuid().ToString()) { }
    public CtmsCraAllocation(string id, CtmsCraAllocationStatus initialStatus = CtmsCraAllocationStatus.Assigned) : base(id)
    {
        Status = initialStatus;
    }
    internal void SetStatusForTesting(CtmsCraAllocationStatus status) => Status = status;
}

public sealed class CtmsDefeatedMonitoringVisit : AggregateRoot<string>
{
    public string VisitId { get; set; } = string.Empty;
    public DateTimeOffset? ActualDate { get; set; }
    public string? Findings { get; set; }
    public DateTimeOffset DeviceTimestamp { get; set; }
    public string OfflineSyncMarkers { get; set; } = "{}";
    public CtmsDefeatedMonitoringVisitStatus Status { get; private set; } = CtmsDefeatedMonitoringVisitStatus.Draft;

    public CtmsDefeatedMonitoringVisit() : base(Guid.NewGuid().ToString()) { }
    public CtmsDefeatedMonitoringVisit(string id, CtmsDefeatedMonitoringVisitStatus initialStatus = CtmsDefeatedMonitoringVisitStatus.Draft) : base(id)
    {
        Status = initialStatus;
    }
    internal void SetStatusForTesting(CtmsDefeatedMonitoringVisitStatus status) => Status = status;
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
    public CtmsGeneratedLetterStatus Status { get; private set; } = CtmsGeneratedLetterStatus.Draft;
    public bool IsSigned { get; set; }
    public string? SignatureManifestation { get; set; }
    public string? EtmfDocumentId { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public string? RecipientEmail { get; set; }

    public CtmsGeneratedLetter() : base(Guid.NewGuid().ToString()) { }
    public CtmsGeneratedLetter(string id, CtmsGeneratedLetterStatus initialStatus = CtmsGeneratedLetterStatus.Draft) : base(id)
    {
        Status = initialStatus;
    }
    internal void SetStatusForTesting(CtmsGeneratedLetterStatus status) => Status = status;
}

public sealed class CtmsInvestigatorGrant : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public double TotalBudget { get; set; }
    public string Currency { get; set; } = "USD";
    public CtmsInvestigatorGrantStatus Status { get; private set; } = CtmsInvestigatorGrantStatus.Draft;

    public CtmsInvestigatorGrant() : base(Guid.NewGuid().ToString()) { }
    public CtmsInvestigatorGrant(string id, CtmsInvestigatorGrantStatus initialStatus = CtmsInvestigatorGrantStatus.Draft) : base(id)
    {
        Status = initialStatus;
    }
    internal void SetStatusForTesting(CtmsInvestigatorGrantStatus status) => Status = status;
}

public sealed class CtmsMonitoringVisitFinding : AggregateRoot<string>
{
    public string VisitId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string ResolutionStatus { get; set; } = string.Empty;
    public string? OfflineSyncMarkers { get; set; }
    public string? SyncStatus { get; set; }

    public CtmsMonitoringVisitFinding() : base(Guid.NewGuid().ToString()) { }
    public CtmsMonitoringVisitFinding(string id) : base(id) { }
}

public sealed class CtmsMonitoringVisit : StatefulAggregateRoot<string, CtmsMonitoringVisitStatus, CtmsMonitoringVisitTrigger>
{
    private static readonly StateMachineDefinition<CtmsMonitoringVisitStatus, CtmsMonitoringVisitTrigger> VisitStateMachine =
        new StateMachineBuilder<CtmsMonitoringVisitStatus, CtmsMonitoringVisitTrigger>(CtmsMonitoringVisitStatus.Scheduled)
            .WithTerminalStates(CtmsMonitoringVisitStatus.Signed_Off, CtmsMonitoringVisitStatus.Cancelled)
            .In(CtmsMonitoringVisitStatus.Scheduled)
                .Permit(CtmsMonitoringVisitTrigger.Start, CtmsMonitoringVisitStatus.InProgress)
                .Permit(CtmsMonitoringVisitTrigger.Complete, CtmsMonitoringVisitStatus.Completed)
                .Permit(CtmsMonitoringVisitTrigger.Cancel, CtmsMonitoringVisitStatus.Cancelled)
            .In(CtmsMonitoringVisitStatus.InProgress)
                .Permit(CtmsMonitoringVisitTrigger.Complete, CtmsMonitoringVisitStatus.Completed)
                .Permit(CtmsMonitoringVisitTrigger.Cancel, CtmsMonitoringVisitStatus.Cancelled)
            .In(CtmsMonitoringVisitStatus.Completed)
                .Permit(CtmsMonitoringVisitTrigger.SignOff, CtmsMonitoringVisitStatus.Signed_Off)
            .Build();

    protected override StateMachineDefinition<CtmsMonitoringVisitStatus, CtmsMonitoringVisitTrigger> StateMachine => VisitStateMachine;

    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string CraId { get; set; } = string.Empty;
    public string VisitType { get; set; } = string.Empty;
    public DateTimeOffset ScheduledDate { get; set; }
    public DateTimeOffset? ActualDate { get; set; }
    public string? SignatureManifestation { get; set; }
    public string? Signer { get; set; }
    public DateTimeOffset? SigningTimestamp { get; set; }
    public string? OfflineSyncMarkers { get; set; }
    public string? SyncStatus { get; set; }

    public CtmsMonitoringVisit() : base(Guid.NewGuid().ToString(), CtmsMonitoringVisitStatus.Scheduled) { }
    public CtmsMonitoringVisit(string id, CtmsMonitoringVisitStatus initialStatus = CtmsMonitoringVisitStatus.Scheduled) : base(id, initialStatus) { }

    public Result<CtmsMonitoringVisitStatus> Complete(
        DateTimeOffset actualDate,
        string reasonForChange,
        string userId,
        DateTimeOffset timestamp,
        string? userRole = null)
    {
        ActualDate = actualDate;
        return Transition(CtmsMonitoringVisitTrigger.Complete, userId, timestamp, reasonForChange, userRole);
    }

    public Result<CtmsMonitoringVisitStatus> SignOff(
        string signer,
        DateTimeOffset signingTimestamp,
        string signatureManifestation,
        string reasonForChange,
        string userId,
        string? userRole = null)
    {
        var result = Transition(CtmsMonitoringVisitTrigger.SignOff, userId, signingTimestamp, reasonForChange, userRole);
        if (result.IsSuccess)
        {
            Signer = signer;
            SigningTimestamp = signingTimestamp;
            SignatureManifestation = signatureManifestation;
        }
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
    public CtmsRegulatoryFormStatus ApprovalStatus { get; private set; } = CtmsRegulatoryFormStatus.Draft;
    public string? SignatureManifestation { get; set; }
    public string? Signer { get; set; }
    public DateTimeOffset? SigningTimestamp { get; set; }

    public CtmsRegulatoryForm() : base(Guid.NewGuid().ToString()) { }
    public CtmsRegulatoryForm(string id, CtmsRegulatoryFormStatus initialStatus = CtmsRegulatoryFormStatus.Draft) : base(id)
    {
        ApprovalStatus = initialStatus;
    }
    internal void SetApprovalStatusForTesting(CtmsRegulatoryFormStatus status) => ApprovalStatus = status;
}

public sealed class CtmsSiteMilestone : StatefulAggregateRoot<string, CtmsSiteMilestoneStatus, CtmsSiteMilestoneTrigger>
{
    private static readonly StateMachineDefinition<CtmsSiteMilestoneStatus, CtmsSiteMilestoneTrigger> MilestoneStateMachine =
        new StateMachineBuilder<CtmsSiteMilestoneStatus, CtmsSiteMilestoneTrigger>(CtmsSiteMilestoneStatus.Planned)
            .WithTerminalStates(CtmsSiteMilestoneStatus.Completed, CtmsSiteMilestoneStatus.Achieved, CtmsSiteMilestoneStatus.Cancelled)
            .In(CtmsSiteMilestoneStatus.Planned)
                .Permit(CtmsSiteMilestoneTrigger.Activate, CtmsSiteMilestoneStatus.Active)
                .Permit(CtmsSiteMilestoneTrigger.Achieve, CtmsSiteMilestoneStatus.Achieved)
                .Permit(CtmsSiteMilestoneTrigger.Complete, CtmsSiteMilestoneStatus.Completed)
                .Permit(CtmsSiteMilestoneTrigger.Delay, CtmsSiteMilestoneStatus.Delayed)
                .Permit(CtmsSiteMilestoneTrigger.Cancel, CtmsSiteMilestoneStatus.Cancelled)
            .In(CtmsSiteMilestoneStatus.Active)
                .Permit(CtmsSiteMilestoneTrigger.Achieve, CtmsSiteMilestoneStatus.Achieved)
                .Permit(CtmsSiteMilestoneTrigger.Complete, CtmsSiteMilestoneStatus.Completed)
                .Permit(CtmsSiteMilestoneTrigger.Delay, CtmsSiteMilestoneStatus.Delayed)
                .Permit(CtmsSiteMilestoneTrigger.Cancel, CtmsSiteMilestoneStatus.Cancelled)
            .In(CtmsSiteMilestoneStatus.Delayed)
                .Permit(CtmsSiteMilestoneTrigger.Activate, CtmsSiteMilestoneStatus.Active)
                .Permit(CtmsSiteMilestoneTrigger.Achieve, CtmsSiteMilestoneStatus.Achieved)
                .Permit(CtmsSiteMilestoneTrigger.Complete, CtmsSiteMilestoneStatus.Completed)
                .Permit(CtmsSiteMilestoneTrigger.Cancel, CtmsSiteMilestoneStatus.Cancelled)
            .Build();

    protected override StateMachineDefinition<CtmsSiteMilestoneStatus, CtmsSiteMilestoneTrigger> StateMachine => MilestoneStateMachine;

    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string MilestoneType { get; set; } = string.Empty;
    public DateTimeOffset? PlannedDate { get; set; }
    public DateTimeOffset? ActualDate { get; set; }

    public CtmsSiteMilestone() : base(Guid.NewGuid().ToString(), CtmsSiteMilestoneStatus.Planned) { }
    public CtmsSiteMilestone(string id, CtmsSiteMilestoneStatus initialStatus = CtmsSiteMilestoneStatus.Planned) : base(id, initialStatus) { }

    public Result<CtmsSiteMilestoneStatus> Activate(string userId, DateTimeOffset timestamp, string reasonForChange) =>
        Transition(CtmsSiteMilestoneTrigger.Activate, userId, timestamp, reasonForChange);

    public Result<CtmsSiteMilestoneStatus> Complete(string userId, DateTimeOffset timestamp, string reasonForChange, DateTimeOffset? actualDate = null)
    {
        if (actualDate.HasValue) ActualDate = actualDate;
        return Transition(CtmsSiteMilestoneTrigger.Complete, userId, timestamp, reasonForChange);
    }
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
    public DoaDelegationStatus Status { get; private set; } = DoaDelegationStatus.Pending;
    public string? PiSignatureHash { get; set; }
    public DateTimeOffset? PiApprovedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }

    public DoaDelegationRecord() : base(Guid.NewGuid().ToString()) { }
    public DoaDelegationRecord(string id, DoaDelegationStatus initialStatus = DoaDelegationStatus.Pending) : base(id)
    {
        Status = initialStatus;
    }
    internal void SetStatusForTesting(DoaDelegationStatus status) => Status = status;
}

public sealed class CtmsInvestigatorPayable : AggregateRoot<string>
{
    public string GrantId { get; set; } = string.Empty;
    public string? MilestoneId { get; set; }
    public double Amount { get; set; }
    public CtmsPaymentStatus PaymentStatus { get; private set; } = CtmsPaymentStatus.Pending;
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    public CtmsInvestigatorPayable() : base(Guid.NewGuid().ToString()) { }
    public CtmsInvestigatorPayable(string id, CtmsPaymentStatus initialStatus = CtmsPaymentStatus.Pending) : base(id)
    {
        PaymentStatus = initialStatus;
    }
    internal void SetPaymentStatusForTesting(CtmsPaymentStatus status) => PaymentStatus = status;
}

public enum SiteGreenlightStatus
{
    Pending,
    Approved,
    Suspended,
    Revoked
}

public class SiteGreenlightTransitionException : InvalidOperationException
{
    public SiteGreenlightTransitionException(string message) : base(message) { }
    public SiteGreenlightTransitionException(string message, Exception innerException) : base(message, innerException) { }
}

public record SiteGreenlightApproved(
    string GateId,
    string Study,
    string Site,
    string CountryCode,
    string CertifiedBy,
    DateTimeOffset CertifiedAt,
    string GreenlightToken,
    bool IsOverridden,
    string? Reason);

public record SiteGreenlightRevoked(
    string GateId,
    string Study,
    string Site,
    string CountryCode,
    string RevokedBy,
    DateTimeOffset RevokedAt,
    string Reason);

public record SiteGreenlightSuspended(
    string GateId,
    string Study,
    string Site,
    string CountryCode,
    string SuspendedBy,
    DateTimeOffset SuspendedAt,
    string Reason);

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<object> events, CancellationToken ct = default);
}

public sealed class DefaultDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IReadOnlyList<object> events, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class CtmsSiteGreenlightGate : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public SiteGreenlightStatus OverallStatus { get; private set; } = SiteGreenlightStatus.Pending;
    public int ReadinessPercentage { get; set; }
    public bool FacetRegulatoryIrb { get; set; }
    public bool FacetCtaBudget { get; set; }
    public bool FacetEisfBaseline { get; set; }
    public bool FacetDoaLog { get; set; }
    public bool FacetStaffTraining { get; set; }
    public bool FacetIpRelease { get; set; }
    public string? GreenlightToken { get; private set; }
    public string? GreenlightCertifiedBy { get; private set; }
    public DateTimeOffset? GreenlightCertifiedAt { get; private set; }
    public bool IsOverridden { get; set; }
    public string? OverrideSponsorUserId { get; set; }
    public string? OverrideMedicalUserId { get; set; }
    public string? OverrideJustification { get; set; }
    public DateTimeOffset? OverrideAuthorizedAt { get; set; }
    public string? UnfulfilledReasons { get; set; }

    [NotMapped]
    private readonly List<object> _domainEvents = [];
    [NotMapped]
    private readonly List<object> _uncommittedEvents = [];

    [NotMapped]
    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();
    [NotMapped]
    public IReadOnlyList<object> Events => _domainEvents.AsReadOnly();
    [NotMapped]
    public IReadOnlyList<object> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    public bool IsFullyCompliant => IsOverridden || (
        FacetRegulatoryIrb &&
        FacetCtaBudget &&
        FacetEisfBaseline &&
        FacetDoaLog &&
        FacetStaffTraining &&
        FacetIpRelease);

    public CtmsSiteGreenlightGate() : base(Guid.NewGuid().ToString()) { }
    public CtmsSiteGreenlightGate(string id) : base(id) { }
    public CtmsSiteGreenlightGate(string id, SiteGreenlightStatus initialStatus) : base(id)
    {
        OverallStatus = initialStatus;
    }
    internal void SetOverallStatusForTesting(SiteGreenlightStatus status) => OverallStatus = status;

    public void RecordEvent(object domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
        _uncommittedEvents.Add(domainEvent);
    }

    public IReadOnlyList<object> FlushUncommittedEvents()
    {
        var events = _uncommittedEvents.ToList();
        _uncommittedEvents.Clear();
        return events;
    }

    public void ClearEvents()
    {
        _domainEvents.Clear();
        _uncommittedEvents.Clear();
    }

    public void Certify(string signer, string tokenSecret, DateTimeOffset timestamp, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signer);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenSecret);

        if (OverallStatus == SiteGreenlightStatus.Revoked)
        {
            throw new SiteGreenlightTransitionException(
                $"Cannot certify greenlight gate for site {SiteId} because it is in terminal state '{OverallStatus}'.");
        }

        if (OverallStatus != SiteGreenlightStatus.Pending)
        {
            throw new SiteGreenlightTransitionException(
                $"Cannot certify greenlight gate for site {SiteId} because current status is '{OverallStatus}'. Only gates in Pending status can be certified.");
        }

        if (!IsFullyCompliant)
        {
            throw new InvalidOperationException(
                $"Site {SiteId} does not fulfill all 6 greenlight facets and cannot be certified.");
        }

        long issuedAt = timestamp.ToUnixTimeSeconds();
        long expiresAt = issuedAt + (86400 * 365);
        string token = GreenlightTokenGenerator.GenerateToken(
            SiteId, StudyId, CountryCode, signer, tokenSecret, issuedAt, expiresAt, IsOverridden);

        OverallStatus = SiteGreenlightStatus.Approved;
        GreenlightToken = token;
        GreenlightCertifiedBy = signer;
        GreenlightCertifiedAt = timestamp;
        ReasonForChange = reason ?? "Greenlight activation certified";

        RecordEvent(new SiteGreenlightApproved(
            Id,
            StudyId,
            SiteId,
            CountryCode,
            signer,
            timestamp,
            token,
            IsOverridden,
            ReasonForChange));
    }

    public void CertifyWithToken(string token, string signer, DateTimeOffset timestamp, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(signer);

        if (OverallStatus == SiteGreenlightStatus.Revoked)
        {
            throw new SiteGreenlightTransitionException(
                $"Cannot certify greenlight gate for site {SiteId} because it is in terminal state '{OverallStatus}'.");
        }

        if (OverallStatus != SiteGreenlightStatus.Pending)
        {
            throw new SiteGreenlightTransitionException(
                $"Cannot certify greenlight gate for site {SiteId} because current status is '{OverallStatus}'. Only gates in Pending status can be certified.");
        }

        if (!IsFullyCompliant)
        {
            throw new InvalidOperationException(
                $"Site {SiteId} does not fulfill all 6 greenlight facets and cannot be certified.");
        }

        OverallStatus = SiteGreenlightStatus.Approved;
        GreenlightToken = token;
        GreenlightCertifiedBy = signer;
        GreenlightCertifiedAt = timestamp;
        ReasonForChange = reason ?? "Greenlight activation certified";

        RecordEvent(new SiteGreenlightApproved(
            Id,
            StudyId,
            SiteId,
            CountryCode,
            signer,
            timestamp,
            token,
            IsOverridden,
            ReasonForChange));
    }

    public void Revoke(string revokedBy, DateTimeOffset timestamp, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revokedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (OverallStatus == SiteGreenlightStatus.Revoked)
        {
            throw new SiteGreenlightTransitionException(
                $"Gate for site {SiteId} is already in terminal state Revoked.");
        }

        OverallStatus = SiteGreenlightStatus.Revoked;
        GreenlightToken = null;
        ReasonForChange = reason;

        RecordEvent(new SiteGreenlightRevoked(
            Id,
            StudyId,
            SiteId,
            CountryCode,
            revokedBy,
            timestamp,
            reason));
    }

    public void Suspend(string suspendedBy, DateTimeOffset timestamp, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suspendedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (OverallStatus == SiteGreenlightStatus.Revoked)
        {
            throw new SiteGreenlightTransitionException(
                $"Cannot suspend greenlight gate for site {SiteId} because it is in terminal state Revoked.");
        }

        if (OverallStatus == SiteGreenlightStatus.Suspended)
        {
            throw new SiteGreenlightTransitionException(
                $"Gate for site {SiteId} is already suspended.");
        }

        OverallStatus = SiteGreenlightStatus.Suspended;
        ReasonForChange = reason;

        RecordEvent(new SiteGreenlightSuspended(
            Id,
            StudyId,
            SiteId,
            CountryCode,
            suspendedBy,
            timestamp,
            reason));
    }

    public void Override(string sponsorUserId, string medicalUserId, string justification, DateTimeOffset authorizedAt, string greenlightToken)
    {
        OverallStatus = SiteGreenlightStatus.Approved;
        IsOverridden = true;
        OverrideSponsorUserId = sponsorUserId;
        OverrideMedicalUserId = medicalUserId;
        OverrideJustification = justification;
        OverrideAuthorizedAt = authorizedAt;
        GreenlightToken = greenlightToken;
    }
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
