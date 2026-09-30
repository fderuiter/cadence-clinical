using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Application.Standards;

/// <summary>Validates mapping specification content when it is drafted (<see cref="MappingSpecificationJson"/>).</summary>
internal sealed class MappingSpecificationContentValidator : ILibraryContentValidator
{
    public LibraryItemKind Kind => LibraryItemKind.MappingSpecification;

    public IReadOnlyList<string> Validate(JsonElement content) => MappingSpecificationJson.Read(content).Problems;
}

/// <summary>
/// What a mapping specification must resolve to when it is drafted (ADR-2463): exactly one pinned standard version whose
/// datasets hold every variable the specification writes, CT versions the tenant has imported and whose codelists hold
/// the decodes, the Concept versions it maps, and an existing base when it overrides one. The standard version, the CT
/// versions and the Concept versions become <see cref="LineageEdgeKind.DefinedBy"/> inputs of the drafted version; the
/// base is named by key, not version, so it is checked but not linked.
/// </summary>
internal static class MappingSpecificationReferences
{
    public static async Task<(IReadOnlyList<NodeRef> Inputs, KernelError? Error)> ResolveAsync(
        DraftLibraryItem command, IStandardsRegistry registry, IConceptRegistry concepts, ICommandContext context, CancellationToken cancellationToken)
    {
        var specification = MappingSpecificationJson.Parse(command.Content);
        var standards = command.StandardVersions.Distinct().ToList();
        if (standards.Count != 1)
        {
            return ([], Invalid($"A mapping specification maps into exactly one standard version; this one names {standards.Count}."));
        }

        var inputs = new List<NodeRef>();
        if (await context.Work.ReadAsync<StandardVersion>(standards[0], cancellationToken).ConfigureAwait(false) is not { } standard
            || await registry.GetStandardPackageAsync(command.TenantId, standards[0], cancellationToken).ConfigureAwait(false) is not { } package)
        {
            return ([], StandardsErrors.StandardVersionUnknown.WithMessage($"Standard version {standards[0]} does not exist."));
        }

        inputs.Add(NodeRef.For(standard.Id, standard.VersionIndex));
        if (UnknownVariable(specification, package) is { } unknown)
        {
            return ([], Invalid(unknown));
        }

        var terminology = await TerminologyResolution.ResolveAsync(concepts, command.TenantId, specification.ControlledTerminology, cancellationToken).ConfigureAwait(false);
        if (specification.ControlledTerminology.FirstOrDefault(t => !terminology.ContainsKey(t)) is { } missing)
        {
            return ([], Invalid($"{missing} has not been imported into this tenant's Concept Registry; import the package before mapping with it."));
        }

        inputs.AddRange(terminology.Values.Distinct().OrderBy(v => v.Value).Select(v => NodeRef.For(v, 1)));
        foreach (var codelist in specification.Mappings.Select(m => m.Codelist).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!await AnyHoldsAsync(concepts, command.TenantId, terminology.Values, codelist, cancellationToken).ConfigureAwait(false))
            {
                return ([], Invalid($"Codelist {codelist} is not in any controlled-terminology version the specification names."));
            }
        }

        foreach (var concept in specification.Mappings.Select(m => m.Concept).Distinct().OrderBy(c => c.ConceptId.Value).ThenBy(c => c.Version))
        {
            var current = await context.Work.ReadAsync<Concept>(concept.ConceptId, cancellationToken).ConfigureAwait(false);
            if (current is null || concept.Version > current.Version)
            {
                return ([], Invalid($"Concept {concept} does not exist in this tenant."));
            }

            inputs.Add(NodeRef.For(concept.ConceptId, concept.Version));
        }

        if (specification.Overrides is { } baseKey)
        {
            var bases = await registry.ListLibraryItemVersionsAsync(command.TenantId, LibraryItemKind.MappingSpecification, baseKey, cancellationToken).ConfigureAwait(false);
            if (string.Equals(baseKey, command.Key, StringComparison.Ordinal) || bases.Count == 0)
            {
                return ([], Invalid($"A mapping specification overrides another one; \"{baseKey}\" is not a mapping specification key in this tenant."));
            }
        }

        return (inputs, null);
    }

    private static string? UnknownVariable(MappingSpecification specification, StandardPackage package)
    {
        var datasets = package.Datasets.ToDictionary(d => d.Name, d => d.Variables.Select(v => v.Name).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var mapping in specification.Mappings)
        {
            if (!datasets.TryGetValue(mapping.Dataset, out var variables))
            {
                return $"Mapping \"{mapping.Id}\": {package.StandardCode} {package.Version} has no dataset {mapping.Dataset}.";
            }

            if (mapping.Variables().FirstOrDefault(v => !variables.Contains(v)) is { } variable)
            {
                return $"Mapping \"{mapping.Id}\": {package.StandardCode} {package.Version} has no variable {mapping.Dataset}.{variable}.";
            }
        }

        return null;
    }

    private static async Task<bool> AnyHoldsAsync(
        IConceptRegistry concepts, TenantId tenant, IEnumerable<CodeSystemVersionId> versions, string codelist, CancellationToken cancellationToken)
    {
        foreach (var version in versions.Distinct())
        {
            if ((await concepts.GetGroupAsync(tenant, version, codelist, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static KernelError Invalid(string message) => StandardsErrors.LibraryItemInvalid.WithMessage(message);
}

/// <summary>Reads the mapping specifications a study pinned.</summary>
internal static class PinnedMappings
{
    /// <summary>Resolves a study's pinned mapping specifications over its declared standards (<see cref="StudyMappings.ResolvePins"/>).</summary>
    public static (StudyMappings? Mappings, IReadOnlyList<string> Problems) Resolve(IEnumerable<LibraryItemView> pins, IEnumerable<StandardVersionId> declaredStandards) =>
        StudyMappings.ResolvePins(pins, declaredStandards);

    /// <summary>Reads the study's pinned library items.</summary>
    public static async Task<IReadOnlyList<LibraryItemView>> LoadAsync(
        IStandardsRegistry registry, TenantId tenant, IEnumerable<LibraryItemId> pins, CancellationToken cancellationToken)
    {
        var items = new List<LibraryItemView>();
        foreach (var id in pins.Distinct())
        {
            if (await registry.FindLibraryItemAsync(tenant, id, cancellationToken).ConfigureAwait(false) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }
}

/// <summary>
/// K-STD-1 (spec §6.3, ADR-2463): a protocol version cannot be submitted while a Concept one of its CollectionSpec items
/// records has no mapping in the study's declared standards, unless the gap is an accepted finding
/// (<see cref="MappingGap"/>). A study that has declared no standards has nothing to map into. The protocol version
/// records each mapping specification version it relies on as a <see cref="LineageEdgeKind.MappedBy"/> input.
/// </summary>
internal sealed class MappingCoverageCheck(IStandardsRegistry registry, IValidationFindings findings) : IProtocolSubmissionCheck
{
    /// <summary>The most uncovered items one refusal lists.</summary>
    private const int MaxListed = 10;

    public async Task<KernelError?> CheckAsync(ProtocolSubmission submission, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var (study, design) = (submission.Study, submission.Design);
        if (await registry.FindStudyStandardsAsync(context.TenantId, study, cancellationToken).ConfigureAwait(false) is not { } profile)
        {
            return null;
        }

        var pins = await PinnedMappings.LoadAsync(registry, context.TenantId, profile.LibraryPins, cancellationToken).ConfigureAwait(false);
        var (mappings, problems) = PinnedMappings.Resolve(pins, profile.StandardVersions);
        if (mappings is null)
        {
            return StandardsErrors.MappingMissing.WithMessage(string.Join(" ", problems));
        }

        var items = design.CollectionSpecs.SelectMany(spec => spec.Items.Select(item => (Spec: spec.Id, Item: item))).ToList();
        var used = new SortedSet<NodeRef>(Comparer<NodeRef>.Create((a, b) => string.CompareOrdinal(a.ToString(), b.ToString())));
        var uncovered = new List<(string Spec, CollectionItem Item)>();
        foreach (var entry in items)
        {
            var covering = mappings.For(entry.Item.Concept.ConceptId);
            if (covering.Count == 0)
            {
                uncovered.Add(entry);
            }

            used.UnionWith(covering.Select(c => c.Specification.Node));
        }

        if (uncovered.Count > 0)
        {
            var accepted = await AcceptedGapsAsync(context.TenantId, study, cancellationToken).ConfigureAwait(false);
            uncovered.RemoveAll(entry => accepted.Contains(MappingGap.Target(entry.Item.Concept.ConceptId).Key));
        }

        if (uncovered.Count > 0)
        {
            var listed = string.Join("; ", uncovered.Take(MaxListed).Select(u => $"{u.Spec}.{u.Item.Id} (concept {u.Item.Concept})"));
            var more = uncovered.Count > MaxListed ? $" and {uncovered.Count - MaxListed} more" : string.Empty;
            return StandardsErrors.MappingMissing.WithMessage(
                $"{StandardsErrors.MappingMissing.Message} Unmapped: {listed}{more}. Pin a mapping specification that maps each concept, or accept the gap as a Design finding.");
        }

        foreach (var node in used)
        {
            context.Work.LinkInput(node, LineageEdgeKind.MappedBy);
        }

        return null;
    }

    /// <summary>The target keys of the study's accepted mapping-gap findings.</summary>
    private async Task<HashSet<string>> AcceptedGapsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var query = new FindingQuery(study)
        {
            Stage = ValidationStage.Design,
            Statuses = [FindingStatus.Accepted],
            Dataset = MappingGap.Dataset,
            Take = FindingQuery.MaxTake,
        };

        while (true)
        {
            var page = await findings.ListAsync(tenant, query, cancellationToken).ConfigureAwait(false);
            keys.UnionWith(page.Findings.Where(f => f.Target.Kind == FindingTargetKind.DesignElement).Select(f => f.Target.Key));
            if (page.Findings.Count < query.Take || query.Skip + page.Findings.Count >= page.Total)
            {
                return keys;
            }

            query = query with { Skip = query.Skip + page.Findings.Count };
        }
    }
}
