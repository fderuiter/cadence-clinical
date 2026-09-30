using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;

namespace Cadence.Standards.Blocks;

/// <summary>A collection specification instantiated for a protocol design, or why it could not be.</summary>
/// <param name="Spec">The specification, when every item resolved.</param>
/// <param name="Problems">What did not resolve; empty on success.</param>
public sealed record CollectionSpecInstantiation(CollectionSpec? Spec, IReadOnlyList<string> Problems)
{
    /// <summary>True when <see cref="Spec"/> was built.</summary>
    public bool IsSuccess => Spec is not null;
}

/// <summary>
/// Turns library content (<see cref="CollectionSpecificationDefinition"/>) into a protocol design's
/// <see cref="CollectionSpec"/> for one tenant (ADR-2503). Each Biomedical Concept C-code resolves to the tenant's
/// concept version and each terminology reference to its pinned code-system version (and codelist, when one is named).
/// Nothing is guessed: a concept, version or codelist the tenant's Concept Registry does not hold is a problem, and
/// every problem is reported.
/// </summary>
public static class CollectionSpecificationInstantiator
{
    /// <summary>
    /// Instantiates <paramref name="definition"/> as the design element <paramref name="specId"/>. Item ids become
    /// <c>{specId}.{itemId}</c>, so the same library item can be used more than once in one design.
    /// </summary>
    public static async Task<CollectionSpecInstantiation> InstantiateAsync(
        CollectionSpecificationDefinition definition,
        string specId,
        IConceptRegistry registry,
        TenantId tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registry);
        if (string.IsNullOrEmpty(specId) || !CollectionSpecificationJson.IsItemId(specId))
        {
            return new(null, [$"'{specId}' is not a valid specification id: it must start with a letter and hold up to {CollectionSpecificationJson.MaxItemIdLength} characters: letters, digits, '_', '-', ':' or '.'."]);
        }

        var problems = new List<string>();
        var items = new List<CollectionItem>(definition.Items.Count);
        foreach (var item in definition.Items)
        {
            var concept = await registry.FindBiomedicalConceptAsync(tenant, item.BiomedicalConcept, cancellationToken).ConfigureAwait(false);
            if (concept is null)
            {
                problems.Add($"Item '{item.Id}': the Concept Registry has no Biomedical Concept {item.BiomedicalConcept}.");
            }

            TerminologyBinding? binding = null;
            if (item.Terminology is { } reference)
            {
                binding = await ResolveTerminologyAsync(item.Id, reference, registry, tenant, problems, cancellationToken).ConfigureAwait(false);
            }

            if (concept is not null && (item.Terminology is null || binding is not null))
            {
                items.Add(new CollectionItem($"{specId}.{item.Id}", concept.Concept, item.Unit, binding, item.Required));
            }
        }

        return problems.Count > 0
            ? new(null, problems)
            : new(new CollectionSpec(specId, definition.Name, items), problems);
    }

    private static async Task<TerminologyBinding?> ResolveTerminologyAsync(
        string itemId,
        TerminologyReference reference,
        IConceptRegistry registry,
        TenantId tenant,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        var version = await registry.FindCodeSystemVersionAsync(tenant, reference.CodeSystem, reference.Version, cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            problems.Add($"Item '{itemId}': the Concept Registry has no version {reference.Version} of {reference.CodeSystem}.");
            return null;
        }

        if (reference.Codelist is { } codelist)
        {
            var entries = await registry.GetGroupAsync(tenant, version.Id, codelist, cancellationToken).ConfigureAwait(false);
            if (entries.Count == 0)
            {
                problems.Add($"Item '{itemId}': {reference.CodeSystem} version {reference.Version} has no codelist {codelist}.");
                return null;
            }
        }

        return new TerminologyBinding(version.Id, reference.Codelist);
    }
}
