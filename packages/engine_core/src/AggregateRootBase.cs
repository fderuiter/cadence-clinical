namespace Cadence.Core.Domain;

/// <summary>
/// Non-generic base carrying the 21 CFR Part 11 audit fields shared by every <see cref="AggregateRoot{TId}"/>,
/// letting infrastructure (e.g. <c>GxPAuditInterceptor</c>) pattern-match any aggregate regardless of its TId.
/// </summary>
public abstract class AggregateRootBase : IGxPAuditable
{
    /// <summary>
    /// When the aggregate was first saved, in UTC. It has no default: the save path stamps it from the injected
    /// clock (the kernel command transaction, or <c>GxPAuditInterceptor</c> for services), never the wall clock
    /// (K-TIME-3).
    /// </summary>
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public long VersionIndex { get; set; } = 1;
}
