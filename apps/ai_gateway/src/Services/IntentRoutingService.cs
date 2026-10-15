using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Contracts;

namespace Cadence.Apps.AiGateway.Services;

/// <summary>
/// Service that classifies natural language prompts into structured clinical intent actions.
/// </summary>
public static class IntentRoutingService
{
    private static readonly Regex LogAeRegex = new(
        @"(?:log|report|add|record)\s+(?:an?\s+)?(?:adverse\s+event|ae)\s+(?:for\s+)?(?:subject\s+|patient\s+|subj-?|sub-?)?([a-zA-Z0-9_-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProtocolDeviationRegex = new(
        @"(?:create|log|raise|report|add)\s+(?:a\s+)?(?:protocol\s+deviation|deviation|issue|ticket)(?:\s+for\s+site\s+([a-zA-Z0-9_-]+))?(?:\s+(?:for\s+|due\ to\s+|missing\s+)?(.+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SubjectEcrfRegex = new(
        @"(?:ecrf|subject|patient|form|data\s+entry)\s+(?:for\s+|review\s+)?(?:subject\s+|patient\s+|subj-?|sub-?)?([a-zA-Z0-9_-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Evaluates the natural language query and context against clinical intent patterns.
    /// </summary>
    /// <param name="query">User query prompt.</param>
    /// <param name="userRoles">List of roles assigned to the authenticated user.</param>
    /// <param name="context">Optional request context key-value pairs.</param>
    /// <returns>IntentRouteResponse containing matched recommendations allowed for the user roles.</returns>
    public static IntentRouteResponse ClassifyIntent(string query, IReadOnlyList<string> userRoles, IReadOnlyDictionary<string, string>? context = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new IntentRouteResponse(query ?? string.Empty, Array.Empty<IntentRecommendation>());
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var recommendations = new List<IntentRecommendation>();

        // 1. Scenario: Log Adverse Event for Subject
        var aeMatch = LogAeRegex.Match(normalizedQuery);
        if (aeMatch.Success)
        {
            var rawSubject = aeMatch.Groups[1].Value;
            var subjectId = NormalizeSubjectId(rawSubject);
            recommendations.Add(new IntentRecommendation(
                Id: $"ai-intent-log-ae-{subjectId.ToLowerInvariant()}",
                Name: $"Log Adverse Event for Subject {subjectId}",
                Description: $"Open eCRF data entry form to record a new adverse event for subject {subjectId}",
                Category: "[AI Copilot]",
                Icon: "🩺",
                TargetRoute: $"/ecrf?subjectId={Uri.EscapeDataString(subjectId)}&action=log_ae",
                Action: "log_ae",
                Confidence: 0.98,
                Roles: new[] { "site_investigator", "crc", "data_manager", "sponsor_admin", "cra", "monitor" }
            ));
        }

        // 2. Scenario: Create Protocol Deviation / Ticket
        if (normalizedQuery.Contains("protocol deviation") || normalizedQuery.Contains("create ticket") || normalizedQuery.Contains("log deviation") || normalizedQuery.Contains("missing lab cert"))
        {
            var devMatch = ProtocolDeviationRegex.Match(normalizedQuery);
            string siteId = devMatch.Success && !string.IsNullOrWhiteSpace(devMatch.Groups[1].Value) ? devMatch.Groups[1].Value : "102";
            if (context != null && context.TryGetValue("siteId", out var ctxSite) && !string.IsNullOrWhiteSpace(ctxSite))
            {
                siteId = ctxSite;
            }

            string reason = "missing_lab_cert";
            if (normalizedQuery.Contains("missing lab cert") || normalizedQuery.Contains("lab cert"))
            {
                reason = "missing_lab_cert";
            }
            else if (devMatch.Success && !string.IsNullOrWhiteSpace(devMatch.Groups[2].Value))
            {
                reason = devMatch.Groups[2].Value.Trim().Replace(" ", "_");
            }

            recommendations.Add(new IntentRecommendation(
                Id: $"ai-intent-ticket-deviation-{siteId.ToLowerInvariant()}",
                Name: $"Create Protocol Deviation for Site {siteId}",
                Description: $"Open clinical ticket creation pre-filled with protocol deviation details for site {siteId}",
                Category: "[AI Copilot]",
                Icon: "🎫",
                TargetRoute: $"/tickets?action=new&type=protocol_deviation&siteId={Uri.EscapeDataString(siteId)}&reason={Uri.EscapeDataString(reason)}",
                Action: "create_ticket",
                Confidence: 0.95,
                Roles: new[] { "cra", "monitor", "site_investigator", "crc", "sponsor_admin", "data_manager" }
            ));
        }

        // 3. Scenario: eCRF Direct Subject Access
        if (!aeMatch.Success && (normalizedQuery.Contains("subject") || normalizedQuery.Contains("ecrf")) && !normalizedQuery.Contains("protocol deviation"))
        {
            var subjMatch = SubjectEcrfRegex.Match(normalizedQuery);
            if (subjMatch.Success)
            {
                var rawSubject = subjMatch.Groups[1].Value;
                var subjectId = NormalizeSubjectId(rawSubject);
                recommendations.Add(new IntentRecommendation(
                    Id: $"ai-intent-ecrf-{subjectId.ToLowerInvariant()}",
                    Name: $"Open eCRF for Subject {subjectId}",
                    Description: $"Navigate directly to electronic case report form entry for subject {subjectId}",
                    Category: "[AI Copilot]",
                    Icon: "🩺",
                    TargetRoute: $"/ecrf?subjectId={Uri.EscapeDataString(subjectId)}",
                    Action: "open_ecrf",
                    Confidence: 0.92,
                    Roles: new[] { "site_investigator", "crc", "data_manager", "sponsor_admin", "cra", "monitor" }
                ));
            }
        }

        // 4. Scenario: eTMF Document Search / Regulatory Binder
        if (normalizedQuery.Contains("etmf") || normalizedQuery.Contains("document") || normalizedQuery.Contains("regulatory binder") || normalizedQuery.Contains("sop-"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "ai-intent-etmf-search",
                Name: "Open eTMF Document Repository",
                Description: "Search regulatory binder documents, inspect artifact zones, and view filings",
                Category: "[AI Copilot]",
                Icon: "📁",
                TargetRoute: "/etmf",
                Action: "open_etmf",
                Confidence: 0.88,
                Roles: new[] { "cra", "monitor", "auditor", "tmf_auditor", "sponsor_admin" }
            ));
        }

        // 5. Scenario: Lab Governance & Certificates
        if (normalizedQuery.Contains("lab") || normalizedQuery.Contains("certificate") || normalizedQuery.Contains("central lab"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "ai-intent-labs",
                Name: "Central Lab Governance & Certificates",
                Description: "Inspect central lab ranges, missing certificates, and lab alerts",
                Category: "[AI Copilot]",
                Icon: "🧪",
                TargetRoute: "/labs",
                Action: "open_labs",
                Confidence: 0.90,
                Roles: new[] { "site_investigator", "crc", "data_manager", "cra", "monitor", "sponsor_designer", "sponsor_admin", "super_admin" }
            ));
        }

        // 6. Scenario: CTMS Site Monitoring
        if (normalizedQuery.Contains("ctms") || normalizedQuery.Contains("monitoring") || normalizedQuery.Contains("site visit"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "ai-intent-ctms",
                Name: "CTMS Site Monitoring & Milestones",
                Description: "View trial milestones, site visit logs, and operational CRA metrics",
                Category: "[AI Copilot]",
                Icon: "📊",
                TargetRoute: "/ctms",
                Action: "open_ctms",
                Confidence: 0.88,
                Roles: new[] { "cra", "monitor", "sponsor_admin" }
            ));
        }

        // 7. Scenario: Audit & Cryptographic Ledger
        if (normalizedQuery.Contains("audit") || normalizedQuery.Contains("ledger") || normalizedQuery.Contains("gxp") || normalizedQuery.Contains("chain"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "ai-intent-audit",
                Name: "Cryptographic Ledger & Audit Trail",
                Description: "Inspect 21 CFR Part 11 audit trails and cryptographic block verification",
                Category: "[AI Copilot]",
                Icon: "🔒",
                TargetRoute: "/audit",
                Action: "open_audit",
                Confidence: 0.90,
                Roles: new[] { "auditor", "tmf_auditor", "sponsor_admin" }
            ));
        }

        // 8. Scenario: Knowledge & SOP Guidance
        if (normalizedQuery.Contains("sop") || normalizedQuery.Contains("guidance") || normalizedQuery.Contains("knowledge"))
        {
            recommendations.Add(new IntentRecommendation(
                Id: "ai-intent-knowledge",
                Name: "Knowledge & SOP Guidance Hub",
                Description: "Search operational guidance, clinical regulatory compliance, and study SOPs",
                Category: "[AI Copilot]",
                Icon: "📚",
                TargetRoute: "/knowledge",
                Action: "open_knowledge",
                Confidence: 0.85,
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
        if (string.IsNullOrWhiteSpace(raw)) return "101";
        if (raw.All(char.IsDigit) && !raw.StartsWith("SUBJ-", StringComparison.OrdinalIgnoreCase))
        {
            return $"SUBJ-{raw}";
        }
        return raw.ToUpperInvariant();
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
