using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Contracts;

namespace Cadence.Apps.AiGateway.Services;

/// <summary>
/// Rule-based intent router that classifies natural language prompt queries into structured clinical intent actions.
/// @req:PRD-SYS-051
/// </summary>
public static class RuleBasedIntentRouter
{
    private static readonly Regex LogAeRegex = new(
        @"(?:log|report|add|record)\s+(?:an?\s+)?(?:adverse\s+event|ae)(?:\s+(?:for|on)\s+(?:subject\s+|patient\s+|subj-?|sub-?)?([a-zA-Z0-9_-]+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProtocolDeviationRegex = new(
        @"(?:create|log|raise|report|add)\s+(?:a\s+)?(?:protocol\s+deviation|deviation|issue|ticket)(?:\s+for\s+site\s+([a-zA-Z0-9_-]+))?(?:\s+(?:for\s+|due\ to\s+|missing\s+)?(.+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SubjectEcrfRegex = new(
        @"(?:ecrf|subject|patient|form|data\s+entry)(?:\s+(?:for\s+|review\s+)?(?:subject\s+|patient\s+|subj-?|sub-?)?([a-zA-Z0-9_-]+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Evaluates the natural language query and context against clinical intent patterns using deterministic rule matching.
    /// </summary>
    /// <param name="query">User query prompt.</param>
    /// <param name="userRoles">List of verified roles assigned to the authenticated user from ClaimsPrincipal.</param>
    /// <param name="context">Optional request context key-value pairs (e.g. siteId, studyId).</param>
    /// <returns>IntentRouteResponse containing matched recommendations allowed for the user roles.</returns>
    public static IntentRouteResponse ClassifyIntent(
        string query,
        IReadOnlyList<string> userRoles,
        IReadOnlyDictionary<string, string>? context = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new IntentRouteResponse(query ?? string.Empty, Array.Empty<IntentRecommendation>());
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var recommendations = new List<IntentRecommendation>();

        // 1. Log Adverse Event
        var aeMatch = LogAeRegex.Match(normalizedQuery);
        if (aeMatch.Success)
        {
            string rawSubject = aeMatch.Groups[1].Value;
            bool hasSubject = !string.IsNullOrWhiteSpace(rawSubject);
            string subjectId = hasSubject ? NormalizeSubjectId(rawSubject) : string.Empty;

            string targetRoute = hasSubject
                ? $"/ecrf?subjectId={Uri.EscapeDataString(subjectId)}&action=log_ae"
                : "/ecrf?action=log_ae";

            string name = hasSubject
                ? $"Log Adverse Event for Subject {subjectId}"
                : "Log Adverse Event";

            string description = hasSubject
                ? $"Open eCRF data entry form to record a new adverse event for subject {subjectId}"
                : "Open eCRF data entry form to record a new adverse event";

            recommendations.Add(new IntentRecommendation(
                Id: hasSubject ? $"intent-log-ae-{subjectId.ToLowerInvariant()}" : "intent-log-ae",
                Name: name,
                Description: description,
                Category: "[AI Copilot]",
                Icon: "🩺",
                TargetRoute: targetRoute,
                Action: "log_ae",
                Confidence: 1.0,
                Roles: new[] { "site_investigator", "crc", "data_manager", "sponsor_admin", "cra", "monitor" }
            ));
        }

        // 2. Create Protocol Deviation / Ticket
        if (normalizedQuery.Contains("protocol deviation") || normalizedQuery.Contains("create ticket") || normalizedQuery.Contains("log deviation") || normalizedQuery.Contains("missing lab cert"))
        {
            var devMatch = ProtocolDeviationRegex.Match(normalizedQuery);
            string? rawSite = devMatch.Success && !string.IsNullOrWhiteSpace(devMatch.Groups[1].Value)
                ? devMatch.Groups[1].Value
                : null;

            if (rawSite == null && context != null && context.TryGetValue("siteId", out var ctxSite) && !string.IsNullOrWhiteSpace(ctxSite))
            {
                rawSite = ctxSite;
            }

            bool hasSite = !string.IsNullOrWhiteSpace(rawSite);
            string siteId = hasSite ? rawSite! : string.Empty;

            string reason = string.Empty;
            if (normalizedQuery.Contains("missing lab cert") || normalizedQuery.Contains("lab cert"))
            {
                reason = "missing_lab_cert";
            }
            else if (devMatch.Success && !string.IsNullOrWhiteSpace(devMatch.Groups[2].Value))
            {
                reason = devMatch.Groups[2].Value.Trim().Replace(" ", "_");
            }

            var queryParams = new List<string> { "action=new", "type=protocol_deviation" };
            if (hasSite)
            {
                queryParams.Add($"siteId={Uri.EscapeDataString(siteId)}");
            }
            if (!string.IsNullOrEmpty(reason))
            {
                queryParams.Add($"reason={Uri.EscapeDataString(reason)}");
            }

            string name = hasSite
                ? $"Create Protocol Deviation for Site {siteId}"
                : "Create Protocol Deviation";

            string description = hasSite
                ? $"Open clinical ticket creation pre-filled with protocol deviation details for site {siteId}"
                : "Open clinical ticket creation workflow for protocol deviations";

            recommendations.Add(new IntentRecommendation(
                Id: hasSite ? $"intent-ticket-deviation-{siteId.ToLowerInvariant()}" : "intent-ticket-deviation",
                Name: name,
                Description: description,
                Category: "[AI Copilot]",
                Icon: "🎫",
                TargetRoute: $"/tickets?{string.Join("&", queryParams)}",
                Action: "create_ticket",
                Confidence: 1.0,
                Roles: new[] { "cra", "monitor", "site_investigator", "crc", "sponsor_admin", "data_manager" }
            ));
        }

        // 3. eCRF Direct Subject Access
        if (!aeMatch.Success && (normalizedQuery.Contains("subject") || normalizedQuery.Contains("ecrf")) && !normalizedQuery.Contains("protocol deviation"))
        {
            var subjMatch = SubjectEcrfRegex.Match(normalizedQuery);
            string? rawSubject = subjMatch.Success && !string.IsNullOrWhiteSpace(subjMatch.Groups[1].Value)
                ? subjMatch.Groups[1].Value
                : null;

            bool hasSubject = !string.IsNullOrWhiteSpace(rawSubject);
            string subjectId = hasSubject ? NormalizeSubjectId(rawSubject!) : string.Empty;

            string targetRoute = hasSubject
                ? $"/ecrf?subjectId={Uri.EscapeDataString(subjectId)}"
                : "/ecrf";

            string name = hasSubject
                ? $"Open eCRF for Subject {subjectId}"
                : "Open eCRF Form Engine";

            string description = hasSubject
                ? $"Navigate directly to electronic case report form entry for subject {subjectId}"
                : "Navigate directly to electronic case report form entry";

            recommendations.Add(new IntentRecommendation(
                Id: hasSubject ? $"intent-ecrf-{subjectId.ToLowerInvariant()}" : "intent-ecrf",
                Name: name,
                Description: description,
                Category: "[AI Copilot]",
                Icon: "🩺",
                TargetRoute: targetRoute,
                Action: "open_ecrf",
                Confidence: 1.0,
                Roles: new[] { "site_investigator", "crc", "data_manager", "sponsor_admin", "cra", "monitor" }
            ));
        }

        // 4. eTMF Document Search / Regulatory Binder
        if (normalizedQuery.Contains("etmf") || normalizedQuery.Contains("document") || normalizedQuery.Contains("regulatory binder") || normalizedQuery.Contains("sop-"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "intent-etmf-search",
                Name: "Open eTMF Document Repository",
                Description: "Search regulatory binder documents, inspect artifact zones, and view filings",
                Category: "[AI Copilot]",
                Icon: "📁",
                TargetRoute: "/etmf",
                Action: "open_etmf",
                Confidence: 1.0,
                Roles: new[] { "cra", "monitor", "auditor", "tmf_auditor", "sponsor_admin" }
            ));
        }

        // 5. Lab Governance & Certificates
        if (normalizedQuery.Contains("lab") || normalizedQuery.Contains("certificate") || normalizedQuery.Contains("central lab"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "intent-labs",
                Name: "Central Lab Governance & Certificates",
                Description: "Inspect central lab ranges, missing certificates, and lab alerts",
                Category: "[AI Copilot]",
                Icon: "🧪",
                TargetRoute: "/labs",
                Action: "open_labs",
                Confidence: 1.0,
                Roles: new[] { "site_investigator", "crc", "data_manager", "cra", "monitor", "sponsor_designer", "sponsor_admin", "super_admin" }
            ));
        }

        // 6. CTMS Site Monitoring
        if (normalizedQuery.Contains("ctms") || normalizedQuery.Contains("monitoring") || normalizedQuery.Contains("site visit"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "intent-ctms",
                Name: "CTMS Site Monitoring & Milestones",
                Description: "View trial milestones, site visit logs, and operational CRA metrics",
                Category: "[AI Copilot]",
                Icon: "📊",
                TargetRoute: "/ctms",
                Action: "open_ctms",
                Confidence: 1.0,
                Roles: new[] { "cra", "monitor", "sponsor_admin" }
            ));
        }

        // 7. Audit & Cryptographic Ledger
        if (normalizedQuery.Contains("audit") || normalizedQuery.Contains("ledger") || normalizedQuery.Contains("gxp") || normalizedQuery.Contains("chain"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "intent-audit",
                Name: "Cryptographic Ledger & Audit Trail",
                Description: "Inspect 21 CFR Part 11 audit trails and cryptographic block verification",
                Category: "[AI Copilot]",
                Icon: "🔒",
                TargetRoute: "/audit",
                Action: "open_audit",
                Confidence: 1.0,
                Roles: new[] { "auditor", "tmf_auditor", "sponsor_admin" }
            ));
        }

        // 8. Knowledge & SOP Guidance
        if (normalizedQuery.Contains("sop") || normalizedQuery.Contains("guidance") || normalizedQuery.Contains("knowledge"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "intent-knowledge",
                Name: "Knowledge & SOP Guidance Hub",
                Description: "Search operational guidance, clinical regulatory compliance, and study SOPs",
                Category: "[AI Copilot]",
                Icon: "📚",
                TargetRoute: "/knowledge",
                Action: "open_knowledge",
                Confidence: 1.0,
                Roles: Array.Empty<string>()
            ));
        }

        // Filter recommendations according to user roles
        var filtered = recommendations
            .Where(rec => IsRoleAllowed(userRoles, rec.Roles))
            .DistinctBy(rec => rec.Id)
            .ToList();

        return new IntentRouteResponse(query, filtered);
    }

    private static string NormalizeSubjectId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.All(char.IsDigit) && !trimmed.StartsWith("SUBJ-", StringComparison.OrdinalIgnoreCase))
        {
            return $"SUBJ-{trimmed}";
        }
        return trimmed.ToUpperInvariant();
    }

    public static bool IsRoleAllowed(IReadOnlyList<string> userRoles, IReadOnlyList<string> requiredRoles)
    {
        if (requiredRoles == null || requiredRoles.Count == 0)
        {
            return true;
        }

        if (userRoles == null || userRoles.Count == 0)
        {
            return false;
        }

        var normalizedUserRoles = userRoles
            .Select(r => r.Trim().ToLowerInvariant().Replace(" ", "_"))
            .ToHashSet();

        return requiredRoles.Any(req =>
        {
            var normalizedReq = req.Trim().ToLowerInvariant().Replace(" ", "_");
            return normalizedUserRoles.Contains(normalizedReq);
        });
    }
}

/// <summary>
/// Service facade providing intent routing classification.
/// @req:PRD-SYS-051
/// </summary>
public static class IntentRoutingService
{
    /// <summary>
    /// Classifies query using the rule-based intent router.
    /// </summary>
    public static IntentRouteResponse ClassifyIntent(
        string query,
        IReadOnlyList<string> userRoles,
        IReadOnlyDictionary<string, string>? context = null)
    {
        return RuleBasedIntentRouter.ClassifyIntent(query, userRoles, context);
    }
}
