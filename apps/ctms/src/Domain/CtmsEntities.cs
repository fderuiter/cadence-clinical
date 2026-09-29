using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Ctms.Domain;

public sealed class CtmsAuditLog : AggregateRoot<string>
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
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
    public DateTime EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }

    public CtmsCraAllocation() : base(Guid.NewGuid().ToString()) { }
    public CtmsCraAllocation(string id) : base(id) { }
}

public sealed class CtmsDefeatedMonitoringVisit : AggregateRoot<string>
{
    public string VisitId { get; set; } = string.Empty;
    public DateTime? ActualDate { get; set; }
    public string? Findings { get; set; }
    public DateTime DeviceTimestamp { get; set; }
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
    public string VisitId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string ResolutionStatus { get; set; } = string.Empty;
    public string? OfflineSyncMarkers { get; set; }
    public string? SyncStatus { get; set; }

    public CtmsMonitoringVisitFinding() : base(Guid.NewGuid().ToString()) { }
    public CtmsMonitoringVisitFinding(string id) : base(id) { }
}

public sealed class CtmsMonitoringVisit : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string CraId { get; set; } = string.Empty;
    public string VisitType { get; set; } = string.Empty;
    public DateTime ScheduledDate { get; set; }
    public DateTime? ActualDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SignatureManifestation { get; set; }
    public string? Signer { get; set; }
    public DateTime? SigningTimestamp { get; set; }
    public string? OfflineSyncMarkers { get; set; }
    public string? SyncStatus { get; set; }

    public CtmsMonitoringVisit() : base(Guid.NewGuid().ToString()) { }
    public CtmsMonitoringVisit(string id) : base(id) { }
}

public sealed class CtmsRecruitmentRecord : AggregateRoot<string>
{
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public int ScreenedCount { get; set; }
    public int EnrolledCount { get; set; }
    public int TargetCount { get; set; }
    public DateTime AsOfDate { get; set; }

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
    public DateTime? SigningTimestamp { get; set; }

    public CtmsRegulatoryForm() : base(Guid.NewGuid().ToString()) { }
    public CtmsRegulatoryForm(string id) : base(id) { }
}

public sealed class CtmsSiteMilestone : AggregateRoot<string>
{
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string MilestoneType { get; set; } = string.Empty;
    public DateTime? PlannedDate { get; set; }
    public DateTime? ActualDate { get; set; }
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
    public DateTime? TriggeredAt { get; set; }

    public CtmsPaymentMilestone() : base(Guid.NewGuid().ToString()) { }
    public CtmsPaymentMilestone(string id) : base(id) { }
}

public sealed class DoaDelegationRecord : AggregateRoot<string>
{
    public string SiteId { get; set; } = string.Empty;
    public string StaffUserId { get; set; } = string.Empty;
    public string TaskCode { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PiSignatureHash { get; set; }
    public DateTime? PiApprovedAt { get; set; }
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
    public DateTime? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }

    public CtmsInvestigatorPayable() : base(Guid.NewGuid().ToString()) { }
    public CtmsInvestigatorPayable(string id) : base(id) { }
}
