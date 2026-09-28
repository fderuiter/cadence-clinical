using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Quality.Domain;

public sealed class QualityDeviation : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsProtocolViolation { get; set; }

    public QualityDeviation() : base(Guid.NewGuid().ToString()) { }
    public QualityDeviation(string id) : base(id) { }
}

public sealed class QualityRootCauseAnalysis : AggregateRoot<string>
{
    public string DeviationId { get; set; } = string.Empty;
    public string Methodology { get; set; } = string.Empty;
    public string InvestigationDetails { get; set; } = string.Empty;
    public string RootCauseSummary { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }

    public QualityRootCauseAnalysis() : base(Guid.NewGuid().ToString()) { }
    public QualityRootCauseAnalysis(string id) : base(id) { }
}

public sealed class QualityCapaRecord : AggregateRoot<string>
{
    public string DeviationId { get; set; } = string.Empty;
    public string? RcaId { get; set; }
    public string CapaType { get; set; } = string.Empty;
    public string ActionPlan { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PreventiveMeasures { get; set; }
    public DateTime? TargetCompletionDate { get; set; }
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }

    public QualityCapaRecord() : base(Guid.NewGuid().ToString()) { }
    public QualityCapaRecord(string id) : base(id) { }
}

public sealed class QualityAuditLog : AggregateRoot<string>
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? RecordId { get; set; }
    public string? ChangeReason { get; set; }

    public QualityAuditLog() : base(Guid.NewGuid().ToString()) { }
    public QualityAuditLog(string id) : base(id) { }
}
