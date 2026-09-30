using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;

namespace Cadence.Kernel.Application.Protocol;

/// <summary>
/// A protocol version's design elements as lineage nodes (ADR-2474, spec §9.6). An element is the object
/// <c>protocol_element:&lt;ProtocolElementId.For(study, element id)&gt;</c>; its version is the number of the protocol
/// version that last added or changed it, by the kernel's structural diff (ADR-2480 §4.6). So an element that an
/// amendment does not touch keeps its node, and the lineage that hangs off it.
/// </summary>
internal static class ProtocolElementLineage
{
    /// <summary>
    /// What approving the last version of <paramref name="chain"/> writes: every element of its design as a part, new
    /// when this version added or changed it; <see cref="LineageEdgeKind.Supersedes"/> from a changed element's previous
    /// version; the references between elements where either side is new; and the concepts and code-system versions a
    /// new element is defined by.
    /// </summary>
    /// <param name="study">The study whose protocol this is.</param>
    /// <param name="chain">The version and its ancestors, first version first, as (number, content).</param>
    public static ElementLineagePlan Plan(StudyId study, IReadOnlyList<(int Number, string Content)> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
        {
            throw new ArgumentException("The chain holds at least the version being approved.", nameof(chain));
        }

        var versions = new Dictionary<string, int>(StringComparer.Ordinal);
        var previous = versions;
        IReadOnlyList<ElementChange> last = [];
        string? before = null;
        foreach (var (number, content) in chain)
        {
            previous = new Dictionary<string, int>(versions, StringComparer.Ordinal);
            last = Changes(before, content);
            foreach (var change in last)
            {
                if (change.Change == ElementChangeKind.Removed)
                {
                    versions.Remove(change.ElementId);
                }
                else
                {
                    versions[change.ElementId] = number;
                }
            }

            before = content;
        }

        var added = last.Where(c => c.Change != ElementChangeKind.Removed).Select(c => c.ElementId).ToHashSet(StringComparer.Ordinal);
        NodeRef Node(string id) => NodeRef.For(ProtocolElementId.For(study, id), versions[id]);

        var parts = versions.Keys.Order(StringComparer.Ordinal).Select(id => new ElementPart(Node(id), added.Contains(id))).ToList();
        var edges = new List<ElementEdge>();
        foreach (var id in last.Where(c => c.Change == ElementChangeKind.Changed).Select(c => c.ElementId))
        {
            edges.Add(new ElementEdge(NodeRef.For(ProtocolElementId.For(study, id), previous[id]), Node(id), LineageEdgeKind.Supersedes));
        }

        var design = ProtocolDesignDocument.Read(before!).Design
            ?? throw new InvalidOperationException("An approved protocol version holds a protocol design (ADR-2472).");
        foreach (var (from, to, kind) in References(design).Where(r => added.Contains(r.From) || added.Contains(r.To)))
        {
            edges.Add(new ElementEdge(Node(from), Node(to), kind));
        }

        foreach (var (element, input) in Definitions(design).Where(d => added.Contains(d.Element)))
        {
            edges.Add(new ElementEdge(input, Node(element), LineageEdgeKind.DefinedBy));
        }

        return new ElementLineagePlan(parts, [.. edges.Distinct()]);
    }

    /// <summary>
    /// The references between a design's elements, upstream first: <c>Objective → Endpoint → Estimand</c>,
    /// <c>PlannedActivity → CollectionSpec → item</c> and the purposes that require data (<see cref="LineageEdgeKind.RequiredBy"/>);
    /// populations, arms, interventions, criteria and epochs that define what refers to them (<see cref="LineageEdgeKind.DefinedBy"/>).
    /// </summary>
    public static IEnumerable<(string From, string To, LineageEdgeKind Kind)> References(ProtocolDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return ReferencesOf(design);
    }

    private static IEnumerable<(string From, string To, LineageEdgeKind Kind)> ReferencesOf(ProtocolDesign design)
    {
        foreach (var endpoint in design.Endpoints)
        {
            yield return (endpoint.ObjectiveId, endpoint.Id, LineageEdgeKind.RequiredBy);
        }

        foreach (var estimand in design.Estimands)
        {
            yield return (estimand.EndpointId, estimand.Id, LineageEdgeKind.RequiredBy);
            yield return (estimand.PopulationId, estimand.Id, LineageEdgeKind.DefinedBy);
            foreach (var id in estimand.ArmIds.Concat(estimand.InterventionIds ?? []))
            {
                yield return (id, estimand.Id, LineageEdgeKind.DefinedBy);
            }
        }

        foreach (var population in design.Populations)
        {
            foreach (var criterion in population.CriterionIds)
            {
                yield return (criterion, population.Id, LineageEdgeKind.DefinedBy);
            }
        }

        foreach (var arm in design.Arms)
        {
            foreach (var intervention in arm.InterventionIds)
            {
                yield return (intervention, arm.Id, LineageEdgeKind.DefinedBy);
            }
        }

        foreach (var cohort in design.Cohorts)
        {
            foreach (var arm in cohort.ArmIds)
            {
                yield return (arm, cohort.Id, LineageEdgeKind.DefinedBy);
            }
        }

        foreach (var activity in design.PlannedActivities)
        {
            yield return (activity.EpochId, activity.Id, LineageEdgeKind.DefinedBy);
            foreach (var spec in activity.CollectionSpecIds)
            {
                yield return (activity.Id, spec, LineageEdgeKind.RequiredBy);
            }

            foreach (var purpose in Purposes(activity.Id, activity.Purpose))
            {
                yield return purpose;
            }
        }

        foreach (var spec in design.CollectionSpecs)
        {
            foreach (var purpose in Purposes(spec.Id, spec.Purpose))
            {
                yield return purpose;
            }

            foreach (var item in spec.Items)
            {
                yield return (spec.Id, item.Id, LineageEdgeKind.RequiredBy);
                foreach (var purpose in Purposes(item.Id, item.Purpose))
                {
                    yield return purpose;
                }
            }
        }
    }

    private static IEnumerable<(string From, string To, LineageEdgeKind Kind)> Purposes(string element, DataPurpose? purpose) =>
        purpose is null
            ? []
            : purpose.EndpointIds.Concat(purpose.EstimandIds).Concat(purpose.SafetyRuleIds).Select(id => (id, element, LineageEdgeKind.RequiredBy));

    /// <summary>The concept versions and code-system versions outside the design that define its elements.</summary>
    private static IEnumerable<(string Element, NodeRef Input)> Definitions(ProtocolDesign design)
    {
        static NodeRef Concept(ConceptRef concept) => NodeRef.For(concept.ConceptId, concept.Version);

        foreach (var endpoint in design.Endpoints.Where(e => e.Concept is not null))
        {
            yield return (endpoint.Id, Concept(endpoint.Concept!.Value));
        }

        foreach (var criterion in design.EligibilityCriteria)
        {
            foreach (var concept in criterion.Concepts)
            {
                yield return (criterion.Id, Concept(concept));
            }
        }

        foreach (var intervention in design.Interventions.Where(i => i.Product is not null))
        {
            yield return (intervention.Id, Concept(intervention.Product!.Value));
        }

        foreach (var item in design.CollectionSpecs.SelectMany(s => s.Items))
        {
            yield return (item.Id, Concept(item.Concept));
            if (item.Terminology is { } binding)
            {
                // A code-system version is immutable: its one lineage version is 1, as the Concept Registry links it.
                yield return (item.Id, NodeRef.For(binding.CodeSystemVersionId, 1));
            }
        }

        foreach (var rule in design.SafetyRules.Concat(design.DiscontinuationRules))
        {
            foreach (var concept in rule.Concepts)
            {
                yield return (rule.Id, Concept(concept));
            }
        }
    }

    /// <summary>The elements <paramref name="after"/> adds, changes or removes relative to <paramref name="before"/>.</summary>
    public static IReadOnlyList<ElementChange> Changes(string? before, string after)
    {
        var diff = ProtocolStructuralDiff.Compare(before, after);
        if (!diff.IsSuccess)
        {
            throw new InvalidOperationException($"Protocol content cannot be compared: {diff.Error!.Message}");
        }

        return [.. diff.Value!.Where(c => c.ElementId != ProtocolStructuralDiff.RootId)];
    }
}

/// <summary>An element of the approved design as a lineage part: its pinned node, and whether this approval writes it.</summary>
internal sealed record ElementPart(NodeRef Node, bool NewVersion);

/// <summary>An edge the approval writes between element versions, or into a new element version.</summary>
internal sealed record ElementEdge(NodeRef From, NodeRef To, LineageEdgeKind Kind);

/// <summary>Everything approving a protocol version writes for its design elements (ADR-2474).</summary>
internal sealed record ElementLineagePlan(IReadOnlyList<ElementPart> Parts, IReadOnlyList<ElementEdge> Edges);
