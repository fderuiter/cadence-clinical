using System;
using System.Collections.Generic;

namespace Cadence.Security.Rbac;

/// <summary>
/// Authenticated principal identity context capturing user ID, normalized roles, site/study scope assignments,
/// unblinded access status, sponsor ID, and change justification reason.
/// </summary>
public record Principal
{
    public string UserId { get; init; } = string.Empty;
    public string? TenantId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AssignedSites { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AssignedStudies { get; init; } = Array.Empty<string>();
    public bool UnblindedAccess { get; init; } = false;
    public string? SponsorId { get; init; }
    public string? ChangeReason { get; init; }
    public IReadOnlyList<string> RawRoles { get; init; } = Array.Empty<string>();
}
