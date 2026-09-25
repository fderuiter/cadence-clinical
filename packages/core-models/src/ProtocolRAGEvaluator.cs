#pragma warning disable S101 // Type names should comply with a configured naming convention

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.CoreModels;

/// <summary>
/// Core Protocol RAG Domain Matchers, Citation Generators, and Faithfulness Evaluator.
/// Enforces 21 CFR Part 11 and CDISC GxP compliance for Grounded Protocol AI Assistance.
/// Requirements: PRD-TCK-005, PRD-SYS-051, ADR-2192
/// </summary>
public static partial class ProtocolRAGEvaluator
{
    /// <summary>
    /// Canonical verbatim citation format regex per PRD-TCK-005.
    /// </summary>
    public static readonly Regex CitationRegex = new(
        @"\[Protocol\s+([a-zA-Z0-9.\-_]+)(?:,\s*Section\s*([0-9a-zA-Z.\-_]+))?,\s*Page\s*(\d+)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Minimum confidence threshold for grounded RAG answers.
    /// </summary>
    public const double ConfidenceThreshold = 0.85;

    /// <summary>
    /// System prompt template for Tier 2 Grounded RAG model invocation.
    /// </summary>
    public const string Tier2GroundedRagSystemPrompt = @"You are Cadence Clinical Research AI Assistant operating under strict Tier 2 Grounded RAG constraints.
You assist Principal Investigators, Clinical Research Coordinators (CRCs), Clinical Research Associates (CRAs), and Data Managers.

MANDATORY CLINICAL SAFETY & GXP RULES:
1. Grounding Guarantee: You MUST answer the user's clinical question SOLELY and EXCLUSIVELY using the verified Study Protocol excerpts provided below.
2. Verbatim Citation Syntax: Every factual claim, timeframe, inclusion/exclusion criterion, dosage specification, or procedural guideline MUST be immediately followed by its exact structural citation marker:
   Format: `[Protocol {version}, Section {section_number}, Page {page_number}]`
   Example: `Serious adverse events must be reported within 24 hours of site awareness [Protocol v2.1, Section 7.3, Page 42].`
3. Zero Speculation / Fail-Closed: If the provided protocol excerpts do NOT contain sufficient information to answer the question with 100% confidence, you MUST state:
   ""The provided study protocol excerpts do not contain sufficient information to answer this inquiry. Please escalate to the Lead Study CRA or Data Management team.""
4. Version Integrity: NEVER reference protocol versions or sections that are not explicitly provided in the verified context blocks.
";

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "above", "after", "again", "against", "all", "and", "any", "are", "asking",
        "because", "been", "before", "being", "below", "between", "both", "but", "cannot", "could",
        "did", "does", "doing", "down", "during", "each", "few", "for", "from", "further",
        "had", "has", "have", "having", "her", "here", "hers", "him", "his", "how",
        "into", "its", "many", "more", "most", "much", "myself", "nor", "not", "off",
        "once", "only", "other", "ought", "our", "ours", "out", "over", "own", "query",
        "question", "same", "she", "should", "some", "such", "than", "that", "the", "their",
        "theirs", "them", "then", "there", "these", "they", "this", "those", "through", "too",
        "under", "until", "very", "was", "were", "what", "when", "where", "which", "while",
        "who", "whom", "why", "with", "would", "you", "your", "yours"
    };

    /// <summary>
    /// Formats canonical verbatim citation marker per PRD-TCK-005.
    /// </summary>
    /// <param name="protocolVersion">Protocol version string (e.g. 'v2.1' or '2.1').</param>
    /// <param name="sectionNumber">Optional protocol section number.</param>
    /// <param name="pageNumber">1-indexed page number.</param>
    /// <returns>Formatted citation marker string.</returns>
    public static string FormatCitationMarker(string protocolVersion, string? sectionNumber, int pageNumber)
    {
        var versionClean = protocolVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? protocolVersion
            : $"v{protocolVersion}";

        if (!string.IsNullOrWhiteSpace(sectionNumber))
        {
            return $"[Protocol {versionClean}, Section {sectionNumber}, Page {pageNumber}]";
        }

        return $"[Protocol {versionClean}, Page {pageNumber}]";
    }

    /// <summary>
    /// Formats the Tier 2 Grounded RAG user prompt containing structured excerpt blocks.
    /// </summary>
    /// <param name="query">Clinical inquiry from support ticket or user.</param>
    /// <param name="chunks">List of retrieved protocol context chunks.</param>
    /// <returns>Formatted prompt string.</returns>
    public static string FormatGroundedRagPrompt(string query, IReadOnlyList<ProtocolRAGContextChunk>? chunks)
    {
        if (chunks == null || chunks.Count == 0)
        {
            return $@"--- PROTOCOL EXCERPTS ---
(No approved protocol excerpts found for this study)
--- END PROTOCOL EXCERPTS ---

CLINICAL INQUIRY:
{query}

Please formulate a helpful, concise clinical response with exact citations.";
        }

        var excerptsFormatted = new List<string>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            var secInfo = !string.IsNullOrWhiteSpace(c.SectionNumber)
                ? $"Section {c.SectionNumber} ({c.SectionTitle})"
                : "General";

            excerptsFormatted.Add(
                $"[EXCERPT {i + 1}: Protocol {c.ProtocolVersion}, {secInfo}, Page {c.PageNumber}]\n{c.ChunkText.Trim()}\n");
        }

        var joinedExcerpts = string.Join("\n", excerptsFormatted);
        return $@"--- PROTOCOL EXCERPTS ---
{joinedExcerpts}
--- END PROTOCOL EXCERPTS ---

CLINICAL INQUIRY:
{query}

Please formulate a helpful, concise clinical response strictly citing the excerpts above.";
    }

    /// <summary>
    /// Computes mathematical faithfulness score and validates citation grounding.
    /// </summary>
    /// <param name="query">User inquiry string.</param>
    /// <param name="answer">Generated answer string containing verbatim citations.</param>
    /// <param name="chunks">List of verified protocol context chunks.</param>
    /// <returns>Tuple containing faithfulness score, parsed citations list, and grounded boolean flag.</returns>
    public static (double FaithfulnessScore, IReadOnlyList<ParsedCitation> Citations, bool IsGrounded) EvaluateFaithfulness(
        string query,
        string answer,
        IReadOnlyList<ProtocolRAGContextChunk>? chunks)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return (0.0, Array.Empty<ParsedCitation>(), false);
        }

        var lowerAns = answer.ToLowerInvariant();
        if (lowerAns.Contains("insufficient information")
            || lowerAns.Contains("does not contain")
            || lowerAns.Contains("no approved protocol"))
        {
            return (0.0, Array.Empty<ParsedCitation>(), false);
        }

        if (chunks == null || chunks.Count == 0)
        {
            return (0.0, Array.Empty<ParsedCitation>(), false);
        }

        var matches = CitationRegex.Matches(answer);
        if (matches.Count == 0)
        {
            return (0.0, Array.Empty<ParsedCitation>(), false);
        }

        var chunkMap = new Dictionary<(string VersionNorm, int PageNumber), ProtocolRAGContextChunk>();
        foreach (var c in chunks)
        {
            var normV = c.ProtocolVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? c.ProtocolVersion.ToLowerInvariant()
                : $"v{c.ProtocolVersion.ToLowerInvariant()}";
            chunkMap[(normV, c.PageNumber)] = c;
        }

        var parsedCitations = new List<ParsedCitation>();
        var validCitationsCount = 0;
        var approvedCitationsCount = 0;
        var contentOverlapScores = new List<double>();

        foreach (Match match in matches)
        {
            var rawMarker = match.Value;
            var vRaw = match.Groups[1].Value;
            var secNum = match.Groups[2].Success ? match.Groups[2].Value : null;
            var pageNum = int.Parse(match.Groups[3].Value);

            var vNorm = vRaw.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? vRaw.ToLowerInvariant()
                : $"v{vRaw.ToLowerInvariant()}";

            if (chunkMap.TryGetValue((vNorm, pageNum), out var matchedChunk))
            {
                validCitationsCount++;
                if (matchedChunk.IsApproved)
                {
                    approvedCitationsCount++;
                }

                var matchStart = match.Index;
                var claimStartIndex = Math.Max(0, matchStart - 200);
                var claimWindow = answer[claimStartIndex..matchStart];

                var claimWords = Regex.Matches(claimWindow.ToLowerInvariant(), @"\w{4,}")
                    .Select(m => m.Value)
                    .ToHashSet();

                var chunkWords = Regex.Matches(matchedChunk.ChunkText.ToLowerInvariant(), @"\w{4,}")
                    .Select(m => m.Value)
                    .ToHashSet();

                if (claimWords.Count > 0)
                {
                    var overlap = (double)claimWords.Intersect(chunkWords).Count() / claimWords.Count;
                    contentOverlapScores.Add(Math.Min(1.0, overlap + 0.3));
                }
                else
                {
                    contentOverlapScores.Add(0.8);
                }

                parsedCitations.Add(new ParsedCitation(
                    ProtocolVersion: matchedChunk.ProtocolVersion,
                    SectionNumber: secNum ?? matchedChunk.SectionNumber,
                    SectionTitle: matchedChunk.SectionTitle,
                    PageNumber: pageNum,
                    CitationMarker: rawMarker,
                    IsApproved: matchedChunk.IsApproved,
                    IsVerified: true
                ));
            }
            else
            {
                parsedCitations.Add(new ParsedCitation(
                    ProtocolVersion: vRaw,
                    SectionNumber: secNum,
                    SectionTitle: null,
                    PageNumber: pageNum,
                    CitationMarker: rawMarker,
                    IsApproved: false,
                    IsVerified: false
                ));
            }
        }

        var totalCitations = matches.Count;
        var citationValidityRate = totalCitations > 0 ? (double)validCitationsCount / totalCitations : 0.0;
        var approvalRate = totalCitations > 0 ? (double)approvedCitationsCount / totalCitations : 0.0;
        var avgOverlap = contentOverlapScores.Count > 0 ? contentOverlapScores.Average() : 0.0;

        var faithfulnessScore = (0.50 * citationValidityRate) + (0.30 * approvalRate) + (0.20 * avgOverlap);

        var queryWords = Regex.Matches(query.ToLowerInvariant(), @"\b[a-z]{3,}\b")
            .Select(m => m.Value)
            .Where(w => !Stopwords.Contains(w))
            .ToHashSet();

        var combinedChunkText = string.Join(" ", chunks.Select(c => c.ChunkText.ToLowerInvariant()));
        var allChunkWords = Regex.Matches(combinedChunkText, @"\b[a-z]{3,}\b")
            .Select(m => m.Value)
            .Where(w => !Stopwords.Contains(w))
            .ToHashSet();

        var queryOverlap = queryWords.Count > 0
            ? (double)queryWords.Intersect(allChunkWords).Count() / queryWords.Count
            : 0.0;

        if (queryOverlap < 0.25)
        {
            faithfulnessScore *= queryOverlap / 0.25;
        }

        if (validCitationsCount < totalCitations)
        {
            faithfulnessScore *= 0.5;
        }

        faithfulnessScore = Math.Round(Math.Max(0.0, Math.Min(1.0, faithfulnessScore)), 4);
        var isGrounded = faithfulnessScore >= 0.85
            && validCitationsCount == totalCitations
            && approvedCitationsCount == totalCitations
            && queryOverlap >= 0.25;

        return (faithfulnessScore, parsedCitations, isGrounded);
    }
}
