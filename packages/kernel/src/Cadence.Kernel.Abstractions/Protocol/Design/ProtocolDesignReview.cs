using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Kernel.Protocol.Design;

/// <summary>
/// A design-stage finding about a protocol design (K-PROT-3). It informs and never blocks: a version with findings can
/// still be submitted and approved. It becomes a ValidationFinding once the Quality engine's findings exist (#4812).
/// </summary>
/// <param name="ElementId">The element the finding is about.</param>
/// <param name="Code">A stable code, for example <see cref="ProtocolDesignReview.ItemWithoutPurpose"/>.</param>
/// <param name="Message">What is missing, in words.</param>
public sealed record DesignFinding(string ElementId, string Code, string Message);

/// <summary>
/// Checks that the scientific chain Objective → Endpoint → Estimand → required data → PlannedActivity → CollectionSpec
/// is explicit (K-PROT-3).
/// </summary>
public static class ProtocolDesignReview
{
    /// <summary>An objective no endpoint measures.</summary>
    public const string ObjectiveWithoutEndpoint = "K-PROT-3.objective-without-endpoint";

    /// <summary>A primary endpoint no estimand defines.</summary>
    public const string PrimaryEndpointWithoutEstimand = "K-PROT-3.primary-endpoint-without-estimand";

    /// <summary>A collection item with no path to an endpoint, estimand, safety rule or operational need.</summary>
    public const string ItemWithoutPurpose = "K-PROT-3.item-without-purpose";

    /// <summary>A collection specification no planned activity collects.</summary>
    public const string SpecNotScheduled = "K-PROT-3.collection-spec-not-scheduled";

    /// <summary>The design's findings, in element order.</summary>
    public static IReadOnlyList<DesignFinding> Findings(ProtocolDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        var findings = new List<DesignFinding>();
        var measured = design.Endpoints.Select(e => e.ObjectiveId).ToHashSet(StringComparer.Ordinal);
        var defined = design.Estimands.Select(e => e.EndpointId).ToHashSet(StringComparer.Ordinal);

        findings.AddRange(design.Objectives
            .Where(o => !measured.Contains(o.Id))
            .Select(o => new DesignFinding(o.Id, ObjectiveWithoutEndpoint, $"Objective {o.Id} has no endpoint.")));
        findings.AddRange(design.Endpoints
            .Where(e => e.Level == ObjectiveLevel.Primary && !defined.Contains(e.Id))
            .Select(e => new DesignFinding(e.Id, PrimaryEndpointWithoutEstimand, $"Primary endpoint {e.Id} has no estimand.")));

        foreach (var spec in design.CollectionSpecs)
        {
            var activities = design.PlannedActivities.Where(a => a.CollectionSpecIds.Contains(spec.Id, StringComparer.Ordinal)).ToList();
            if (activities.Count == 0)
            {
                findings.Add(new DesignFinding(spec.Id, SpecNotScheduled, $"Collection specification {spec.Id} is not collected by any planned activity."));
            }

            // An item inherits its specification's purpose, and the purpose of any activity that collects it.
            var inherited = HasPurpose(spec.Purpose) || activities.Exists(a => HasPurpose(a.Purpose));
            findings.AddRange(spec.Items
                .Where(item => !inherited && !HasPurpose(item.Purpose))
                .Select(item => new DesignFinding(
                    item.Id,
                    ItemWithoutPurpose,
                    $"Item {item.Id} has no path to an endpoint, estimand, safety rule or operational need.")));
        }

        return findings;
    }

    private static bool HasPurpose(DataPurpose? purpose) =>
        purpose is not null
        && (purpose.EndpointIds.Count > 0
            || purpose.EstimandIds.Count > 0
            || purpose.SafetyRuleIds.Count > 0
            || !string.IsNullOrWhiteSpace(purpose.OperationalNeed));
}
