using System.Collections.Generic;
using System.Linq;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Funnel for every transition check (Rule 9). Reproduces `normalize_state`'s exact string shape
/// (`UPPER_SNAKE_CASE`) purely for parity with §5's fixed-string response bodies (e.g. "INVALID_STATE_TRANSITION");
/// internally the domain always works with the <see cref="SubjectState"/> enum, never bare strings.
/// @req:PRD-SUB-001
/// </summary>
public static class SubjectStateCodec
{
    private static readonly IReadOnlyDictionary<string, SubjectState> ByCode = new Dictionary<string, SubjectState>
    {
        ["SCREENING"] = SubjectState.Screening,
        ["SCREEN_FAILED"] = SubjectState.ScreenFailed,
        ["ENROLLED"] = SubjectState.Enrolled,
        ["RANDOMIZED"] = SubjectState.Randomized,
        ["ACTIVE"] = SubjectState.Active,
        ["UNBLINDED"] = SubjectState.Unblinded,
        ["COMPLETED"] = SubjectState.Completed,
        ["WITHDRAWN"] = SubjectState.Withdrawn,
        ["RECONSENT_REQUIRED"] = SubjectState.ReconsentRequired,
    };

    public static string ToCode(SubjectState state) =>
        ByCode.First(kv => kv.Value == state).Key;

    /// <summary>Mirrors `normalize_state`: trims, upper-cases, and replaces spaces with underscores before lookup.
    /// Returns false (rather than throwing) for unmapped input — callers decide how to surface that, matching
    /// raise site #6's "no distinct error code from a legal-but-forbidden transition."</summary>
    public static bool TryParse(string? raw, out SubjectState state)
    {
        state = default;
        if (raw is null) return false;
        string normalized = raw.Trim().ToUpperInvariant().Replace(' ', '_');
        return ByCode.TryGetValue(normalized, out state);
    }
}
