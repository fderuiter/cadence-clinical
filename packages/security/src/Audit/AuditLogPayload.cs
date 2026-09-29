using System;
using System.Collections.Generic;

namespace Cadence.Security.Audit;

/// <summary>
/// Schema for creating an auditable event log entry under 21 CFR Part 11.
/// @req:PRD-SYS-053
/// </summary>
public record AuditLogPayload
{
    public string ServiceName { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string TenantId { get; init; } = "tenant_default";
    public string ReasonForChange { get; init; } = string.Empty;
    public IDictionary<string, object?> Details { get; init; } = new Dictionary<string, object?>();
}
