using System.Collections.Generic;
using System.Linq;

namespace Cadence.Apps.Execution.Domain.Queries;

/// <summary>
/// Codec for mapping <see cref="QueryStatus"/> to/from wire-representation strings.
/// @req:PRD-SYS-007
/// @req:SRS-EDC-004
/// </summary>
public static class QueryStatusCodec
{
    private static readonly IReadOnlyDictionary<string, QueryStatus> ByCode = new Dictionary<string, QueryStatus>
    {
        ["NONE"] = QueryStatus.None,
        ["CANDIDATE"] = QueryStatus.Candidate,
        ["DRAFT_AI"] = QueryStatus.DraftAi,
        ["OPEN"] = QueryStatus.Open,
        ["ANSWERED"] = QueryStatus.Answered,
        ["RESOLVED"] = QueryStatus.Resolved,
        ["CLOSED"] = QueryStatus.Closed,
        ["REOPENED"] = QueryStatus.Reopened,
        ["CANCELLED"] = QueryStatus.Cancelled,
    };

    public static string ToCode(QueryStatus status) =>
        ByCode.First(kv => kv.Value == status).Key;

    public static bool TryParse(string? raw, out QueryStatus status)
    {
        status = default;
        if (raw is null) return false;
        string normalized = raw.Trim().ToUpperInvariant();
        return ByCode.TryGetValue(normalized, out status);
    }
}
