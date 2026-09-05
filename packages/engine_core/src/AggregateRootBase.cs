namespace Cadence.Core.Domain;

/// <summary>
/// Non-generic base carrying the 21 CFR Part 11 audit fields shared by every <see cref="AggregateRoot{TId}"/>,
/// letting infrastructure (e.g. <c>GxPAuditInterceptor</c>) pattern-match any aggregate regardless of its TId.
/// @req:PRD-SYS-053
/// </summary>
public abstract class AggregateRootBase : IGxPAuditable
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public long VersionIndex { get; set; } = 1;
}
