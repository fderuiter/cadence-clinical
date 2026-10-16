using System;
using System.Linq;

namespace Cadence.Security.Rbac;

/// <summary>
/// Evaluates study-level scope from the studies the gateway signs for the caller (<c>X-Study-Ids</c>). It fails closed
/// (ADR-2516): a blank study id, or a caller assigned to no study, is a denial; <c>*</c> stands for every study. The
/// signed list is the interim source until the kernel's grants decide study scope for every module (#4781).
/// @req:PRD-SYS-053
/// </summary>
public static class StudyAccessEvaluator
{
    /// <summary>The assignment that covers every study.</summary>
    public const string AllStudies = "*";

    /// <summary>True when <paramref name="principal"/> is assigned to <paramref name="studyId"/> or to every study.</summary>
    public static bool CanAccessStudy(Principal? principal, string? studyId)
    {
        if (principal is null || string.IsNullOrWhiteSpace(studyId))
        {
            return false;
        }

        string targetStudy = studyId.Trim();
        return principal.AssignedStudies.Any(s =>
            string.Equals(s.Trim(), AllStudies, StringComparison.Ordinal)
            || string.Equals(s.Trim(), targetStudy, StringComparison.Ordinal));
    }
}
