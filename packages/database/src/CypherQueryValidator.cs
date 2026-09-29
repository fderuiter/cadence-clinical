using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.Database;

/// <summary>
/// Cypher query safety validator enforcing bounded variable-length relationship traversals for graph databases.
/// @req:PRD-SYS-001
/// </summary>
public static partial class CypherQueryValidator
{
    [GeneratedRegex(@"\[([^\]]+)\]", RegexOptions.Compiled)]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"^\d*\.\.$", RegexOptions.Compiled)]
    private static partial Regex UnboundedRangeRegex();

    /// <summary>
    /// Validates a Cypher query string to prevent unbounded variable-length traversals.
    /// Throws <see cref="InvalidOperationException"/> if an unsafe pattern is detected.
    /// </summary>
    /// <param name="query">The Cypher query string to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when an unbounded variable-length traversal is detected.</exception>
    public static void ValidateCypherQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        foreach (Match match in BracketRegex().Matches(query))
        {
            string content = match.Groups[1].Value;
            if (content.Contains('*'))
            {
                string starPart = content.Split('*', 2)[1];
                string starPartClean = string.Concat(starPart.Where(c => !char.IsWhiteSpace(c)));

                if (starPartClean == string.Empty || UnboundedRangeRegex().IsMatch(starPartClean))
                {
                    throw new InvalidOperationException(
                        $"Unsafe Cypher query: Unbounded variable-length traversal pattern detected in '{match.Value}'. " +
                        "All variable-length relationships must specify an upper bound (e.g., *1..5 or *..10).");
                }
            }
        }
    }
}
