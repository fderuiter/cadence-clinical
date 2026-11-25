using System;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Extracts explicit Markdown <c>Status</c> metadata headers from documentation preambles
/// (prior to the first <c>##</c> heading) for <c>docs/DOCUMENTATION_INDEX.md</c> parity checks.
/// </summary>
public static partial class DocIndexStatusHeaderParser
{
    [GeneratedRegex(
        @"^\s*\|\s*\*{0,2}Status\*{0,2}\s*\|\s*\*{0,2}(?<status>Proposed|Draft|Current|Active|Accepted|Normative|Baseline|Historical|Deprecated|Superseded|Archived|Generated)\b(?<rest>[^\r\n|]*)|^\s*(?:[-*>]\s*)?\*\*Status:?\*\*:?\s*\*{0,2}(?<lineStatus>Proposed|Draft|Current|Active|Accepted|Normative|Baseline|Historical|Deprecated|Superseded|Archived|Generated)\b(?<lineRest>[^\r\n]*)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitStatusHeaderRegex();

    [GeneratedRegex(@"^##\s+", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SecondLevelHeadingRegex();

    /// <summary>
    /// Attempts to extract a normalized index <c>Status</c> value from a Markdown document's preamble
    /// (before the first second-level heading), also reporting whether the header carries a
    /// <c>Normative once accepted</c> qualification.
    /// </summary>
    public static bool TryExtractExplicitHeaderStatus(
        string fileContent,
        out string mappedStatus,
        out bool normativelyQualified)
    {
        mappedStatus = string.Empty;
        normativelyQualified = false;
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return false;
        }

        Match h2Match = SecondLevelHeadingRegex().Match(fileContent);
        string preamble = h2Match.Success ? fileContent[..h2Match.Index] : fileContent;
        Match statusMatch = ExplicitStatusHeaderRegex().Match(preamble);
        if (!statusMatch.Success)
        {
            return false;
        }

        string rawKeyword = statusMatch.Groups["status"].Success
            ? statusMatch.Groups["status"].Value
            : statusMatch.Groups["lineStatus"].Value;
        string rest = statusMatch.Groups["rest"].Success
            ? statusMatch.Groups["rest"].Value
            : statusMatch.Groups["lineRest"].Value;

        mappedStatus = rawKeyword.ToLowerInvariant() switch
        {
            "proposed" or "draft" => "Proposed",
            "current" or "active" or "accepted" => "Current",
            "normative" => "Normative",
            "baseline" => "Baseline",
            "historical" or "deprecated" or "superseded" or "archived" => "Historical",
            "generated" => "Generated",
            _ => "Current",
        };
        normativelyQualified = rest.Contains("Normative", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(mappedStatus, "Normative", StringComparison.Ordinal);
        return true;
    }
}
