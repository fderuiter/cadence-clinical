using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Standards.Mapping;

/// <summary>A mapping specification library item version a study pinned, with its content read.</summary>
/// <param name="Item">The library item version.</param>
/// <param name="Key">The item's key.</param>
/// <param name="Version">The item's version number.</param>
/// <param name="VersionIndex">The item's version index, which pins its lineage node.</param>
/// <param name="StandardVersion">The standard version the specification maps into.</param>
/// <param name="Specification">The content.</param>
public sealed record PinnedMappingSpecification(
    LibraryItemId Item,
    string Key,
    int Version,
    long VersionIndex,
    StandardVersionId StandardVersion,
    MappingSpecification Specification)
{
    /// <summary>The pinned lineage node of this version.</summary>
    public NodeRef Node => NodeRef.For(Item, VersionIndex);
}

/// <summary>The mapping a study uses for a Concept in one standard version, and the specification it comes from.</summary>
public sealed record CoveringMapping(PinnedMappingSpecification Specification, VariableMapping Mapping);

/// <summary>
/// The mappings a study uses (ADR-2463): its pinned mapping specifications with overrides applied. Mapping is authored
/// against Concepts, so one mapping serves every CollectionSpec item that records the same Concept. A study-level
/// override is itself a versioned mapping specification that names the base it overrides; its mappings replace that
/// base's mappings for the same Concepts, and add to them otherwise. An override never replaces another specification's
/// mapping.
/// </summary>
public sealed class StudyMappings
{
    private readonly Dictionary<(StandardVersionId Standard, ConceptId Concept), CoveringMapping> _mappings;

    private StudyMappings(Dictionary<(StandardVersionId, ConceptId), CoveringMapping> mappings, IReadOnlyList<PinnedMappingSpecification> pins)
    {
        _mappings = mappings;
        Specifications = pins;
    }

    /// <summary>The pinned specifications, ordered by key.</summary>
    public IReadOnlyList<PinnedMappingSpecification> Specifications { get; }

    /// <summary>
    /// Resolves a study's pinned specifications, or returns why they cannot be used together: a study pins one version of
    /// each specification; a specification must map
    /// into a standard version the study declared; an override's base must be pinned, must map into the same standard
    /// version and must not itself be an override; and within one standard version a Concept has one mapping after
    /// overrides are applied.
    /// </summary>
    public static (StudyMappings? Mappings, IReadOnlyList<string> Problems) Resolve(
        IReadOnlyList<PinnedMappingSpecification> pins, IReadOnlyCollection<StandardVersionId> declaredStandards)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(declaredStandards);

        var problems = new List<string>();
        var ordered = pins.OrderBy(p => p.Key, StringComparer.Ordinal).ThenBy(p => p.Version).ToList();
        foreach (var duplicate in ordered.GroupBy(p => p.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add($"Mapping specification {duplicate.Key} is pinned at {duplicate.Count()} versions; a study pins one version of each.");
        }

        if (problems.Count > 0)
        {
            return (null, problems);
        }

        var byKey = ordered.ToDictionary(p => p.Key, StringComparer.Ordinal);
        foreach (var pin in ordered)
        {
            if (!declaredStandards.Contains(pin.StandardVersion))
            {
                problems.Add($"Mapping specification {pin.Key} v{pin.Version} maps into standard version {pin.StandardVersion}, which the study does not declare.");
            }

            if (pin.Specification.Overrides is not { } baseKey)
            {
                continue;
            }

            if (!byKey.TryGetValue(baseKey, out var basePin))
            {
                problems.Add($"Mapping specification {pin.Key} v{pin.Version} overrides {baseKey}, which the study does not pin.");
            }
            else if (basePin.Specification.Overrides is not null)
            {
                problems.Add($"Mapping specification {pin.Key} v{pin.Version} overrides {baseKey}, which is itself an override; override the base specification.");
            }
            else if (basePin.StandardVersion != pin.StandardVersion)
            {
                problems.Add($"Mapping specification {pin.Key} v{pin.Version} maps into a different standard version from {baseKey}, which it overrides.");
            }
        }

        if (problems.Count > 0)
        {
            return (null, problems);
        }

        var mappings = new Dictionary<(StandardVersionId, ConceptId), CoveringMapping>();
        foreach (var group in ordered.GroupBy(p => p.StandardVersion))
        {
            // Base specifications first, then overrides replace the base mappings they cover.
            var bases = group.Where(p => p.Specification.Overrides is null).ToList();
            var overrides = group.Where(p => p.Specification.Overrides is not null).ToList();
            AddAll(group.Key, bases, mappings, problems);
            AddAll(group.Key, overrides, mappings, problems);
        }

        return problems.Count > 0 ? (null, problems) : (new StudyMappings(mappings, ordered), []);
    }

    /// <summary>The mapping for <paramref name="concept"/> in each standard version that maps it, ordered by specification key.</summary>
    public IReadOnlyList<CoveringMapping> For(ConceptId concept) =>
        [.. _mappings.Where(m => m.Key.Concept == concept).Select(m => m.Value).OrderBy(m => m.Specification.Key, StringComparer.Ordinal)];

    private static void AddAll(
        StandardVersionId standard,
        List<PinnedMappingSpecification> pins,
        Dictionary<(StandardVersionId, ConceptId), CoveringMapping> mappings,
        List<string> problems)
    {
        foreach (var pin in pins)
        {
            foreach (var mapping in pin.Specification.Mappings)
            {
                var key = (standard, mapping.Concept.ConceptId);

                // An override replaces only the mapping of the base it names; any other existing mapping is a clash.
                if (mappings.TryGetValue(key, out var existing)
                    && pin.Specification.Overrides is { } baseKey
                    && string.Equals(existing.Specification.Key, baseKey, StringComparison.Ordinal))
                {
                    mappings[key] = new CoveringMapping(pin, mapping);
                    continue;
                }

                if (existing is not null)
                {
                    problems.Add($"Concept {mapping.Concept.ConceptId} is mapped by both {existing.Specification.Key} and {pin.Key}; a study uses one mapping per concept and standard version.");
                    continue;
                }

                mappings[key] = new CoveringMapping(pin, mapping);
            }
        }
    }
}

/// <summary>
/// How a K-STD-1 mapping gap is recorded as a finding (ADR-2463): a <see cref="ValidationStage.Design"/> finding on the
/// Concept that has no mapping, with <see cref="FindingTarget.Dataset"/> <see cref="Dataset"/>. A study may publish a
/// CollectionSpec item whose Concept has no mapping only while such a finding is
/// <see cref="FindingStatus.Accepted"/>.
/// </summary>
public static class MappingGap
{
    /// <summary>The target dataset of a mapping-gap finding.</summary>
    public const string Dataset = "CollectionSpec";

    /// <summary>The target of the mapping-gap finding for <paramref name="concept"/>.</summary>
    public static FindingTarget Target(ConceptId concept) =>
        new(FindingTargetKind.DesignElement, Dataset, NodeRef.For(concept).ToString());
}
