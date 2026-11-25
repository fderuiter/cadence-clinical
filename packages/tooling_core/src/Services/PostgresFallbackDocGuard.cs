using System;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Shared guard detecting stale documentation claims of a built-in <c>Development</c> /
/// <c>localhost:5432</c> PostgreSQL fallback that contradict ADR-2260 fail-closed startup.
/// </summary>
public static partial class PostgresFallbackDocGuard
{
    [GeneratedRegex(
        @"(?<!\b(?:no|never|not|without(?:\s+a|\s+any)?)\s+(?:built-in\s+|`?Development`?\s+|local\s+)*)(?:falls?\s+back|fallback)[^\r\n.]{0,140}(?:localhost:5432|local\s+(?:PostgreSQL|Postgres|database))|(?<!\b(?:no|without(?:\s+a|\s+any)?)\s+)built-in\s+`?localhost:5432`?\s+(?:Development\s+)?fallback|Neither\s+connection\s+key\s+set\s+outside\s+`?Development`?|fails\s+outside\s+`?Development`?\s+when\s+neither\s+is\s+set",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StalePostgresFallbackRegex();

    /// <summary>
    /// Checks whether <paramref name="content"/> contains a stale <c>Development</c> /
    /// <c>localhost:5432</c> PostgreSQL fallback claim.
    /// </summary>
    public static bool TryFindStalePostgresFallbackClaim(string content, out string matchedSnippet)
    {
        Match match = StalePostgresFallbackRegex().Match(content);
        if (match.Success)
        {
            matchedSnippet = match.Value.Trim();
            return true;
        }

        matchedSnippet = string.Empty;
        return false;
    }

    /// <summary>
    /// Validates <paramref name="text"/> for <paramref name="docLabel"/>, writing an error to
    /// <see cref="Console.Out"/> and returning <see langword="false"/> when a stale fallback claim is present.
    /// </summary>
    public static bool ValidateNoStalePostgresFallback(string text, string docLabel)
    {
        if (TryFindStalePostgresFallbackClaim(text, out string snippet))
        {
            Console.WriteLine(
                $"[ERROR] {docLabel} contains stale Development PostgreSQL fallback claim ('{snippet}'). AddCadenceNpgsql fails closed in every environment.");
            return false;
        }

        return true;
    }
}
