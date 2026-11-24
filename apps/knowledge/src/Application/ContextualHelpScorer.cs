using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Apps.Knowledge.Domain;

namespace Cadence.Apps.Knowledge.Application;

/// <summary>
/// Deterministic Hierarchical Specificity Scoring engine for contextual SOP resolution (CONTEXT.md, PRD-KNB-002).
/// Ranks active <see cref="ContextualHelpMapping"/> records by:
/// 1. Route pattern specificity: exact route match &gt; parameterized segment match &gt; longest prefix wildcard &gt; global wildcard,
///    plus query specificity bonuses for Clinical Canvas <c>stage</c>, <c>section</c>, and entity zoom parameters.
/// 2. Persona specificity: exact normalized persona match &gt; universal wildcard (<c>null</c> or <c>*</c>).
/// 3. Explicit <see cref="ContextualHelpMapping.Priority"/> weight (descending).
/// 4. Exact query value matches and recency tie-breaking via <see cref="ContextualHelpMapping.CreatedAt"/> (descending).
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

    private static readonly Dictionary<string, int> ZoomQuerySpecificityBonuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["participant"] = 700,
        ["form"] = 750,
        ["observation"] = 800,
        ["proposal"] = 850,
    };

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

    /// <summary>
    /// Normalizes a UI route path by stripping hash fragments, trailing slashes, and non-routing query parameters
    /// while preserving canonical Clinical Canvas <c>stage</c> and <c>section</c> query parameters.
    /// </summary>
    public static string NormalizeRoute(string? route)
    {
        var (path, stage, section) = ParseCanonicalRoute(route);
        return FormatCanonicalRoute(path, stage, section);
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

        var (routePath, routeStage, routeSection) = ParseCanonicalRoute(route);
        string trimmedPattern = rawPattern.Trim();

        if (trimmedPattern is "*" or "/*")
        {
            return (1000, 0);
        }

        int hashIdx = trimmedPattern.IndexOf('#');
        if (hashIdx >= 0)
        {
            trimmedPattern = trimmedPattern[..hashIdx];
        }

        string patternPathPart = trimmedPattern;
        string? patternQueryPart = null;
        int queryIdx = trimmedPattern.IndexOf('?');
        if (queryIdx >= 0)
        {
            patternPathPart = trimmedPattern[..queryIdx];
            patternQueryPart = trimmedPattern[(queryIdx + 1)..];
        }

        var (queryBonus, exactQueryMatches, queryMatched) = EvaluatePatternQuery(
            route,
            routeStage,
            routeSection,
            patternQueryPart);
        if (!queryMatched)
        {
            return (-1, 0);
        }

        string pattern = patternPathPart.Trim();
        if (pattern is "*" or "/*")
        {
            return (1000 + queryBonus, exactQueryMatches);
        }

        if (!pattern.StartsWith('/'))
        {
            pattern = "/" + pattern;
        }

        string[] routeSegments = SplitSegments(routePath);

        // Prefix wildcard pattern: e.g. "/studies/*" or "/studies/:studyCode/*"
        if (pattern.EndsWith("/*", StringComparison.Ordinal))
        {
            string prefixPart = pattern[..^2];
            string normalizedPrefix = prefixPart.Length == 0 ? "/" : prefixPart.TrimEnd('/');
            if (normalizedPrefix.Length == 0)
            {
                return (1000 + queryBonus, exactQueryMatches);
            }

            string[] prefixSegments = SplitSegments(normalizedPrefix);
            if (routeSegments.Length < prefixSegments.Length)
            {
                return (-1, 0);
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
                        return (-1, 0);
                    }
                }
                else if (string.Equals(patSeg, routeSeg, StringComparison.OrdinalIgnoreCase))
                {
                    literalMatches++;
                }
                else
                {
                    return (-1, 0);
                }
            }

            // Longer prefixes and literal prefix segments outrank shorter prefixes; exact/parameterized full matches stay in 3000+/4000+.
            return (2000 + (prefixSegments.Length * 25) + (literalMatches * 10) + queryBonus, exactQueryMatches);
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
            return string.Equals(routePath, cleanPattern, StringComparison.OrdinalIgnoreCase)
                ? (4000 + (routeSegments.Length * 10) + queryBonus, exactQueryMatches)
                : (-1, 0);
        }

        if (patternSegments.Length != routeSegments.Length)
        {
            return (-1, 0);
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
                    return (-1, 0);
                }
            }
            else if (string.Equals(patSeg, routeSeg, StringComparison.OrdinalIgnoreCase))
            {
                literalSegmentCount++;
            }
            else
            {
                return (-1, 0);
            }
        }

        return (3000 + (literalSegmentCount * 15) + routeSegments.Length + queryBonus, exactQueryMatches);
    }

    private static (int Bonus, int ExactMatches, bool IsMatch) EvaluatePatternQuery(
        string rawRoute,
        string? routeStage,
        string? routeSection,
        string? patternQueryPart)
    {
        if (string.IsNullOrWhiteSpace(patternQueryPart))
        {
            return (0, 0, true);
        }

        Dictionary<string, string>? routeQuery = null;
        int bonus = 0;
        int exactMatches = 0;

        foreach (string pair in patternQueryPart.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eqIdx = pair.IndexOf('=');
            string rawKey = eqIdx >= 0 ? pair[..eqIdx] : pair;
            string rawVal = eqIdx >= 0 ? pair[(eqIdx + 1)..] : "*";

            string key = DecodeQueryPart(rawKey).Trim();
            string expectedVal = DecodeQueryPart(rawVal).Trim();
            if (key.Length == 0)
            {
                continue;
            }

            if (string.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
            {
                if (expectedVal.Length == 0)
                {
                    return (0, 0, false);
                }

                if (!IsWildcardOrParameterQueryValue(expectedVal))
                {
                    if (!string.Equals(routeStage, expectedVal, StringComparison.OrdinalIgnoreCase))
                    {
                        return (0, 0, false);
                    }

                    exactMatches++;
                }
                else if (string.IsNullOrWhiteSpace(routeStage))
                {
                    return (0, 0, false);
                }

                bonus += 200;
            }
            else if (string.Equals(key, "section", StringComparison.OrdinalIgnoreCase))
            {
                if (expectedVal.Length == 0)
                {
                    return (0, 0, false);
                }

                if (!IsWildcardOrParameterQueryValue(expectedVal))
                {
                    if (!string.Equals(routeSection, expectedVal, StringComparison.OrdinalIgnoreCase))
                    {
                        return (0, 0, false);
                    }

                    exactMatches++;
                }
                else if (string.IsNullOrWhiteSpace(routeSection))
                {
                    return (0, 0, false);
                }

                bonus += 400;
            }
            else if (ZoomQuerySpecificityBonuses.TryGetValue(key, out int zoomBonus))
            {
                routeQuery ??= ExtractQueryParameters(rawRoute);
                if (!routeQuery.TryGetValue(key, out string? actualVal) || string.IsNullOrWhiteSpace(actualVal))
                {
                    return (0, 0, false);
                }

                if (!IsWildcardOrParameterQueryValue(expectedVal))
                {
                    if (!string.Equals(actualVal, expectedVal, StringComparison.OrdinalIgnoreCase))
                    {
                        return (0, 0, false);
                    }

                    exactMatches++;
                }

                bonus += zoomBonus;
            }
            else
            {
                return (0, 0, false);
            }
        }

        return (bonus, exactMatches, true);
    }

    private static Dictionary<string, string> ExtractQueryParameters(string? rawRoute)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawRoute))
        {
            return result;
        }

        string trimmed = rawRoute.Trim();
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
            string rawValue = eqIdx >= 0 ? pair[(eqIdx + 1)..] : string.Empty;

            string key = DecodeQueryPart(rawKey).Trim();
            if (key.Length == 0)
            {
                continue;
            }

            string value = DecodeQueryPart(rawValue).Trim();
            result[key] = value;
        }

        return result;
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

    private static (string Path, string? Stage, string? Section) ParseCanonicalRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return ("/", null, null);
        }

        string clean = route.Trim();
        int hashIdx = clean.IndexOf('#');
        if (hashIdx >= 0)
        {
            clean = clean[..hashIdx];
        }

        string rawPath = clean;
        string? rawQuery = null;
        int queryIdx = clean.IndexOf('?');
        if (queryIdx >= 0)
        {
            rawPath = clean[..queryIdx];
            rawQuery = clean[(queryIdx + 1)..];
        }

        const char RouteDelimiter = '/';
        string normalizedPath = rawPath.Trim();
        if (!normalizedPath.StartsWith(RouteDelimiter))
        {
            normalizedPath = string.Concat(RouteDelimiter, normalizedPath);
        }

        normalizedPath = normalizedPath.TrimEnd(RouteDelimiter);
        if (normalizedPath.Length == 0)
        {
            normalizedPath = RouteDelimiter.ToString();
        }

        var (stage, section) = ParseQueryStageAndSection(rawQuery);
        return (normalizedPath, stage, section);
    }

    private static string FormatCanonicalRoute(string path, string? stage, string? section)
    {
        if (stage is not null && section is not null)
        {
            return $"{path}?stage={stage}&section={section}";
        }

        if (stage is not null)
        {
            return $"{path}?stage={stage}";
        }

        if (section is not null)
        {
            return $"{path}?section={section}";
        }

        return path;
    }

    private static (string? Stage, string? Section) ParseQueryStageAndSection(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return (null, null);
        }

        string? stage = null;
        string? section = null;

        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eqIdx = pair.IndexOf('=');
            string rawKey = eqIdx >= 0 ? pair[..eqIdx] : pair;
            string rawVal = eqIdx >= 0 ? pair[(eqIdx + 1)..] : string.Empty;

            string key = DecodeQueryPart(rawKey).Trim();
            string val = DecodeQueryPart(rawVal).Trim().ToLowerInvariant();
            if (key.Length == 0 || val.Length == 0)
            {
                continue;
            }

            if (string.Equals(key, "stage", StringComparison.OrdinalIgnoreCase))
            {
                stage = val;
            }
            else if (string.Equals(key, "section", StringComparison.OrdinalIgnoreCase))
            {
                section = val;
            }
        }

        return (stage, section);
    }

    private static string DecodeQueryPart(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static string[] SplitSegments(string normalizedPath) =>
        normalizedPath == "/"
            ? []
            : normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsParameterSegment(string segment) =>
        segment.StartsWith(':')
        || (segment.StartsWith('{') && segment.EndsWith('}'));
}
