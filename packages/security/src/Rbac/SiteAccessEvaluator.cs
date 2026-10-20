using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Cadence.Security.Rbac;

/// <summary>
/// Evaluates site-level scope access with fail-closed default enforcement for site-scoped trial personas.
/// @req:PRD-SYS-053
/// </summary>
public static class SiteAccessEvaluator
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
        Justification = "Public UPPER_SNAKE names ported from the Python reference (packages/security/rbac.py) and referenced across services; renaming them is a separate public API change.")]
    public static IReadOnlySet<string> SITE_SCOPED_ROLES { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RoleNormalization.ROLE_INVESTIGATOR,
        RoleNormalization.ROLE_CRC,
        RoleNormalization.ROLE_CRA_CANONICAL,
        "monitor",
        RoleNormalization.ROLE_EXTERNAL_MONITOR,
        RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR,
        RoleNormalization.ROLE_AUTHORIZED_ER_PHYSICIAN,
        RoleNormalization.ROLE_LEAD_INVESTIGATOR
    };

    /// <summary>
    /// Evaluates if the authenticated principal is permitted to access the given site ID.
    /// Site-scoped users receive a fail-closed access deny if they have no assigned sites.
    /// </summary>
    public static bool CanAccessSite(Principal? principal, string? siteId)
    {
        if (principal == null) return false;

        bool hasSiteScopedRoles = principal.Roles.Any(r => SITE_SCOPED_ROLES.Contains(r));

        if (string.IsNullOrWhiteSpace(siteId))
        {
            return !hasSiteScopedRoles && principal.AssignedSites.Count == 0;
        }

        string targetSite = siteId.Trim();

        if (hasSiteScopedRoles)
        {
            return principal.AssignedSites.Any(s => string.Equals(s.Trim(), targetSite, StringComparison.OrdinalIgnoreCase));
        }

        if (principal.AssignedSites.Count > 0)
        {
            return principal.AssignedSites.Any(s => string.Equals(s.Trim(), targetSite, StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }
}
