using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Knowledge;

/// <summary>
/// Reads the Concept Registry through a context bound to the caller's tenant, so Row-Level Security limits every query
/// to that tenant's terminology (K-ID-2). Read-only and untracked; batch lookups hold a single connection lease so
/// <see cref="KernelTenantConnectionInterceptor"/> sets <c>cadence.tenant_id</c> once per call.
/// </summary>
internal sealed class KernelConceptRegistry(KernelTenantDbContextFactory contexts) : IConceptRegistry
{
    public async Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default)
    {
        var batch = await LookupBatchAsync(tenant, new ConceptBatchLookup { PinnedConcepts = [concept] }, cancellationToken)
            .ConfigureAwait(false);
        return batch.PinnedConcepts.GetValueOrDefault(concept);
    }

    public async Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default)
    {
        var batch = await LookupBatchAsync(tenant, new ConceptBatchLookup { CurrentConcepts = [concept] }, cancellationToken)
            .ConfigureAwait(false);
        return batch.CurrentConcepts.GetValueOrDefault(concept);
    }

    public async Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cdiscConceptCode);
        var trimmed = cdiscConceptCode.Trim();
        var batch = await LookupBatchAsync(tenant, new ConceptBatchLookup { BiomedicalConceptCodes = [trimmed] }, cancellationToken)
            .ConfigureAwait(false);
        return batch.BiomedicalConcepts.GetValueOrDefault(trimmed);
    }

    public async Task<ConceptBatchLookupResult> LookupBatchAsync(
        TenantId tenant,
        ConceptBatchLookup lookup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var bcCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in lookup.BiomedicalConceptCodes)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                bcCodes.Add(raw.Trim());
            }
        }

        var currentIds = lookup.CurrentConcepts.Count > 0
            ? new HashSet<ConceptId>(lookup.CurrentConcepts)
            : [];
        var pinnedRefs = lookup.PinnedConcepts.Count > 0
            ? new HashSet<ConceptRef>(lookup.PinnedConcepts)
            : [];

        var wantedIds = new HashSet<ConceptId>(currentIds);
        foreach (var pinnedRef in pinnedRefs)
        {
            wantedIds.Add(pinnedRef.ConceptId);
        }

        if (bcCodes.Count == 0 && wantedIds.Count == 0)
        {
            return ConceptBatchLookupResult.Empty;
        }

        await using var db = contexts.Create(tenant);
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<Concept> headers;
            if (bcCodes.Count > 0 && wantedIds.Count > 0)
            {
                var idList = wantedIds.ToList();
                var codeList = bcCodes.ToList();
                headers = await db.Concepts.AsNoTracking()
                    .Where(c => idList.Contains(c.Id) || (c.Origin == ConceptOrigin.CdiscBiomedicalConcept && c.ExternalCode != null && codeList.Contains(c.ExternalCode)))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (bcCodes.Count > 0)
            {
                var codeList = bcCodes.ToList();
                headers = await db.Concepts.AsNoTracking()
                    .Where(c => c.Origin == ConceptOrigin.CdiscBiomedicalConcept && c.ExternalCode != null && codeList.Contains(c.ExternalCode))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var idList = wantedIds.ToList();
                headers = await db.Concepts.AsNoTracking()
                    .Where(c => idList.Contains(c.Id))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            }

            if (headers.Count == 0)
            {
                return ConceptBatchLookupResult.Empty;
            }

            var headersById = new Dictionary<ConceptId, Concept>(headers.Count);
            foreach (var header in headers)
            {
                headersById[header.Id] = header;
            }

            var foundConceptIds = headersById.Keys.ToList();
            var revisions = await db.ConceptRevisions.AsNoTracking()
                .Where(r => foundConceptIds.Contains(r.ConceptId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var revisionsByKey = new Dictionary<(ConceptId ConceptId, int Version), ConceptRevision>(revisions.Count);
            foreach (var revision in revisions)
            {
                revisionsByKey[(revision.ConceptId, revision.Version)] = revision;
            }

            var activeMappings = await db.ConceptMappings.AsNoTracking()
                .Where(m => foundConceptIds.Contains(m.ConceptId) && m.Status != MappingStatus.Rejected)
                .Select(m => new { m.ConceptId, m.Status })
                .Distinct()
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var mappingStates = new Dictionary<ConceptId, ConceptMappingState>();
            foreach (var row in activeMappings)
            {
                if (row.Status == MappingStatus.Confirmed)
                {
                    mappingStates[row.ConceptId] = ConceptMappingState.Mapped;
                }
                else if (!mappingStates.ContainsKey(row.ConceptId))
                {
                    mappingStates[row.ConceptId] = ConceptMappingState.Proposed;
                }
            }

            var biomedicalResult = new Dictionary<string, ConceptVersionView>(StringComparer.Ordinal);
            foreach (var header in headers)
            {
                if (header.Origin == ConceptOrigin.CdiscBiomedicalConcept
                    && header.ExternalCode is { } extCode
                    && bcCodes.Contains(extCode)
                    && revisionsByKey.TryGetValue((header.Id, header.Version), out var revision))
                {
                    var state = mappingStates.GetValueOrDefault(header.Id, ConceptMappingState.Unmapped);
                    biomedicalResult[extCode] = ToConceptVersionView(header, revision, state);
                }
            }

            var currentResult = new Dictionary<ConceptId, ConceptVersionView>();
            foreach (var conceptId in currentIds)
            {
                if (headersById.TryGetValue(conceptId, out var header)
                    && revisionsByKey.TryGetValue((header.Id, header.Version), out var revision))
                {
                    var state = mappingStates.GetValueOrDefault(header.Id, ConceptMappingState.Unmapped);
                    currentResult[conceptId] = ToConceptVersionView(header, revision, state);
                }
            }

            var pinnedResult = new Dictionary<ConceptRef, ConceptVersionView>();
            foreach (var conceptRef in pinnedRefs)
            {
                if (headersById.TryGetValue(conceptRef.ConceptId, out var header)
                    && revisionsByKey.TryGetValue((conceptRef.ConceptId, conceptRef.Version), out var revision))
                {
                    var state = mappingStates.GetValueOrDefault(header.Id, ConceptMappingState.Unmapped);
                    pinnedResult[conceptRef] = ToConceptVersionView(header, revision, state);
                }
            }

            return new ConceptBatchLookupResult(biomedicalResult, currentResult, pinnedResult);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyDictionary<ConceptRef, ConceptVersionView>> ResolveManyAsync(
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

    public async Task<IReadOnlyDictionary<ConceptId, ConceptVersionView>> GetCurrentManyAsync(
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

    public async Task<IReadOnlyDictionary<string, ConceptVersionView>> FindBiomedicalConceptsAsync(
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

    public async Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeSystemUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        await using var db = contexts.Create(tenant);
        var trimmed = version.Trim();
        return await (
                from v in db.CodeSystemVersions.AsNoTracking()
                join s in db.CodeSystems.AsNoTracking() on v.CodeSystemId equals s.Id
                where s.Uri == codeSystemUri && v.Version == trimmed
                select new CodeSystemVersionView(v.Id, s.Id, s.Uri, v.Version, v.ReleasedOn, s.License, v.PackageSha256, v.EntryCount))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var entries = await db.CodeSystemEntries.AsNoTracking()
            .Where(e => e.CodeSystemVersionId == coding.CodeSystemVersionId && e.Code == coding.Code)
            .OrderBy(e => e.Ordinal)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. entries.Select(ToView)];
    }

    public async Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupCode);
        await using var db = contexts.Create(tenant);
        var entries = await db.CodeSystemEntries.AsNoTracking()
            .Where(e => e.CodeSystemVersionId == version && e.GroupCode == groupCode)
            .OrderBy(e => e.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. entries.Select(ToView)];
    }

    public async Task<IReadOnlyList<ConceptMappingView>> ResolveCodingAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var mappings = await db.ConceptMappings.AsNoTracking()
            .Where(m => m.CodeSystemVersionId == coding.CodeSystemVersionId && m.Code == coding.Code && m.Status == MappingStatus.Confirmed)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. mappings.Select(ToView)];
    }

    public async Task<IReadOnlyList<ConceptMappingView>> GetMappingsAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var mappings = await db.ConceptMappings.AsNoTracking()
            .Where(m => m.ConceptId == concept)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. mappings.Select(ToView)];
    }

    private static ConceptVersionView ToConceptVersionView(Concept header, ConceptRevision revision, ConceptMappingState mappingState) =>
        new(
            new ConceptRef(header.Id, revision.Version),
            header.Origin,
            header.ExternalCode,
            header.Kind,
            revision.PreferredLabel,
            revision.Definition,
            header.Status,
            header.Version,
            mappingState,
            revision.RecordedAt);

    private static CodeSystemEntryView ToView(CodeSystemEntry e) =>
        new(e.CodeSystemVersionId, e.Code, e.Display, e.GroupCode, e.SubmissionValue, e.Definition, e.Synonyms);

    private static ConceptMappingView ToView(ConceptMapping m) =>
        new(m.Id, m.Concept, m.Target, m.Relation, m.Status, m.Evidence);
}
