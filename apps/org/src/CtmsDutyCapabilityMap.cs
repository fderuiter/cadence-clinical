using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;

namespace Cadence.Modules.Org;

/// <summary>
/// Maps clinical site duties and kernel role codes to bounded kernel capability tokens and Keycloak realm roles,
/// enforcing <c>K-AUTH-4</c> so unblinding capabilities are never bundled into standard invitations or delegations.
/// </summary>
public static class CtmsDutyCapabilityMap
{
    /// <summary>Named clinical duties available for bounded site delegation.</summary>
    public static IReadOnlyList<(string Duty, string Label, IReadOnlyList<string> Capabilities)> Catalog { get; } =
    [
        ("Informed Consent", "Informed Consent & Subject Registration", ["participant.register", "participant.screen", "collection.open"]),
        ("CRF Data Entry", "eCRF Data Entry & Corrections", ["observation.record", "observation.correct", "collection.open", "collection.record"]),
        ("Subject Screening", "Subject Screening & Enrollment Status", ["participant.register", "participant.screen", "enrollment.change_status"]),
        ("Query Resolution", "Clinical Query Resolution", ["execution.query.answer", "observation.correct"]),
        ("PI Sign-Off", "Investigator Form Sign-Off", ["collection.sign", "execution.query.answer"]),
        ("Source Data Verification", "Source Data Verification (SDV)", ["observation.verify", "execution.query.raise"]),
    ];

    private static readonly FrozenDictionary<string, string[]> ByDuty = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Informed Consent"] = ["participant.register", "participant.screen", "collection.open"],
        ["CRF Data Entry"] = ["observation.record", "observation.correct", "collection.open", "collection.record"],
        ["Data Entry"] = ["observation.record", "observation.correct", "collection.open", "collection.record"],
        ["Subject Screening"] = ["participant.register", "participant.screen", "enrollment.change_status"],
        ["Participant Screening"] = ["participant.register", "participant.screen", "enrollment.change_status"],
        ["Query Resolution"] = ["execution.query.answer", "observation.correct"],
        ["PI Sign-Off"] = ["collection.sign", "execution.query.answer"],
        ["Form Sign-Off"] = ["collection.sign", "execution.query.answer"],
        ["Principal Investigator Oversight"] = ["collection.sign", "execution.query.answer"],
        ["Source Data Verification"] = ["observation.verify", "execution.query.raise"],
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string[]> RealmRolesByKernelRole = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["DEMO_SPONSOR_ADMIN"] = ["Sponsor Admin", "Sponsor Designer", "Data Manager"],
        ["SPONSOR_ADMIN"] = ["Sponsor Admin"],
        ["DEMO_INVESTIGATOR"] = ["Site Investigator"],
        ["SITE_INVESTIGATOR"] = ["Site Investigator"],
        ["DEMO_CRC"] = ["Site Investigator"],
        ["CRC"] = ["Site Investigator"],
        ["DEMO_MONITOR"] = ["CRA", "Monitor"],
        ["CRA"] = ["CRA", "Monitor"],
        ["DEMO_DATA_MANAGER"] = ["Data Manager"],
        ["DATA_MANAGER"] = ["Data Manager"],
        ["DEMO_DESIGNER"] = ["Sponsor Designer"],
        ["SPONSOR_DESIGNER"] = ["Sponsor Designer"],
        ["DEMO_SAFETY"] = ["Sponsor Admin"],
        ["DEMO_AUDITOR"] = ["Auditor"],
        ["AUDITOR"] = ["Auditor"],
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a list of duty names or explicit capability tokens into distinct well-formed kernel capability tokens.
    /// Returns <c>hasUnblinding = true</c> when any token is an unblinding capability (<c>K-AUTH-4</c>).
    /// </summary>
    public static (IReadOnlyList<string> Capabilities, bool HasUnblinding, string? InvalidToken) ResolveDutiesAndCapabilities(
        IEnumerable<string>? duties,
        IEnumerable<string>? capabilities)
    {
        var resolved = new SortedSet<string>(StringComparer.Ordinal);
        bool hasUnblinding = false;

        if (duties is not null)
        {
            foreach (var rawDuty in duties.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                var trimmed = rawDuty.Trim();
                if (ByDuty.TryGetValue(trimmed, out var mapped))
                {
                    foreach (var cap in mapped)
                    {
                        resolved.Add(cap);
                    }
                }
                else if (Capability.IsWellFormed(trimmed))
                {
                    if (Capability.IsUnblinding(trimmed))
                    {
                        hasUnblinding = true;
                    }

                    resolved.Add(trimmed);
                }
                else
                {
                    return ([], false, trimmed);
                }
            }
        }

        if (capabilities is not null)
        {
            foreach (var rawCap in capabilities.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var trimmed = rawCap.Trim();
                if (!Capability.IsWellFormed(trimmed))
                {
                    return ([], false, trimmed);
                }

                if (Capability.IsUnblinding(trimmed))
                {
                    hasUnblinding = true;
                }

                resolved.Add(trimmed);
            }
        }

        return (resolved.ToList(), hasUnblinding, null);
    }

    /// <summary>
    /// Maps a kernel role code or delegated capability set to Keycloak realm roles used by legacy downstream services.
    /// </summary>
    public static IReadOnlyList<string> RealmRolesFor(string? roleCode, IReadOnlyCollection<string> capabilities, ScopeLevel scopeLevel)
    {
        if (!string.IsNullOrWhiteSpace(roleCode) && RealmRolesByKernelRole.TryGetValue(roleCode.Trim(), out var byRole))
        {
            return byRole;
        }

        var roles = new SortedSet<string>(StringComparer.Ordinal);
        if (capabilities.Contains("grant.issue", StringComparer.Ordinal))
        {
            roles.Add("Sponsor Admin");
        }

        if (capabilities.Any(c => c.StartsWith("protocol.", StringComparison.Ordinal)))
        {
            roles.Add("Sponsor Designer");
        }

        if (capabilities.Contains("observation.lock", StringComparer.Ordinal) || capabilities.Contains("validation_run.record", StringComparer.Ordinal))
        {
            roles.Add("Data Manager");
        }

        if (capabilities.Contains("observation.verify", StringComparer.Ordinal))
        {
            roles.Add("CRA");
            roles.Add("Monitor");
        }

        if (scopeLevel == ScopeLevel.StudySite
            || capabilities.Any(c => c is "participant.register" or "participant.screen" or "observation.record" or "collection.open" or "collection.record" or "collection.sign" or "execution.query.answer"))
        {
            roles.Add("Site Investigator");
        }

        if (roles.Count == 0)
        {
            roles.Add("Auditor");
        }

        return roles.ToList();
    }
}
