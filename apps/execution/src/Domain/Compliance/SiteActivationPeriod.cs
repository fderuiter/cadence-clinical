using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Database.Temporal;

using NpgsqlTypes;

namespace Cadence.Apps.Execution.Domain.Compliance;

/// <summary>
/// Bitemporal period record for site compliance and activation state transitions (<c>K-TIME-1</c>, <c>K-TIME-2</c>, ADR-2246).
/// Preserves point-in-time reconstructability across <c>valid_period</c> and <c>recorded_period</c> without mutating historical state.
/// @req:PRD-SUB-001
/// @req:PRD-SYS-001
/// </summary>
public sealed class SiteActivationPeriod : IBitemporalPeriodEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SiteComplianceCacheId { get; set; }

    public StudyCode StudyCode { get; set; } = default!;

    public SiteCode SiteCode { get; set; }

    public string Milestone { get; set; } = string.Empty;

    public bool IsComplete { get; set; }

    public List<string> MissingDocuments { get; set; } = [];

    public bool IsActivated { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public string? ActivatedBy { get; set; }

    public string? ReasonForChange { get; set; }

    public NpgsqlRange<DateTimeOffset> ValidPeriod { get; set; }

    public NpgsqlRange<DateTimeOffset> RecordedPeriod { get; set; }
}
