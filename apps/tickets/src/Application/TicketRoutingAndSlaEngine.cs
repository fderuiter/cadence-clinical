using System;
using System.Collections.Generic;

using Cadence.Kernel.Time;

namespace Cadence.Apps.Tickets.Application;

/// <summary>
/// Deterministic Module-Based Ticket Routing and Dual-Target SLA engine (CONTEXT.md, PRD-TCK-001, PRD-TCK-002).
/// @req:PRD-TCK-001
/// @req:PRD-TCK-002
/// </summary>
public static class TicketRoutingAndSlaEngine
{
    public const string QueueProtocolDesigner = "PROTOCOL_DESIGNER";
    public const string QueueDataCaptureEcrf = "DATA_CAPTURE_ECRF";
    public const string QueueSubjectManagementRtsm = "SUBJECT_MANAGEMENT_RTSM";
    public const string QueueReportingAnalytics = "REPORTING_ANALYTICS";
    public const string QueuePlatformAdminAccess = "PLATFORM_ADMIN_ACCESS";
    public const string QueueRegulatoryCompliance = "REGULATORY_COMPLIANCE";

    public static readonly IReadOnlySet<string> ValidCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PROTOCOL_DEVIATION",
        "DATA_QUERY",
        "SAFETY_ADVERSE_EVENT",
        "SUPPLY_EXCURSION",
        "SITE_OPERATIONS",
        "MONITORING_FINDING",
        "TECHNICAL_SYSTEM",
        "ACCESS_CONTROL",
        "REGULATORY_QUERY",
        "SYSTEM_SUPPORT",
    };

    public static readonly IReadOnlySet<string> ValidSeverities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "MINOR",
        "MAJOR",
        "CRITICAL",
    };

    public static readonly IReadOnlySet<string> ValidPriorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "LOW",
        "MEDIUM",
        "HIGH",
        "CRITICAL",
    };

    public sealed record SlaTargets(
        DateTimeOffset AcknowledgeTargetAt,
        DateTimeOffset ResolutionTargetAt);

    /// <summary>
    /// Deterministically assigns a ticket to one of the six functional support queues defined in <c>CONTEXT.md</c>
    /// based on category, originating route context, and linked entity type.
    /// </summary>
    public static string ResolveQueue(string? category, string? sourceRoute, string? entityType)
    {
        string normCategory = (category ?? "SYSTEM_SUPPORT").Trim().ToUpperInvariant();
        string normRoute = NormalizeRoute(sourceRoute);
        string normEntity = (entityType ?? string.Empty).Trim().ToUpperInvariant();

        // 1. High-specificity category assignments that always route to dedicated specialist teams
        switch (normCategory)
        {
            case "ACCESS_CONTROL":
            case "TECHNICAL_SYSTEM":
                return QueuePlatformAdminAccess;
            case "REGULATORY_QUERY":
                return QueueRegulatoryCompliance;
            case "SUPPLY_EXCURSION":
                return QueueSubjectManagementRtsm;
        }

        // 2. Originating module / route context routing
        if (StartsWithSegment(normRoute, "/ecrf")
            || StartsWithSegment(normRoute, "/rules")
            || StartsWithSegment(normRoute, "/coding"))
        {
            return QueueDataCaptureEcrf;
        }

        if (StartsWithSegment(normRoute, "/mdr")
            || StartsWithSegment(normRoute, "/designer")
            || StartsWithSegment(normRoute, "/protocol"))
        {
            return QueueProtocolDesigner;
        }

        if (StartsWithSegment(normRoute, "/studies"))
        {
            return normCategory switch
            {
                "DATA_QUERY" => QueueDataCaptureEcrf,
                "SITE_OPERATIONS" or "MONITORING_FINDING" or "SAFETY_ADVERSE_EVENT" => QueueSubjectManagementRtsm,
                _ => QueueProtocolDesigner,
            };
        }

        if (StartsWithSegment(normRoute, "/rtsm")
            || StartsWithSegment(normRoute, "/supply")
            || StartsWithSegment(normRoute, "/ssu")
            || StartsWithSegment(normRoute, "/ctms")
            || StartsWithSegment(normRoute, "/safety"))
        {
            return QueueSubjectManagementRtsm;
        }

        if (StartsWithSegment(normRoute, "/exports")
            || StartsWithSegment(normRoute, "/analytics")
            || StartsWithSegment(normRoute, "/reports")
            || StartsWithSegment(normRoute, "/payments")
            || StartsWithSegment(normRoute, "/sce"))
        {
            return QueueReportingAnalytics;
        }

        if (StartsWithSegment(normRoute, "/audit")
            || StartsWithSegment(normRoute, "/quality")
            || StartsWithSegment(normRoute, "/etmf")
            || StartsWithSegment(normRoute, "/eisf")
            || StartsWithSegment(normRoute, "/econsent"))
        {
            return QueueRegulatoryCompliance;
        }

        if (StartsWithSegment(normRoute, "/admin")
            || StartsWithSegment(normRoute, "/users")
            || StartsWithSegment(normRoute, "/org")
            || StartsWithSegment(normRoute, "/settings"))
        {
            return QueuePlatformAdminAccess;
        }

        // 3. Linked entity type fallback
        switch (normEntity)
        {
            case "CRF_FORM":
                return QueueDataCaptureEcrf;
            case "SUBJECT":
            case "SUPPLY_KIT":
            case "SAE_CASE":
            case "MVR_VISIT":
                return QueueSubjectManagementRtsm;
            case "CAPA_ACTION":
            case "TMF_DOCUMENT":
                return QueueRegulatoryCompliance;
        }

        // 4. Category fallback when route and entity type are generic
        return normCategory switch
        {
            "DATA_QUERY" => QueueDataCaptureEcrf,
            "SITE_OPERATIONS" or "MONITORING_FINDING" or "SAFETY_ADVERSE_EVENT" => QueueSubjectManagementRtsm,
            "PROTOCOL_DEVIATION" => QueueRegulatoryCompliance,
            _ => QueuePlatformAdminAccess,
        };
    }

    /// <summary>
    /// Computes Dual-Target SLA deadlines (Acknowledge + Resolution) from <paramref name="clock"/> per CONTEXT.md:
    /// Critical: 15m acknowledge / 4h resolution; Major: 1h acknowledge / 8h resolution; Minor: 4h acknowledge / 24h-72h resolution.
    /// </summary>
    public static SlaTargets ComputeSlaTargets(string? gxpSeverity, string? priority, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        DateTimeOffset now = clock.UtcNow;
        string sev = (gxpSeverity ?? "MINOR").Trim().ToUpperInvariant();
        string prio = (priority ?? "MEDIUM").Trim().ToUpperInvariant();

        if (sev == "CRITICAL" || prio == "CRITICAL")
        {
            return new SlaTargets(
                AcknowledgeTargetAt: now.AddMinutes(15),
                ResolutionTargetAt: now.AddHours(4));
        }

        if (sev == "MAJOR" || prio == "HIGH")
        {
            return new SlaTargets(
                AcknowledgeTargetAt: now.AddHours(1),
                ResolutionTargetAt: now.AddHours(8));
        }

        if (prio == "LOW")
        {
            return new SlaTargets(
                AcknowledgeTargetAt: now.AddHours(4),
                ResolutionTargetAt: now.AddHours(72));
        }

        return new SlaTargets(
            AcknowledgeTargetAt: now.AddHours(4),
            ResolutionTargetAt: now.AddHours(24));
    }

    private static string NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return string.Empty;
        }

        string trimmed = route.Trim();
        int queryIdx = trimmed.IndexOfAny(['?', '#']);
        if (queryIdx >= 0)
        {
            trimmed = trimmed[..queryIdx];
        }

        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed.TrimEnd('/').ToLowerInvariant();
    }

    private static bool StartsWithSegment(string normalizedRoute, string segmentPrefix) =>
        string.Equals(normalizedRoute, segmentPrefix, StringComparison.Ordinal)
        || normalizedRoute.StartsWith(segmentPrefix + "/", StringComparison.Ordinal);
}
