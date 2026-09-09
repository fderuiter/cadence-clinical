using System.Collections.Generic;
using System.Linq;

namespace Cadence.Apps.Execution.Domain.Forms;

/// <summary>
/// Codec for mapping <see cref="FormSubmissionStatus"/> to/from wire-representation strings.
/// @req:PRD-SYS-001
/// @req:SRS-EDC-002
/// </summary>
public static class FormSubmissionStatusCodec
{
    private static readonly IReadOnlyDictionary<string, FormSubmissionStatus> ByCode = new Dictionary<string, FormSubmissionStatus>
    {
        ["DRAFT"] = FormSubmissionStatus.Draft,
        ["COMPLETED"] = FormSubmissionStatus.Completed,
        ["APPROVED"] = FormSubmissionStatus.Approved,
    };

    public static string ToCode(FormSubmissionStatus status) =>
        ByCode.First(kv => kv.Value == status).Key;

    public static bool TryParse(string? raw, out FormSubmissionStatus status)
    {
        status = default;
        if (raw is null) return false;
        string normalized = raw.Trim().ToUpperInvariant();
        return ByCode.TryGetValue(normalized, out status);
    }
}
