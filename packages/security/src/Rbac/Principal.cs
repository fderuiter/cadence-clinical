using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Tenancy;
using Cadence.Security.Authentication;

namespace Cadence.Security.Rbac;

/// <summary>
/// Authenticated principal identity context capturing user ID, normalized roles, site/study scope assignments,
/// unblinded access status, sponsor ID, workspace commercial posture, and change justification reason.
/// </summary>
public record Principal
{
    public string UserId { get; init; } = string.Empty;
    public string? TenantId { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AssignedSites { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AssignedStudies { get; init; } = Array.Empty<string>();
    public bool UnblindedAccess { get; init; } = false;
    public bool TenantReadOnly { get; init; } = false;
    public IReadOnlyList<string> ProductFamilies { get; init; } = Array.Empty<string>();
    public TenantScopingCaps? TenantScoping { get; init; }
    public string? SponsorId { get; init; }
    public string? ChangeReason { get; init; }
    public IReadOnlyList<string> RawRoles { get; init; } = Array.Empty<string>();

    /// <summary>Returns true when the workspace holds an active entitlement for <paramref name="family"/>.</summary>
    public bool HasProductFamily(ProductFamily family)
    {
        string target = family.ToString();
        return ProductFamilies.Any(f => string.Equals(f, target, StringComparison.OrdinalIgnoreCase));
    }
}
