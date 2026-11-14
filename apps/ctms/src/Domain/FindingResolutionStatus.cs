using System.Collections.Generic;
using System.Linq;

namespace Cadence.Apps.Ctms.Domain;

/// <summary>
/// Explicit lifecycle states for clinical trial monitoring visit findings.
/// </summary>
public enum FindingResolutionStatus
{
    Open,
    UnderReview,
    Resolved,
    Closed
}

/// <summary>
/// Codec for converting between FindingResolutionStatus enum and canonical UPPER_SNAKE_CASE strings.
/// </summary>
public static class FindingResolutionStatusCodec
{
    private static readonly IReadOnlyDictionary<string, FindingResolutionStatus> ByCode = new Dictionary<string, FindingResolutionStatus>
    {
        ["OPEN"] = FindingResolutionStatus.Open,
        ["UNDER_REVIEW"] = FindingResolutionStatus.UnderReview,
        ["RESOLVED"] = FindingResolutionStatus.Resolved,
        ["CLOSED"] = FindingResolutionStatus.Closed,
    };

    public static string ToCode(FindingResolutionStatus state) =>
        ByCode.First(kv => kv.Value == state).Key;

    public static bool TryParse(string? raw, out FindingResolutionStatus state)
    {
        state = default;
        if (raw is null) return false;
        string normalized = raw.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
        return ByCode.TryGetValue(normalized, out state);
    }

    public static FindingResolutionStatus Parse(string? raw) =>
        TryParse(raw, out var state) ? state : FindingResolutionStatus.Open;
}
