using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards.Mapping;

namespace Cadence.Kernel.Standards.Conformance;

/// <summary>
/// One record of design facts (ADR-2464): a CollectionSpec Concept or item described by variables that design-stage
/// rules test. <see cref="Key"/> is the finding target: a version-less NodeRef, so a finding follows the element across
/// protocol versions.
/// </summary>
/// <param name="Dataset">The record type: <see cref="DesignConformance.ConceptDataset"/> or <see cref="DesignConformance.ItemDataset"/>.</param>
/// <param name="Key">The design element the record describes.</param>
/// <param name="Values">The facts by variable name; null is a fact that does not apply.</param>
public sealed record DesignConformanceRecord(string Dataset, NodeRef Key, IReadOnlyDictionary<string, string?> Values);

/// <summary>What the design facts need from the tenant's Concept Registry.</summary>
public interface IDesignTerminology
{
    /// <summary>The Concept Registry version a declared CT version resolves to, or null when the tenant has not imported it.</summary>
    Task<CodeSystemVersionId?> ResolveAsync(CodeSystemVersionRef version, CancellationToken cancellationToken);

    /// <summary>The submission values of <paramref name="codelist"/> in <paramref name="version"/>, or null when the version has no such codelist.</summary>
    Task<IReadOnlySet<string>?> SubmissionValuesAsync(CodeSystemVersionId version, string codelist, CancellationToken cancellationToken);
}

/// <summary>What a design is checked against: the design, the study's resolved mappings and its standards' packages.</summary>
/// <param name="Study">The study.</param>
/// <param name="Design">The protocol design.</param>
/// <param name="Mappings">The study's mappings, or null when it pins none that resolve.</param>
/// <param name="Packages">The registered package of each declared standard version.</param>
public sealed record DesignConformanceInput(
    StudyId Study,
    ProtocolDesign Design,
    StudyMappings? Mappings,
    IReadOnlyDictionary<StandardVersionId, StandardPackage> Packages);

/// <summary>
/// Design-time conformance facts (K-STD-2, ADR-2464). For each Concept the design's CollectionSpec items record, whether
/// the study maps it (K-STD-1); for each item, whether each mapping that covers it can produce a conformant
/// representation: the unit (a CT unit term and a conversion into the standard unit), the variable types, the codelist
/// the item's codes are decoded with, and the values the mapping writes (the standard's value lists, codelist terms and
/// length). The facts are data; the rules that
/// judge them, and their severities, are rule pack content evaluated at <see cref="Validation.ValidationStage.Design"/>.
/// Nothing here guesses: a fact that cannot be shown to conform is a problem.
/// </summary>
public static class DesignConformance
{
    /// <summary>
    /// Records keyed by Concept (<see cref="MappingGap.Target"/>), one per Concept the design collects: <see cref="Concept"/>,
    /// <see cref="Items"/>, <see cref="Mapped"/>.
    /// </summary>
    public const string ConceptDataset = MappingGap.Dataset;

    /// <summary>
    /// Records keyed by the item's protocol element, one per CollectionSpec item: <see cref="Item"/>, <see cref="Spec"/>,
    /// <see cref="Concept"/>, <see cref="Unit"/>, <see cref="Codelist"/>, <see cref="Mapped"/>, <see cref="Purpose"/> and the
    /// problem facts.
    /// </summary>
    public const string ItemDataset = "CollectionItem";

    /// <summary>Both record types; a conformance run covers both.</summary>
    public static IReadOnlyList<string> Datasets { get; } = [ConceptDataset, ItemDataset];

    /// <summary>The Concept reference.</summary>
    public const string Concept = "CONCEPT";

    /// <summary>The ids of the items that record the Concept, comma-separated.</summary>
    public const string Items = "ITEMS";

    /// <summary>The item's id.</summary>
    public const string Item = "ITEM";

    /// <summary>The id of the CollectionSpec the item belongs to.</summary>
    public const string Spec = "SPEC";

    /// <summary>The UCUM unit the item records.</summary>
    public const string Unit = "UNIT";

    /// <summary>The codelist the item's codes come from.</summary>
    public const string Codelist = "CODELIST";

    /// <summary><c>Y</c> when a pinned mapping specification maps the Concept, otherwise <c>N</c> (K-STD-1).</summary>
    public const string Mapped = "MAPPED";

    /// <summary><c>Y</c> when the item has a path to an endpoint, estimand, safety rule or operational need, otherwise <c>N</c> (K-PROT-3).</summary>
    public const string Purpose = "PURPOSE";

    /// <summary>Why the item's unit has no conformant representation, or null.</summary>
    public const string UnitProblem = "UNIT_PROBLEM";

    /// <summary>Why a variable the mapping writes is not in its dataset or has the wrong type for its value, or null.</summary>
    public const string TypeProblem = "TYPE_PROBLEM";

    /// <summary>
    /// Why the item's codes cannot be decoded as the mapping decodes them: the item records no codes, or its codelist or
    /// terminology version is not the one the mapping uses; the mapping's codelist is in none of the specification's
    /// terminology versions; or a terminology version the specification uses is not imported. Null when none applies.
    /// </summary>
    public const string TerminologyProblem = "CT_PROBLEM";

    /// <summary>Why a constant the mapping assigns is not one of the values the standard lists for its variable, or null.</summary>
    public const string ValueProblem = "VALUE_PROBLEM";

    /// <summary>
    /// Which values the mapping writes (assigned constants, unit terms, decoded terms) are not terms of the codelists
    /// their variables name, in the specification's terminology versions, or null. Codelists may be extensible, so this
    /// is a fact to confirm rather than proof of nonconformance.
    /// </summary>
    public const string TermProblem = "TERM_PROBLEM";

    /// <summary>Why a constant the mapping assigns is too long for a submission dataset, or null.</summary>
    public const string LengthProblem = "LENGTH_PROBLEM";

    /// <summary>The longest character value, in bytes of UTF-8, a submission dataset carries.</summary>
    public const int MaxCharacterBytes = 200;

    private const string Yes = "Y";
    private const string No = "N";

    /// <summary>The records of <paramref name="input"/>: Concept records first, then item records, each in design order.</summary>
    public static async Task<IReadOnlyList<DesignConformanceRecord>> BuildAsync(
        DesignConformanceInput input, IDesignTerminology terminology, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(terminology);

        var items = input.Design.CollectionSpecs.SelectMany(spec => spec.Items.Select(item => (Spec: spec, Item: item))).ToList();
        var withoutPurpose = ProtocolDesignReview.Findings(input.Design)
            .Where(f => string.Equals(f.Code, ProtocolDesignReview.ItemWithoutPurpose, StringComparison.Ordinal))
            .Select(f => f.ElementId)
            .ToHashSet(StringComparer.Ordinal);
        var checker = new MappingChecker(input.Packages, terminology);

        var records = new List<DesignConformanceRecord>();
        foreach (var concept in items.GroupBy(i => i.Item.Concept.ConceptId))
        {
            records.Add(new DesignConformanceRecord(ConceptDataset, NodeRef.For(concept.Key), new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Concept] = NodeRef.For(concept.Key).ToString(),
                [Items] = string.Join(",", concept.Select(i => i.Item.Id)),
                [Mapped] = Covering(input.Mappings, concept.Key).Count > 0 ? Yes : No,
            }));
        }

        foreach (var (spec, item) in items)
        {
            var covering = Covering(input.Mappings, item.Concept.ConceptId);
            var problems = new ItemProblems();
            foreach (var mapping in covering)
            {
                await checker.CheckAsync(item, mapping, problems, cancellationToken).ConfigureAwait(false);
            }

            records.Add(new DesignConformanceRecord(ItemDataset, NodeRef.For(ProtocolElementId.For(input.Study, item.Id)), new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Item] = item.Id,
                [Spec] = spec.Id,
                [Concept] = item.Concept.ToString(),
                [Unit] = item.Unit,
                [Codelist] = item.Terminology?.Codelist,
                [Mapped] = covering.Count > 0 ? Yes : No,
                [Purpose] = withoutPurpose.Contains(item.Id) ? No : Yes,
                [UnitProblem] = problems.UnitProblems.Text(),
                [TypeProblem] = problems.TypeProblems.Text(),
                [TerminologyProblem] = problems.TerminologyProblems.Text(),
                [ValueProblem] = problems.ValueProblems.Text(),
                [LengthProblem] = problems.LengthProblems.Text(),
                [TermProblem] = problems.TermProblems.Text(),
            }));
        }

        return records;
    }

    private static IReadOnlyList<CoveringMapping> Covering(StudyMappings? mappings, ConceptId concept) => mappings?.For(concept) ?? [];

    /// <summary>The problems of one item, by fact, in the order they were found.</summary>
    private sealed class ItemProblems
    {
        public Problems UnitProblems { get; } = new();

        public Problems TypeProblems { get; } = new();

        public Problems TerminologyProblems { get; } = new();

        public Problems ValueProblems { get; } = new();

        public Problems LengthProblems { get; } = new();

        public Problems TermProblems { get; } = new();
    }

    private sealed class Problems
    {
        private readonly List<string> _problems = [];

        public void Add(CoveringMapping covering, string problem) =>
            _problems.Add($"{covering.Specification.Key} v{covering.Specification.Version} mapping {covering.Mapping.Id}: {problem}");

        public string? Text() => _problems.Count == 0 ? null : string.Join("; ", _problems);
    }

    /// <summary>Checks an item against one mapping that covers it, reading the registry through a per-build cache.</summary>
    private sealed class MappingChecker(IReadOnlyDictionary<StandardVersionId, StandardPackage> packages, IDesignTerminology terminology)
    {
        private readonly Dictionary<CodeSystemVersionRef, CodeSystemVersionId?> _versions = [];
        private readonly Dictionary<(CodeSystemVersionId, string), IReadOnlySet<string>?> _codelists = [];

        public async Task CheckAsync(CollectionItem item, CoveringMapping covering, ItemProblems problems, CancellationToken cancellationToken)
        {
            var mapping = covering.Mapping;
            var specification = covering.Specification.Specification;
            var dataset = packages.TryGetValue(covering.Specification.StandardVersion, out var package)
                ? package.Datasets.FirstOrDefault(d => string.Equals(d.Name, mapping.Dataset, StringComparison.Ordinal))
                : null;
            if (dataset is null)
            {
                problems.TypeProblems.Add(covering, $"dataset {mapping.Dataset} is not in the registered package of the standard version it maps into.");
                return;
            }

            var variables = dataset.Variables.ToDictionary(v => v.Name, StringComparer.Ordinal);
            var (versions, unresolved) = await VersionsAsync(specification, cancellationToken).ConfigureAwait(false);
            foreach (var version in unresolved)
            {
                problems.TerminologyProblems.Add(covering, $"the specification's CT version {version.CodeSystem} {version.Version} is not imported into the Concept Registry.");
            }

            CheckUnit(item, covering, problems.UnitProblems);
            CheckTypes(covering, variables, problems.TypeProblems);
            await CheckTerminologyAsync(item, covering, variables, versions, problems, cancellationToken).ConfigureAwait(false);
            await CheckValuesAsync(covering, variables, versions, problems, cancellationToken).ConfigureAwait(false);
            CheckLengths(covering, problems.LengthProblems);
        }

        private static void CheckUnit(CollectionItem item, CoveringMapping covering, Problems problems)
        {
            var mapping = covering.Mapping;
            var specification = covering.Specification.Specification;
            if (item.Unit is not { } unit)
            {
                if (mapping.StandardUnit is { } standard)
                {
                    problems.Add(covering, $"the mapping standardises results into {standard}, but the item records no unit.");
                }

                return;
            }

            if (mapping.Codelist is not null)
            {
                problems.Add(covering, $"the mapping decodes a coded result, but the item records a unit ({unit}).");
                return;
            }

            if (mapping.Result.OriginalUnit is null && mapping.Result.StandardUnit is null)
            {
                problems.Add(covering, $"the mapping writes no unit, but the item records {unit}.");
                return;
            }

            if (mapping.Result.OriginalUnit is not null && specification.SubmissionValueOf(unit) is null)
            {
                problems.Add(covering, $"the unit {unit} has no controlled-terminology unit term in the specification.");
            }

            if (mapping.StandardUnit is { } standardUnit
                && !string.Equals(unit, standardUnit, StringComparison.Ordinal)
                && !mapping.Conversions.Any(c => string.Equals(c.From, unit, StringComparison.Ordinal)))
            {
                problems.Add(covering, $"there is no conversion from {unit} into the standard unit {standardUnit}.");
            }
        }

        private static void CheckTypes(CoveringMapping covering, Dictionary<string, VariableDefinitionData> variables, Problems problems)
        {
            var mapping = covering.Mapping;
            var result = mapping.Result;
            Expect(result.StandardNumeric, StandardDataType.Num, "the standardised numeric result");
            Expect(result.Original, StandardDataType.Char, "the original result");
            Expect(result.OriginalUnit, StandardDataType.Char, "the original unit");
            Expect(result.StandardCharacter, StandardDataType.Char, "the standardised character result");
            Expect(result.StandardUnit, StandardDataType.Char, "the standard unit");

            foreach (var (name, value) in mapping.Assign.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                if (Variable(name) is null)
                {
                    problems.Add(covering, $"{name} is not a variable of {mapping.Dataset}.");
                }
                else if (Variable(name) is { DataType: StandardDataType.Num }
                    && !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
                {
                    problems.Add(covering, $"{name} is numeric, but the mapping assigns \"{value}\".");
                }
            }

            void Expect(string? name, StandardDataType type, string what)
            {
                if (name is not null && Variable(name) is { } variable && variable.DataType != type)
                {
                    problems.Add(covering, $"{what} goes to {name}, which is {variable.DataType}, not {type}.");
                }
                else if (name is not null && Variable(name) is null)
                {
                    problems.Add(covering, $"{name} is not a variable of {mapping.Dataset}.");
                }
            }

            VariableDefinitionData? Variable(string name) => variables.GetValueOrDefault(name);
        }

        private async Task CheckTerminologyAsync(
            CollectionItem item,
            CoveringMapping covering,
            Dictionary<string, VariableDefinitionData> variables,
            IReadOnlyList<CodeSystemVersionId> versions,
            ItemProblems problems,
            CancellationToken cancellationToken)
        {
            var mapping = covering.Mapping;
            var specification = covering.Specification.Specification;
            if (item.Terminology?.Codelist is { } itemCodelist && !string.Equals(itemCodelist, mapping.Codelist, StringComparison.Ordinal))
            {
                problems.TerminologyProblems.Add(covering, $"the item records codes from {itemCodelist}, but the mapping decodes {(mapping.Codelist is null ? "no codelist" : mapping.Codelist)}.");
            }

            if (mapping.Codelist is { } codelist)
            {
                if (item.Terminology?.Codelist is null)
                {
                    problems.TerminologyProblems.Add(covering, $"the mapping decodes codes from {codelist}, but the item records no codes from a codelist.");
                }

                if (item.Terminology is { } binding && !versions.Contains(binding.CodeSystemVersionId))
                {
                    problems.TerminologyProblems.Add(covering, "the item records codes from a terminology version the specification does not use.");
                }

                // A decoded result is written as a submission value of the mapping's codelist, which may be a subset of
                // the codelist the variable names: every term it decodes to must be a term the variable takes.
                var decoded = await PermittedAsync(versions, [codelist], cancellationToken).ConfigureAwait(false);
                if (decoded is null)
                {
                    problems.TerminologyProblems.Add(covering, $"the mapping decodes codes from {codelist}, which none of the specification's terminology versions has.");
                }
                else if (variables.GetValueOrDefault(mapping.Result.Original) is { Codelists.Count: > 0 } original
                    && !original.Codelists.Contains(codelist, StringComparer.Ordinal))
                {
                    await CheckTermsAsync(covering, original, [.. decoded.Order(StringComparer.Ordinal)], versions, problems.TermProblems, cancellationToken).ConfigureAwait(false);
                }
            }

            // Unit terms are written to unit variables, whose values come from the standard's unit codelist.
            var units = new List<(string Variable, string Ucum)>();
            if (mapping.Result.OriginalUnit is { } originalUnit && item.Unit is { } unit && mapping.Codelist is null)
            {
                units.Add((originalUnit, unit));
            }

            if (mapping.Result.StandardUnit is { } standardUnitVariable && mapping.StandardUnit is { } standardUnit)
            {
                units.Add((standardUnitVariable, standardUnit));
            }

            foreach (var (variableName, ucum) in units)
            {
                if (specification.SubmissionValueOf(ucum) is { } term && variables.GetValueOrDefault(variableName) is { Codelists.Count: > 0 } variable)
                {
                    await CheckTermsAsync(covering, variable, [term], versions, problems.TermProblems, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task CheckValuesAsync(
            CoveringMapping covering,
            Dictionary<string, VariableDefinitionData> variables,
            IReadOnlyList<CodeSystemVersionId> versions,
            ItemProblems problems,
            CancellationToken cancellationToken)
        {
            foreach (var (name, value) in covering.Mapping.Assign.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                if (variables.GetValueOrDefault(name) is not { } variable)
                {
                    continue;
                }

                if (variable.ValueList.Count > 0 && !variable.ValueList.Contains(value, StringComparer.Ordinal))
                {
                    problems.ValueProblems.Add(covering, $"{name} = \"{value}\" is not one of the values the standard lists ({string.Join(", ", variable.ValueList)}).");
                }

                if (variable.Codelists.Count > 0)
                {
                    await CheckTermsAsync(covering, variable, [value], versions, problems.TermProblems, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Records each of <paramref name="values"/> that is not a term of <paramref name="variable"/>'s codelists in the
        /// specification's terminology versions. Codelists may be extensible, so this is a fact to confirm, not proof of
        /// nonconformance.
        /// </summary>
        private async Task CheckTermsAsync(
            CoveringMapping covering,
            VariableDefinitionData variable,
            IReadOnlyList<string> values,
            IReadOnlyList<CodeSystemVersionId> versions,
            Problems problems,
            CancellationToken cancellationToken)
        {
            var codelists = string.Join(", ", variable.Codelists);
            if (await PermittedAsync(versions, variable.Codelists, cancellationToken).ConfigureAwait(false) is not { } permitted)
            {
                problems.Add(covering, $"{variable.Name} takes values from {codelists}, which none of the specification's terminology versions has.");
                return;
            }

            foreach (var value in values.Where(v => !permitted.Contains(v)))
            {
                problems.Add(covering, $"{variable.Name} = \"{value}\" is not a term of {codelists}.");
            }
        }

        private static void CheckLengths(CoveringMapping covering, Problems problems)
        {
            foreach (var (name, value) in covering.Mapping.Assign.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                if (Encoding.UTF8.GetByteCount(value) > MaxCharacterBytes)
                {
                    problems.Add(covering, $"the value assigned to {name} is longer than {MaxCharacterBytes} bytes.");
                }
            }
        }

        /// <summary>The submission values of the codelists in the given versions, or null when no version has any of them.</summary>
        private async Task<HashSet<string>?> PermittedAsync(IReadOnlyList<CodeSystemVersionId> versions, IReadOnlyList<string> codelists, CancellationToken cancellationToken)
        {
            HashSet<string>? permitted = null;
            foreach (var version in versions)
            {
                foreach (var codelist in codelists)
                {
                    if (!_codelists.TryGetValue((version, codelist), out var values))
                    {
                        values = await terminology.SubmissionValuesAsync(version, codelist, cancellationToken).ConfigureAwait(false);
                        _codelists[(version, codelist)] = values;
                    }

                    if (values is not null)
                    {
                        permitted ??= new HashSet<string>(StringComparer.Ordinal);
                        permitted.UnionWith(values);
                    }
                }
            }

            return permitted;
        }

        /// <summary>The Concept Registry versions of the specification's CT versions, and the CT versions the tenant has not imported.</summary>
        private async Task<(IReadOnlyList<CodeSystemVersionId> Resolved, IReadOnlyList<CodeSystemVersionRef> Unresolved)> VersionsAsync(
            MappingSpecification specification, CancellationToken cancellationToken)
        {
            var resolved = new List<CodeSystemVersionId>();
            var unresolved = new List<CodeSystemVersionRef>();
            foreach (var version in specification.ControlledTerminology)
            {
                if (!_versions.TryGetValue(version, out var id))
                {
                    id = await terminology.ResolveAsync(version, cancellationToken).ConfigureAwait(false);
                    _versions[version] = id;
                }

                if (id is { } found)
                {
                    resolved.Add(found);
                }
                else
                {
                    unresolved.Add(version);
                }
            }

            return (resolved, unresolved);
        }
    }
}
