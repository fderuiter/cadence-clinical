using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Protocol.Design;

/// <summary>The kind of a protocol design element.</summary>
public enum ProtocolElementKind
{
    Objective,
    Endpoint,
    Estimand,
    Population,
    EligibilityCriterion,
    Arm,
    Epoch,
    Cohort,
    Intervention,
    PlannedActivity,
    CollectionSpec,
    CollectionItem,
    SafetyRule,
    DiscontinuationRule,
    RandomizationScheme,
    StatisticalDesign,
}

/// <summary>One element of a design with its kind.</summary>
public sealed record ProtocolElementEntry(ProtocolElementKind Kind, IProtocolElement Element)
{
    public string Id => Element.Id;
}

/// <summary>What a design refers to outside itself: concepts, pinned code-system versions and artifacts.</summary>
public sealed record DesignExternalReferences(
    IReadOnlySet<ConceptRef> Concepts,
    IReadOnlySet<CodeSystemVersionId> CodeSystemVersions,
    IReadOnlySet<ArtifactId> Artifacts);

/// <summary>The result of reading protocol content: the design, or every problem that stops it being one.</summary>
public sealed record ProtocolDesignRead(ProtocolDesign? Design, IReadOnlyList<string> Problems)
{
    public bool IsValid => Design is not null;
}

/// <summary>
/// Reads and writes the protocol design document (ADR-2472). Reading is strict: unknown properties, missing required
/// values, duplicate or malformed element ids and references to an element of the wrong kind are problems, all
/// reported together so an author can fix them at once.
/// </summary>
public static partial class ProtocolDesignDocument
{
    /// <summary>The longest element id.</summary>
    public const int MaxIdLength = 128;

    /// <summary>Serializes a design in the document's form (camel case, enums by name).</summary>
    public static string Write(ProtocolDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return JsonSerializer.Serialize(design, ProtocolDesignJsonContext.Default.ProtocolDesign);
    }

    /// <summary>Reads protocol content as a design and checks its structure.</summary>
    public static ProtocolDesignRead Read(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ProtocolDesign? design;
        try
        {
            design = JsonSerializer.Deserialize(content, ProtocolDesignJsonContext.Default.ProtocolDesign);
        }
        catch (JsonException ex)
        {
            return new ProtocolDesignRead(null, [ex.Path is { } path ? $"{path}: {ex.Message}" : ex.Message]);
        }
        catch (ArgumentException ex)
        {
            return new ProtocolDesignRead(null, [ex.Message]);
        }

        if (design is null)
        {
            return new ProtocolDesignRead(null, ["The protocol design is null."]);
        }

        var problems = Check(design);
        return problems.Count == 0 ? new ProtocolDesignRead(design, []) : new ProtocolDesignRead(null, problems);
    }

    /// <summary>Every element of the design with its kind, collection items after their specification.</summary>
    public static IEnumerable<ProtocolElementEntry> Elements(this ProtocolDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        IProtocolElement?[] references = [design.RandomizationScheme, design.StatisticalDesign];
        return Of(ProtocolElementKind.Objective, design.Objectives)
            .Concat(Of(ProtocolElementKind.Endpoint, design.Endpoints))
            .Concat(Of(ProtocolElementKind.Estimand, design.Estimands))
            .Concat(Of(ProtocolElementKind.Population, design.Populations))
            .Concat(Of(ProtocolElementKind.EligibilityCriterion, design.EligibilityCriteria))
            .Concat(Of(ProtocolElementKind.Arm, design.Arms))
            .Concat(Of(ProtocolElementKind.Epoch, design.Epochs))
            .Concat(Of(ProtocolElementKind.Cohort, design.Cohorts))
            .Concat(Of(ProtocolElementKind.Intervention, design.Interventions))
            .Concat(Of(ProtocolElementKind.PlannedActivity, design.PlannedActivities))
            .Concat(design.CollectionSpecs.Where(spec => spec is not null).SelectMany(spec =>
                Of(ProtocolElementKind.CollectionSpec, [spec]).Concat(Of(ProtocolElementKind.CollectionItem, spec.Items))))
            .Concat(Of(ProtocolElementKind.SafetyRule, design.SafetyRules))
            .Concat(Of(ProtocolElementKind.DiscontinuationRule, design.DiscontinuationRules))
            .Concat(Of(ProtocolElementKind.RandomizationScheme, references[..1]))
            .Concat(Of(ProtocolElementKind.StatisticalDesign, references[1..]));

        static IEnumerable<ProtocolElementEntry> Of(ProtocolElementKind kind, IEnumerable<IProtocolElement?>? elements) =>
            (elements ?? []).OfType<IProtocolElement>().Select(e => new ProtocolElementEntry(kind, e));
    }

    /// <summary>Every concept, code-system version and artifact the design refers to (K-SEM-1).</summary>
    public static DesignExternalReferences ExternalReferences(this ProtocolDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        var concepts = new HashSet<ConceptRef>();
        concepts.UnionWith(design.Endpoints.Where(e => e.Concept is not null).Select(e => e.Concept!.Value));
        concepts.UnionWith(design.EligibilityCriteria.SelectMany(c => c.Concepts));
        concepts.UnionWith(design.Interventions.Where(i => i.Product is not null).Select(i => i.Product!.Value));
        concepts.UnionWith(design.CollectionSpecs.SelectMany(s => s.Items).Select(i => i.Concept));
        concepts.UnionWith(design.SafetyRules.Concat(design.DiscontinuationRules).SelectMany(r => r.Concepts));

        var codeSystems = design.CollectionSpecs.SelectMany(s => s.Items)
            .Where(i => i.Terminology is not null)
            .Select(i => i.Terminology!.CodeSystemVersionId)
            .ToHashSet();
        var artifacts = new[] { design.RandomizationScheme, design.StatisticalDesign }
            .Where(r => r?.Artifact is not null)
            .Select(r => r!.Artifact!.Value)
            .ToHashSet();
        return new DesignExternalReferences(concepts, codeSystems, artifacts);
    }

    /// <summary>Lists that contain null entries, which JSON allows but a design does not.</summary>
    private static IEnumerable<string> NullEntries(ProtocolDesign design)
    {
        (string Name, IEnumerable<object?> Items)[] lists =
        [
            ("objectives", design.Objectives), ("endpoints", design.Endpoints), ("estimands", design.Estimands),
            ("populations", design.Populations), ("eligibilityCriteria", design.EligibilityCriteria), ("arms", design.Arms),
            ("epochs", design.Epochs), ("cohorts", design.Cohorts), ("interventions", design.Interventions),
            ("plannedActivities", design.PlannedActivities), ("collectionSpecs", design.CollectionSpecs),
            ("safetyRules", design.SafetyRules), ("discontinuationRules", design.DiscontinuationRules),
        ];
        var nested = design.CollectionSpecs.OfType<CollectionSpec>().Select(s => (Name: $"{s.Id}.items", Items: (IEnumerable<object?>)s.Items))
            .Concat(design.Estimands.OfType<Estimand>().Select(e => (Name: $"{e.Id}.intercurrentEvents", Items: (IEnumerable<object?>)e.IntercurrentEvents)))
            .Concat(design.Arms.OfType<Arm>().Select(a => (Name: $"{a.Id}.interventionIds", Items: (IEnumerable<object?>)a.InterventionIds)))
            .Concat(design.Estimands.OfType<Estimand>().Select(e => (Name: $"{e.Id}.armIds", Items: (IEnumerable<object?>)e.ArmIds)))
            .Concat(design.Estimands.OfType<Estimand>().Select(e => (Name: $"{e.Id}.interventionIds", Items: (IEnumerable<object?>)(e.InterventionIds ?? []))))
            .Concat(design.Populations.OfType<Population>().Select(p => (Name: $"{p.Id}.criterionIds", Items: (IEnumerable<object?>)p.CriterionIds)))
            .Concat(design.Cohorts.OfType<Cohort>().Select(c => (Name: $"{c.Id}.armIds", Items: (IEnumerable<object?>)c.ArmIds)))
            .Concat(design.PlannedActivities.OfType<PlannedActivity>().Select(a => (Name: $"{a.Id}.collectionSpecIds", Items: (IEnumerable<object?>)a.CollectionSpecIds)));
        return lists.Concat(nested).Where(list => list.Items.Any(item => item is null)).Select(list => $"{list.Name} contains null.");
    }

    private static List<string> Check(ProtocolDesign design)
    {
        var problems = NullEntries(design).ToList();
        if (problems.Count > 0)
        {
            return problems;
        }

        if (design.SchemaVersion == 0)
        {
            problems.Add($"schemaVersion is required; expected {ProtocolDesign.CurrentSchemaVersion}.");
        }
        else if (design.SchemaVersion != ProtocolDesign.CurrentSchemaVersion)
        {
            problems.Add($"schemaVersion {design.SchemaVersion} is not supported; expected {ProtocolDesign.CurrentSchemaVersion}.");
        }

        var kinds = new Dictionary<string, ProtocolElementKind>(StringComparer.Ordinal);
        foreach (var entry in design.Elements())
        {
            if (entry.Id is null || !IdPattern().IsMatch(entry.Id))
            {
                problems.Add($"{entry.Kind} id '{entry.Id}' must start with a letter and use only letters, digits and _ . : - (at most {MaxIdLength}).");
            }
            else if (!kinds.TryAdd(entry.Id, entry.Kind))
            {
                problems.Add($"Element id '{entry.Id}' is used more than once.");
            }
        }

        var check = new Checker(kinds, problems);
        check.Text("design", "title", design.Title);
        foreach (var o in design.Objectives)
        {
            check.Text(o.Id, "text", o.Text);
            check.Defined(o.Id, o.Level);
        }

        foreach (var e in design.Endpoints)
        {
            check.Ref(e.Id, "objectiveId", e.ObjectiveId, ProtocolElementKind.Objective);
            check.Text(e.Id, "text", e.Text);
            check.Defined(e.Id, e.Level);
        }

        foreach (var e in design.Estimands)
        {
            check.Ref(e.Id, "endpointId", e.EndpointId, ProtocolElementKind.Endpoint);
            check.Ref(e.Id, "populationId", e.PopulationId, ProtocolElementKind.Population);
            check.Refs(e.Id, "armIds", e.ArmIds, ProtocolElementKind.Arm);
            if (e.InterventionIds is not null)
            {
                check.Refs(e.Id, "interventionIds", e.InterventionIds, ProtocolElementKind.Intervention);
            }

            check.Text(e.Id, "variable", e.Variable);
            check.Text(e.Id, "summaryMeasure", e.SummaryMeasure);
            foreach (var ice in e.IntercurrentEvents ?? [])
            {
                check.Text(e.Id, "intercurrentEvents.description", ice?.Description);
                if (ice is not null)
                {
                    check.Defined(e.Id, ice.Strategy);
                    if (ice.Strategy == IntercurrentEventStrategy.Other)
                    {
                        check.Text(e.Id, "intercurrentEvents.strategyText", ice.StrategyText);
                    }
                }
            }
        }

        foreach (var p in design.Populations)
        {
            check.Text(p.Id, "name", p.Name);
            check.Refs(p.Id, "criterionIds", p.CriterionIds, ProtocolElementKind.EligibilityCriterion);
        }

        foreach (var c in design.EligibilityCriteria)
        {
            check.Text(c.Id, "text", c.Text);
            check.Defined(c.Id, c.Category);
            check.NotNull(c.Id, "concepts", c.Concepts);
        }

        foreach (var a in design.Arms)
        {
            check.Text(a.Id, "name", a.Name);
            check.Defined(a.Id, a.Type);
            check.Refs(a.Id, "interventionIds", a.InterventionIds, ProtocolElementKind.Intervention);
        }

        foreach (var e in design.Epochs)
        {
            check.Text(e.Id, "name", e.Name);
        }

        foreach (var c in design.Cohorts)
        {
            check.Text(c.Id, "name", c.Name);
            check.Refs(c.Id, "armIds", c.ArmIds, ProtocolElementKind.Arm);
        }

        foreach (var i in design.Interventions)
        {
            check.Text(i.Id, "name", i.Name);
            if (i.Dose is { } dose && (dose.Amount <= 0 || string.IsNullOrWhiteSpace(dose.Unit)))
            {
                problems.Add($"{i.Id}: a dose needs a positive amount and a UCUM unit.");
            }
        }

        foreach (var a in design.PlannedActivities)
        {
            check.Text(a.Id, "name", a.Name);
            check.Ref(a.Id, "epochId", a.EpochId, ProtocolElementKind.Epoch);
            check.Refs(a.Id, "collectionSpecIds", a.CollectionSpecIds, ProtocolElementKind.CollectionSpec);
            check.NotNull(a.Id, "locations", a.Locations);
            foreach (var location in a.Locations ?? [])
            {
                check.Defined(a.Id, location);
            }

            if (a.Timing is { } timing && (timing.WindowBefore < 0 || timing.WindowAfter < 0))
            {
                problems.Add($"{a.Id}: timing windows cannot be negative.");
            }

            check.Purpose(a.Id, a.Purpose);
        }

        foreach (var spec in design.CollectionSpecs)
        {
            check.Text(spec.Id, "name", spec.Name);
            check.NotNull(spec.Id, "items", spec.Items);
            check.Purpose(spec.Id, spec.Purpose);
            foreach (var item in spec.Items ?? [])
            {
                if (item.Unit is { } unit && string.IsNullOrWhiteSpace(unit))
                {
                    problems.Add($"{item.Id}: unit is blank; leave it out or give a UCUM code.");
                }

                if (item.Terminology is { } binding && binding.CodeSystemVersionId.IsEmpty)
                {
                    problems.Add($"{item.Id}: terminology names no code-system version.");
                }

                check.Purpose(item.Id, item.Purpose);
            }
        }

        foreach (var rule in design.SafetyRules.Concat(design.DiscontinuationRules))
        {
            check.Text(rule.Id, "text", rule.Text);
            check.NotNull(rule.Id, "concepts", rule.Concepts);
        }

        foreach (var reference in new[] { design.RandomizationScheme, design.StatisticalDesign }.OfType<DesignReference>())
        {
            check.Text(reference.Id, "title", reference.Title);
            if (reference.Artifact is { IsEmpty: true })
            {
                problems.Add($"{reference.Id}: artifact is empty; leave it out or name the artifact.");
            }
        }

        return problems;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex IdPattern();

    private sealed class Checker(IReadOnlyDictionary<string, ProtocolElementKind> kinds, List<string> problems)
    {
        public void Text(string owner, string field, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{owner}: {field} is required.");
            }
        }

        public void NotNull<T>(string owner, string field, T? value)
            where T : class
        {
            if (value is null)
            {
                problems.Add($"{owner}: {field} is required (use [] for none).");
            }
        }

        public void Defined<TEnum>(string owner, TEnum value)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(value))
            {
                problems.Add($"{owner}: {typeof(TEnum).Name} {value} is not defined.");
            }
        }

        public void Ref(string owner, string field, string? target, ProtocolElementKind expected)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                problems.Add($"{owner}: {field} is required.");
            }
            else if (!kinds.TryGetValue(target, out var actual))
            {
                problems.Add($"{owner}: {field} '{target}' is not an element of this design.");
            }
            else if (actual != expected)
            {
                problems.Add($"{owner}: {field} '{target}' is of kind {actual}, not {expected}.");
            }
        }

        public void Refs(string owner, string field, IReadOnlyList<string>? targets, ProtocolElementKind expected)
        {
            if (targets is null)
            {
                problems.Add($"{owner}: {field} is required (use [] for none).");
                return;
            }

            foreach (var target in targets)
            {
                Ref(owner, field, target, expected);
            }
        }

        public void Purpose(string owner, DataPurpose? purpose)
        {
            if (purpose is null)
            {
                return;
            }

            Refs(owner, "purpose.endpointIds", purpose.EndpointIds, ProtocolElementKind.Endpoint);
            Refs(owner, "purpose.estimandIds", purpose.EstimandIds, ProtocolElementKind.Estimand);
            Refs(owner, "purpose.safetyRuleIds", purpose.SafetyRuleIds, ProtocolElementKind.SafetyRule);
        }
    }
}

/// <summary>Source-generated JSON for the protocol design document: camel case, enums by name, strict.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    AllowDuplicateProperties = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProtocolDesign))]
internal sealed partial class ProtocolDesignJsonContext : JsonSerializerContext;
