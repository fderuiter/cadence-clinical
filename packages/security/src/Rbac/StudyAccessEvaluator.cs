using System;
using System.Linq;

namespace Cadence.Security.Rbac;

/// <summary>
/// Evaluates study-level scope access for study-restricted personas.
/// @req:PRD-SYS-053
/// </summary>
public static class StudyAccessEvaluator
{
    /// <summary>
    /// Checks if the principal has access to a specific study ID.
    /// Study-scoped users are restricted to their assigned studies or wildcard "*".
    /// </summary>
    public static bool CanAccessStudy(Principal? principal, string? studyId)
    {
        if (principal == null) return false;

        bool isExternalMonitor = principal.Roles.Any(r => string.Equals(r, RoleNormalization.ROLE_EXTERNAL_MONITOR, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(studyId))
        {
            return !isExternalMonitor && principal.AssignedStudies.Count == 0;
        }

        string targetStudy = studyId.Trim();

        if (isExternalMonitor)
        {
            return principal.AssignedStudies.Any(s =>
                s.Trim() == "*" || string.Equals(s.Trim(), targetStudy, StringComparison.OrdinalIgnoreCase));
        }

        if (principal.AssignedStudies.Count > 0)
        {
            return principal.AssignedStudies.Any(s =>
                s.Trim() == "*" || string.Equals(s.Trim(), targetStudy, StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }
}
