using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Apps.Knowledge.Domain;

namespace Cadence.Apps.Knowledge.Application;

/// <summary>
/// Deterministic Hierarchical Specificity Scoring engine for contextual SOP resolution (CONTEXT.md, PRD-KNB-002).
/// Ranks active <see cref="ContextualHelpMapping"/> records by:
/// 1. Route pattern specificity: exact route match &gt; parameterized segment match &gt; longest prefix wildcard &gt; global wildcard.
/// 2. Persona specificity: exact normalized persona match &gt; universal wildcard (<c>null</c> or <c>*</c>).
/// 3. Explicit <see cref="ContextualHelpMapping.Priority"/> weight (descending).
/// 4. Recency tie-breaking via <see cref="ContextualHelpMapping.CreatedAt"/> (descending).
/// @req:PRD-KNB-002
/// </summary>
public static class ContextualHelpScorer
{
    public sealed record ScoredMapping(
        ContextualHelpMapping Mapping,
        KnowledgeArticle Article,
        int RouteSpecificityScore,
        int PersonaSpecificityScore,
        int ExactQueryMatchCount = 0);

    public sealed record ContextualResolution(
        ContextualHelpMapping? MatchedMapping,
        KnowledgeArticle? PrimaryArticle,
        KnowledgeArticleVersion? PrimaryVersion,
        string? SectionAnchor,
        IReadOnlyList<KnowledgeArticle> RelatedArticles);

    private static readonly Dictionary<string, string> PersonaAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["super_admin"] = "super_admin",
        ["sponsor_admin"] = "super_admin",
        ["sysadmin"] = "super_admin",
        ["system_admin"] = "super_admin",
        ["admin"] = "super_admin",

        ["sponsor_designer"] = "sponsor_designer",
        ["study_designer"] = "sponsor_designer",
        ["designer"] = "sponsor_designer",
        ["protocol_reviewer"] = "sponsor_designer",
        ["reviewer"] = "sponsor_designer",

        ["data_manager"] = "data_manager",
        ["sponsor_dm"] = "data_manager",
        ["dm"] = "data_manager",
        ["sponsor_statistician"] = "data_manager",
        ["unblinded_statistician"] = "data_manager",
        ["idmc"] = "data_manager",
        ["terminology_manager"] = "data_manager",

        ["safety_officer"] = "safety_officer",
        ["safety_reviewer"] = "safety_officer",
        ["safety_physician"] = "safety_officer",
        ["medical_monitor"] = "safety_officer",
        ["sponsor_mm"] = "safety_officer",
        ["mm"] = "safety_officer",

        ["site_crc"] = "site_crc",
        ["crc"] = "site_crc",
        ["site_coordinator"] = "site_crc",
        ["study_coordinator"] = "site_crc",
        ["pharmacist"] = "site_crc",

        ["site_investigator"] = "site_investigator",
        ["investigator"] = "site_investigator",
        ["principal_investigator"] = "site_investigator",
        ["pi"] = "site_investigator",
        ["sub_investigator"] = "site_investigator",
        ["authorized_er_physician"] = "site_investigator",
        ["lead_investigator"] = "site_investigator",
        ["investigator_user"] = "site_investigator",

        ["cra_monitor"] = "cra_monitor",
        ["cra"] = "cra_monitor",
        ["monitor"] = "cra_monitor",
        ["external_monitor"] = "cra_monitor",
        ["finance_officer"] = "cra_monitor",

        ["auditor"] = "auditor",
        ["tmf_auditor"] = "auditor",
        ["inspector"] = "auditor",
        ["regulatory_inspector"] = "auditor",
    };

    /// <summary>Normalizes a UI route path by stripping query/hash fragments and trailing slashes.</summary>
    public static string NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        string clean = route.Trim();
        int queryIdx = clean.IndexOfAny(['?', '#']);
        if (queryIdx >= 0)
        {
            clean = clean[..queryIdx];
        }

        if (!clean.StartsWith('/'))
        {
            clean = "/" + clean;
        }

        clean = clean.TrimEnd('/');
        return clean.Length == 0 ? "/" : clean;
    }

    /// <summary>Normalizes a persona key or RBAC role into one of the canonical workspace persona identifiers.</summary>
    public static string NormalizePersona(string? persona)
    {
        if (string.IsNullOrWhiteSpace(persona))
        {
            return "super_admin";
        }

        string trimmed = persona.Trim();
        if (trimmed == "*")
        {
            return "*";
        }

        return PersonaAliases.TryGetValue(trimmed, out string? mapped)
            ? mapped
            : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Computes the route specificity score for <paramref name="rawPattern"/> against <paramref name="route"/>,
    /// including explicit query-string filter bonuses when present on <paramref name="rawPattern"/>,
    /// or returns <c>-1</c> when the pattern does not match.
    /// </summary>
    public static int ScoreRoutePattern(string route, string? rawPattern) =>
        ScoreRoutePatternDetailed(route, rawPattern).Score;

    private static (int Score, int ExactQueryMatches) ScoreRoutePatternDetailed(string route, string? rawPattern)
    {
        if (string.IsNullOrWhiteSpace(rawPattern))
        {
            return (-1, 0);
        }

        string trimmedPattern = rawPattern.Trim();
        int patternQueryIdx = trimmedPattern.IndexOfAny(['?', '#']);
        string patternPath = patternQueryIdx >= 0 ? trimmedPattern[..patternQueryIdx] : trimmedPattern;

        int basePathScore = ScorePathPattern(NormalizeRoute(route), patternPath);
        if (basePathScore < 0)
        {
            return (-1, 0);
        }

        Dictionary<string, string> patternQuery = ExtractQueryParameters(trimmedPattern);
        if (patternQuery.Count == 0)
        {
            return (basePathScore, 0);
        }

        Dictionary<string, string> routeQuery = ExtractQueryParameters(route);
        int exactQueryMatches = 0;

        foreach ((string key, string expectedValue) in patternQuery)
        {
            if (!routeQuery.TryGetValue(key, out string? actualValue) || string.IsNullOrWhiteSpace(actualValue))
            {
                return (-1, 0);
            }

            if (IsWildcardOrParameterQueryValue(expectedValue))
            {
                continue;
            }

            if (!string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return (-1, 0);
            }

            exactQueryMatches++;
        }

        return (basePathScore + (patternQuery.Count * 300), exactQueryMatches);
    }

    private static int ScorePathPattern(string normalizedRoute, string rawPatternPath)
    {
        string pattern = rawPatternPath.Trim();
        if (pattern.Length == 0)
        {
            pattern = "/";
        }

        if (pattern is "*" or "/*")
        {
            return 1000;
        }

        if (!pattern.StartsWith('/'))
        {
            pattern = "/" + pattern;
        }

        string[] routeSegments = SplitSegments(normalizedRoute);

        // Prefix wildcard pattern: e.g. "/studies/*" or "/studies/:studyCode/*"
        if (pattern.EndsWith("/*", StringComparison.Ordinal))
        {
            string prefixPart = pattern[..^2];
            string normalizedPrefix = prefixPart.Length == 0 ? "/" : prefixPart.TrimEnd('/');
            if (normalizedPrefix.Length == 0)
            {
                return 1000;
            }

            string[] prefixSegments = SplitSegments(normalizedPrefix);
            if (routeSegments.Length < prefixSegments.Length)
            {
                return -1;
            }

            int literalMatches = 0;
            for (int i = 0; i < prefixSegments.Length; i++)
            {
                string patSeg = prefixSegments[i];
                string routeSeg = routeSegments[i];
                if (IsParameterSegment(patSeg))
                {
                    if (string.IsNullOrWhiteSpace(routeSeg))
                    {
                        return -1;
                    }
                }
                else if (string.Equals(patSeg, routeSeg, StringComparison.OrdinalIgnoreCase))
                {
                    literalMatches++;
                }
                else
                {
                    return -1;
                }
            }

            // Longer prefixes and literal prefix segments outrank shorter prefixes; exact/parameterized full matches stay in 3000+/4000+.
            return 2000 + (prefixSegments.Length * 25) + (literalMatches * 10);
        }

        string cleanPattern = pattern.TrimEnd('/');
        if (cleanPattern.Length == 0)
        {
            cleanPattern = "/";
        }

        string[] patternSegments = SplitSegments(cleanPattern);
        bool hasParameter = patternSegments.Any(IsParameterSegment);

        if (!hasParameter)
        {
            return string.Equals(normalizedRoute, cleanPattern, StringComparison.OrdinalIgnoreCase)
                ? 4000 + (routeSegments.Length * 10)
                : -1;
        }

        if (patternSegments.Length != routeSegments.Length)
        {
            return -1;
        }

        int literalSegmentCount = 0;
        for (int i = 0; i < patternSegments.Length; i++)
        {
            string patSeg = patternSegments[i];
            string routeSeg = routeSegments[i];
            if (IsParameterSegment(patSeg))
            {
                if (string.IsNullOrWhiteSpace(routeSeg))
                {
                    return -1;
                }
            }
            else if (string.Equals(patSeg, routeSeg, StringComparison.OrdinalIgnoreCase))
            {
                literalSegmentCount++;
            }
            else
            {
                return -1;
            }
        }

        return 3000 + (literalSegmentCount * 15) + routeSegments.Length;
    }

    private static Dictionary<string, string> ExtractQueryParameters(string? rawRouteOrPattern)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawRouteOrPattern))
        {
            return result;
        }

        string trimmed = rawRouteOrPattern.Trim();
        int hashIdx = trimmed.IndexOf('#', StringComparison.Ordinal);
        if (hashIdx >= 0)
        {
            trimmed = trimmed[..hashIdx];
        }

        int queryIdx = trimmed.IndexOf('?', StringComparison.Ordinal);
        if (queryIdx < 0 || queryIdx == trimmed.Length - 1)
        {
            return result;
        }

        string queryString = trimmed[(queryIdx + 1)..];
        foreach (string pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eqIdx = pair.IndexOf('=', StringComparison.Ordinal);
            string rawKey = eqIdx >= 0 ? pair[..eqIdx] : pair;
            string rawValue = eqIdx >= 0 ? pair[(eqIdx + 1)..] : "*";

            string key = SafeUnescapeQueryComponent(rawKey).Trim();
            if (key.Length == 0)
            {
                continue;
            }

            string value = SafeUnescapeQueryComponent(rawValue).Trim();
            result[key] = value;
        }

        return result;
    }

    private static string SafeUnescapeQueryComponent(string raw)
    {
        string plusNormalized = raw.Replace('+', ' ');
        try
        {
            return Uri.UnescapeDataString(plusNormalized);
        }
        catch (UriFormatException)
        {
            return plusNormalized;
        }
    }

    private static bool IsWildcardOrParameterQueryValue(string expectedValue) =>
        string.Equals(expectedValue, "*", StringComparison.Ordinal)
        || IsParameterSegment(expectedValue);

    /// <summary>
    /// Computes persona specificity: <c>1</c> for an exact normalized persona match, <c>0</c> for universal wildcard,
    /// and <c>-1</c> when the mapping targets a different persona.
    /// </summary>
    public static int ScorePersona(string targetPersona, string? mappingPersona)
    {
        if (string.IsNullOrWhiteSpace(mappingPersona) || string.Equals(mappingPersona.Trim(), "*", StringComparison.Ordinal))
        {
            return 0;
        }

        string normalizedTarget = NormalizePersona(targetPersona);
        string normalizedMapping = NormalizePersona(mappingPersona);
        return string.Equals(normalizedTarget, normalizedMapping, StringComparison.OrdinalIgnoreCase) ? 1 : -1;
    }

    /// <summary>
    /// Resolves the highest-specificity SOP article, locked snapshot version, section anchor, and up to 3 related
    /// published articles for the given <paramref name="route"/> and <paramref name="persona"/>.
    /// </summary>
    public static ContextualResolution Resolve(
        string? route,
        string? persona,
        IEnumerable<ContextualHelpMapping> mappings,
        IReadOnlyDictionary<string, KnowledgeArticle> publishedArticlesById,
        IReadOnlyDictionary<string, KnowledgeArticleVersion> versionsById)
    {
        string rawRoute = string.IsNullOrWhiteSpace(route) ? "/" : route.Trim();
        string normalizedPersona = NormalizePersona(persona);

        var scored = new List<ScoredMapping>();
        foreach (var mapping in mappings)
        {
            if (!mapping.IsActive)
            {
                continue;
            }

            if (!publishedArticlesById.TryGetValue(mapping.ArticleId, out var article)
                || article.IsDeleted
                || !string.Equals(article.Status, "PUBLISHED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            (int routeScore, int exactQueryMatches) = ScoreRoutePatternDetailed(rawRoute, mapping.RoutePattern);
            if (routeScore < 0)
            {
                continue;
            }

            int personaScore = ScorePersona(normalizedPersona, mapping.Persona);
            if (personaScore < 0)
            {
                continue;
            }

            scored.Add(new ScoredMapping(mapping, article, routeScore, personaScore, exactQueryMatches));
        }

        if (scored.Count == 0)
        {
            return new ContextualResolution(null, null, null, null, []);
        }

        var ordered = scored
            .OrderByDescending(s => s.RouteSpecificityScore)
            .ThenByDescending(s => s.PersonaSpecificityScore)
            .ThenByDescending(s => s.Mapping.Priority)
            .ThenByDescending(s => s.ExactQueryMatchCount)
            .ThenByDescending(s => s.Mapping.CreatedAt)
            .ThenByDescending(s => s.Mapping.Id, StringComparer.Ordinal)
            .ToList();

        ScoredMapping winner = ordered[0];
        KnowledgeArticle primaryArticle = winner.Article;
        KnowledgeArticleVersion? primaryVersion = null;
        if (!string.IsNullOrWhiteSpace(primaryArticle.CurrentPublishedVersionId))
        {
            versionsById.TryGetValue(primaryArticle.CurrentPublishedVersionId, out primaryVersion);
        }

        var related = new List<KnowledgeArticle>(capacity: 3);
        var seenArticleIds = new HashSet<string>(StringComparer.Ordinal) { primaryArticle.Id };

        foreach (var candidate in ordered.Skip(1).Where(candidate => seenArticleIds.Add(candidate.Article.Id)))
        {
            related.Add(candidate.Article);
            if (related.Count == 3)
            {
                break;
            }
        }

        if (related.Count < 3)
        {
            foreach (var sibling in publishedArticlesById.Values
                         .Where(a => !a.IsDeleted
                                     && string.Equals(a.Status, "PUBLISHED", StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(a.CategoryId, primaryArticle.CategoryId, StringComparison.Ordinal))
                         .OrderByDescending(a => a.CreatedAt)
                         .ThenBy(a => a.Title, StringComparer.Ordinal)
                         .Where(sibling => seenArticleIds.Add(sibling.Id)))
            {
                related.Add(sibling);
                if (related.Count == 3)
                {
                    break;
                }
            }
        }

        return new ContextualResolution(
            winner.Mapping,
            primaryArticle,
            primaryVersion,
            winner.Mapping.SectionAnchor,
            related);
    }

    private static string[] SplitSegments(string normalizedPath) =>
        normalizedPath == "/"
            ? []
            : normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsParameterSegment(string segment) =>
        segment.StartsWith(':')
        || (segment.StartsWith('{') && segment.EndsWith('}'));
}
