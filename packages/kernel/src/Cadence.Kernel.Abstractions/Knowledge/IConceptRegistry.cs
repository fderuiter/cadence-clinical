using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Knowledge;

/// <summary>
/// Reads the tenant's Concept Registry (spec §6.1). Every lookup names a concept version or a code-system version:
/// there is no overload that resolves "the latest", so a reference can never change meaning under the fact that
/// holds it. Changes go through the kernel commands (<c>DefineConcept</c>, <c>MapConcept</c>, …).
/// </summary>
public interface IConceptRegistry
{
    /// <summary>The concept exactly as it was at <paramref name="concept"/>'s version, or null if there is no such version.</summary>
    Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default);

    /// <summary>The concept's current version, for authoring a new reference. Facts store the returned <see cref="ConceptRef"/>.</summary>
    Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default);

    /// <summary>The CDISC Biomedical Concept with this CDISC concept code, at its current version.</summary>
    Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves batches of CDISC Biomedical Concept codes, current <see cref="ConceptId"/>s, and pinned
    /// <see cref="ConceptRef"/>s in one tenant-scoped registry call.
    /// </summary>
    async Task<ConceptBatchLookupResult> LookupBatchAsync(
        TenantId tenant,
        ConceptBatchLookup lookup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var biomedical = new Dictionary<string, ConceptVersionView>(StringComparer.Ordinal);
        foreach (var rawCode in lookup.BiomedicalConceptCodes)
        {
            if (string.IsNullOrWhiteSpace(rawCode))
            {
                continue;
            }

            var trimmed = rawCode.Trim();
            if (!biomedical.ContainsKey(trimmed)
                && await FindBiomedicalConceptAsync(tenant, trimmed, cancellationToken).ConfigureAwait(false) is { } foundBc)
            {
                biomedical[trimmed] = foundBc;
            }
        }

        var current = new Dictionary<ConceptId, ConceptVersionView>();
        foreach (var conceptId in lookup.CurrentConcepts)
        {
            if (!current.ContainsKey(conceptId)
                && await GetCurrentAsync(tenant, conceptId, cancellationToken).ConfigureAwait(false) is { } foundCurrent)
            {
                current[conceptId] = foundCurrent;
            }
        }

        var pinned = new Dictionary<ConceptRef, ConceptVersionView>();
        foreach (var conceptRef in lookup.PinnedConcepts)
        {
            if (!pinned.ContainsKey(conceptRef)
                && await ResolveAsync(tenant, conceptRef, cancellationToken).ConfigureAwait(false) is { } foundPinned)
            {
                pinned[conceptRef] = foundPinned;
            }
        }

        return new ConceptBatchLookupResult(biomedical, current, pinned);
    }

    /// <summary>Resolves a batch of pinned <see cref="ConceptRef"/>s in one tenant-scoped registry call.</summary>
    async Task<IReadOnlyDictionary<ConceptRef, ConceptVersionView>> ResolveManyAsync(
        TenantId tenant,
        IReadOnlyCollection<ConceptRef> concepts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(concepts);
        if (concepts.Count == 0)
        {
            return ConceptBatchLookupResult.Empty.PinnedConcepts;
        }

        var batch = await LookupBatchAsync(tenant, new ConceptBatchLookup { PinnedConcepts = concepts }, cancellationToken)
            .ConfigureAwait(false);
        return batch.PinnedConcepts;
    }

    /// <summary>Resolves the current versions of a batch of <see cref="ConceptId"/>s in one tenant-scoped registry call.</summary>
    async Task<IReadOnlyDictionary<ConceptId, ConceptVersionView>> GetCurrentManyAsync(
        TenantId tenant,
        IReadOnlyCollection<ConceptId> concepts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(concepts);
        if (concepts.Count == 0)
        {
            return ConceptBatchLookupResult.Empty.CurrentConcepts;
        }

        var batch = await LookupBatchAsync(tenant, new ConceptBatchLookup { CurrentConcepts = concepts }, cancellationToken)
            .ConfigureAwait(false);
        return batch.CurrentConcepts;
    }

    /// <summary>Resolves a batch of CDISC Biomedical Concept codes at their current versions in one tenant-scoped registry call.</summary>
    async Task<IReadOnlyDictionary<string, ConceptVersionView>> FindBiomedicalConceptsAsync(
        TenantId tenant,
        IReadOnlyCollection<string> cdiscConceptCodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cdiscConceptCodes);
        if (cdiscConceptCodes.Count == 0)
        {
            return ConceptBatchLookupResult.Empty.BiomedicalConcepts;
        }

        var batch = await LookupBatchAsync(tenant, new ConceptBatchLookup { BiomedicalConceptCodes = cdiscConceptCodes }, cancellationToken)
            .ConfigureAwait(false);
        return batch.BiomedicalConcepts;
    }

    /// <summary>A named version of a code system. <paramref name="version"/> is required; a blank one throws.</summary>
    Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default);

    /// <summary>Every entry for the code in its pinned version, one per group it appears in.</summary>
    Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default);

    /// <summary>The entries of one group (a CDISC CT codelist) in the pinned version, in code order.</summary>
    Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// The concepts a code means, through confirmed mappings. A LOINC-coded lab result and an eCRF field for the same
    /// Biomedical Concept resolve to the same <see cref="ConceptRef"/>.
    /// </summary>
    Task<IReadOnlyList<ConceptMappingView>> ResolveCodingAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default);

    /// <summary>Every mapping of the concept, in any status, across its versions.</summary>
    Task<IReadOnlyList<ConceptMappingView>> GetMappingsAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default);
}

/// <summary>One version of a concept, with its mapping state derived from its mappings.</summary>
public sealed record ConceptVersionView(
    ConceptRef Concept,
    ConceptOrigin Origin,
    string? ExternalCode,
    ConceptKind Kind,
    string PreferredLabel,
    string Definition,
    ConceptStatus Status,
    int CurrentVersion,
    ConceptMappingState MappingState,
    DateTimeOffset RecordedAt);

/// <summary>A code-system version and where it came from.</summary>
public sealed record CodeSystemVersionView(
    CodeSystemVersionId Id,
    CodeSystemId CodeSystemId,
    string CodeSystemUri,
    string Version,
    DateOnly ReleasedOn,
    LicenseClass License,
    string PackageSha256,
    int EntryCount);

/// <summary>One entry of a code-system version.</summary>
public sealed record CodeSystemEntryView(
    CodeSystemVersionId CodeSystemVersionId,
    string Code,
    string Display,
    string? GroupCode,
    string? SubmissionValue,
    string? Definition,
    IReadOnlyList<string> Synonyms);

/// <summary>A mapping between a concept version and a pinned code.</summary>
public sealed record ConceptMappingView(
    ConceptMappingId Id,
    ConceptRef Concept,
    Coding Target,
    MappingRelation Relation,
    MappingStatus Status,
    string Evidence);

/// <summary>
/// Input specification for a tenant-scoped batch concept lookup across CDISC Biomedical Concept codes,
/// current <see cref="ConceptId"/>s, and pinned <see cref="ConceptRef"/>s.
/// </summary>
public sealed record ConceptBatchLookup
{
    /// <summary>CDISC Biomedical Concept codes to resolve at their current versions.</summary>
    public IReadOnlyCollection<string> BiomedicalConceptCodes { get; init; } = [];

    /// <summary>Concept identifiers to resolve at their current versions.</summary>
    public IReadOnlyCollection<ConceptId> CurrentConcepts { get; init; } = [];

    /// <summary>Pinned concept version references to resolve at their exact versions.</summary>
    public IReadOnlyCollection<ConceptRef> PinnedConcepts { get; init; } = [];
}

/// <summary>Outcome of a tenant-scoped batch concept lookup.</summary>
public sealed record ConceptBatchLookupResult(
    IReadOnlyDictionary<string, ConceptVersionView> BiomedicalConcepts,
    IReadOnlyDictionary<ConceptId, ConceptVersionView> CurrentConcepts,
    IReadOnlyDictionary<ConceptRef, ConceptVersionView> PinnedConcepts)
{
    /// <summary>Empty batch lookup result used when no keys are requested or found.</summary>
    public static readonly ConceptBatchLookupResult Empty = new(
        new Dictionary<string, ConceptVersionView>(StringComparer.Ordinal),
        new Dictionary<ConceptId, ConceptVersionView>(),
        new Dictionary<ConceptRef, ConceptVersionView>());
}
