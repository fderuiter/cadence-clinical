using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
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
    private static readonly IReadOnlyDictionary<string, NodeRef> EmptyElements =
        new ReadOnlyDictionary<string, NodeRef>(new Dictionary<string, NodeRef>(StringComparer.Ordinal));

    /// <summary>
    /// Resolves the governed <c>protocol_element</c> lineage nodes for <paramref name="versionId"/> (when approved) or,
    /// when <paramref name="includeApprovedAncestorWhenUnapproved"/> is true, its nearest approved ancestor along
    /// <see cref="ProtocolVersionSummary.ParentVersionId"/>. Returns empty when no approved governing version exists or
    /// any version in the approved chain does not hold a valid typed <see cref="ProtocolDesign"/>.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, NodeRef>> ResolveApprovedChainElementsAsync(
        IProtocolVersionReader reader,
        TenantId tenant,
        ProtocolVersionId versionId,
        bool includeApprovedAncestorWhenUnapproved,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (await reader.GetAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is not { } current)
        {
            return EmptyElements;
        }

        var allVersions = await reader.ListForStudyAsync(tenant, current.StudyId, cancellationToken).ConfigureAwait(false);
        var byId = allVersions.ToDictionary(v => v.Id);
        byId[current.Id] = current;

        var target = IsApprovedGoverning(current) ? current : null;
        if (target is null && includeApprovedAncestorWhenUnapproved)
        {
            var cursor = current.ParentVersionId;
            var visited = new HashSet<ProtocolVersionId> { current.Id };
            while (cursor is { } parentId && visited.Add(parentId))
            {
                var summary = byId.GetValueOrDefault(parentId)
                    ?? await reader.GetAsync(tenant, parentId, cancellationToken).ConfigureAwait(false);
                if (summary is null)
                {
                    break;
                }

                byId[summary.Id] = summary;
                if (IsApprovedGoverning(summary))
                {
                    target = summary;
                    break;
                }

                cursor = summary.ParentVersionId;
            }
        }

        if (target is null)
        {
            return EmptyElements;
        }

        var chainSummaries = new List<ProtocolVersionSummary> { target };
        var ancestorId = target.ParentVersionId;
        var chainVisited = new HashSet<ProtocolVersionId> { target.Id };
        while (ancestorId is { } nextId)
        {
            if (!chainVisited.Add(nextId))
            {
                return EmptyElements;
            }

            var ancestor = byId.GetValueOrDefault(nextId)
                ?? await reader.GetAsync(tenant, nextId, cancellationToken).ConfigureAwait(false);
            if (ancestor is null)
            {
                return EmptyElements;
            }

            chainSummaries.Add(ancestor);
            ancestorId = ancestor.ParentVersionId;
        }

        chainSummaries.Reverse();
        var chain = new List<(int Number, string Content)>(chainSummaries.Count);
        foreach (var summary in chainSummaries)
        {
            var content = await reader.GetContentAsync(tenant, summary.Id, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                if (await reader.GetDesignAsync(tenant, summary.Id, cancellationToken).ConfigureAwait(false) is not { } fallbackDesign)
                {
                    return EmptyElements;
                }

                content = ProtocolDesignDocument.Write(fallbackDesign);
            }
            else if (!ProtocolDesignDocument.Read(content).IsValid)
            {
                return EmptyElements;
            }

            chain.Add((summary.Number, content));
        }

        return Elements(target.StudyId, chain);
    }

    private static bool IsApprovedGoverning(ProtocolVersionSummary summary) =>
        summary.ApprovalAttestationId is not null
        || summary.Status is ProtocolVersionStatus.Approved or ProtocolVersionStatus.Superseded;
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
    /// Every element of the last version in <paramref name="chain"/> mapped to its pinned <c>protocol_element</c>
    /// lineage node (at the number of the protocol version that last added or changed it).
    /// </summary>
    public static IReadOnlyDictionary<string, NodeRef> Elements(StudyId study, IReadOnlyList<(int Number, string Content)> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
        {
            throw new ArgumentException("The chain holds at least the governing version.", nameof(chain));
        }

        var versions = new Dictionary<string, int>(StringComparer.Ordinal);
        string? before = null;
        foreach (var (number, content) in chain)
        {
            foreach (var change in Changes(before, content))
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

        return versions.ToDictionary(
            pair => pair.Key,
            pair => NodeRef.For(ProtocolElementId.For(study, pair.Key), pair.Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolves <paramref name="version"/> and its ancestors via <paramref name="work"/>, returning each design element
    /// of <paramref name="version"/> mapped to its pinned <c>protocol_element</c> lineage node.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, NodeRef>> ElementsAsync(
        IKernelUnitOfWork work, ProtocolVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(version);
        var chain = await ChainAsync(work, version, cancellationToken).ConfigureAwait(false);
        return Elements(version.StudyId, chain);
    }

    /// <summary>The version and its ancestors, first version first, as (number, content).</summary>
    public static async Task<IReadOnlyList<(int Number, string Content)>> ChainAsync(
        IKernelUnitOfWork work, ProtocolVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(version);
        var chain = new List<(int Number, string Content)> { (version.Number, version.Content) };
        var parentId = version.ParentVersionId;
        while (parentId is { } id)
        {
            var parent = await work.ReadAsync<ProtocolVersion>(id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Protocol version {id} names a parent the tenant does not have.");
            chain.Add((parent.Number, parent.Content));
            parentId = parent.ParentVersionId;
        }

        chain.Reverse();
        return chain;
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
