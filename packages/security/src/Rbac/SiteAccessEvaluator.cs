using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Security.Rbac;

/// <summary>
/// Evaluates site-level scope access with fail-closed default enforcement for site-scoped trial personas.
/// @req:PRD-SYS-053
/// </summary>
public static class SiteAccessEvaluator
{
    public static IReadOnlySet<string> SITE_SCOPED_ROLES { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RoleNormalization.ROLE_INVESTIGATOR,
        RoleNormalization.ROLE_CRC,
        RoleNormalization.ROLE_CRA_CANONICAL,
        "monitor",
        RoleNormalization.ROLE_EXTERNAL_MONITOR,
        RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR,
        RoleNormalization.ROLE_AUTHORIZED_ER_PHYSICIAN,
        RoleNormalization.ROLE_LEAD_INVESTIGATOR,
        RoleNormalization.ROLE_PHARMACIST,
        RoleNormalization.ROLE_SUBJECT
    };

    /// <summary>
    /// Evaluates if the authenticated principal is permitted to access the given site ID.
    /// Site-scoped users receive a fail-closed access deny if they have no assigned sites.
    /// </summary>
    public static bool CanAccessSite(Principal? principal, string? siteId)
    {
        if (principal == null) return false;

        bool isSiteBound = ClinicalScopeGuard.IsSiteBound(principal);

        if (string.IsNullOrWhiteSpace(siteId))
        {
            return !isSiteBound;
        }

        string targetSite = siteId.Trim();

        if (isSiteBound)
        {
            return principal.AssignedSites.Any(s => string.Equals(s.Trim(), targetSite, StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }
}
