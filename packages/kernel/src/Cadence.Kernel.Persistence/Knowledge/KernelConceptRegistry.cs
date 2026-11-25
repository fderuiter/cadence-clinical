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
/// to that tenant's terminology (K-ID-2). Read-only and untracked.
/// </summary>
internal sealed class KernelConceptRegistry(KernelTenantDbContextFactory contexts) : IConceptRegistry
{
    public async Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var header = await db.Concepts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == concept.ConceptId, cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        var revision = await db.ConceptRevisions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ConceptId == concept.ConceptId && r.Version == concept.Version, cancellationToken)
            .ConfigureAwait(false);
        return revision is null ? null : await ToViewAsync(db, header, revision, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<ConceptRef, ConceptVersionView>> ResolveManyAsync(
        TenantId tenant,
        IReadOnlyCollection<ConceptRef> concepts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(concepts);
        var distinctRefs = concepts.Where(c => !c.ConceptId.IsEmpty).Distinct().ToList();
        if (distinctRefs.Count == 0)
        {
            return new Dictionary<ConceptRef, ConceptVersionView>();
        }

        await using var db = contexts.Create(tenant);
        var conceptIds = distinctRefs.Select(c => c.ConceptId).Distinct().ToList();
        var headers = await db.Concepts.AsNoTracking()
            .Where(c => conceptIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);
        if (headers.Count == 0)
        {
            return new Dictionary<ConceptRef, ConceptVersionView>();
        }

        var targetRefs = distinctRefs.Where(r => headers.ContainsKey(r.ConceptId)).ToList();
        return await BuildViewsAsync(db, headers, targetRefs, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var header = await db.Concepts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == concept, cancellationToken).ConfigureAwait(false);
        return header is null ? null : await CurrentViewAsync(db, header, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<ConceptId, ConceptVersionView>> GetCurrentManyAsync(
        TenantId tenant,
        IReadOnlyCollection<ConceptId> concepts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(concepts);
        var distinctIds = concepts.Where(id => !id.IsEmpty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<ConceptId, ConceptVersionView>();
        }

        await using var db = contexts.Create(tenant);
        var headers = await db.Concepts.AsNoTracking()
            .Where(c => distinctIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);
        if (headers.Count == 0)
        {
            return new Dictionary<ConceptId, ConceptVersionView>();
        }

        var targetRefs = headers.Values.Select(h => new ConceptRef(h.Id, h.Version)).ToList();
        var byRef = await BuildViewsAsync(db, headers, targetRefs, cancellationToken).ConfigureAwait(false);
        var byId = new Dictionary<ConceptId, ConceptVersionView>(byRef.Count);
        foreach (var (conceptRef, view) in byRef)
        {
            byId[conceptRef.ConceptId] = view;
        }

        return byId;
    }

    public async Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cdiscConceptCode);
        await using var db = contexts.Create(tenant);
        var code = cdiscConceptCode.Trim();
        var header = await db.Concepts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ExternalCode == code && c.Origin == ConceptOrigin.CdiscBiomedicalConcept, cancellationToken)
            .ConfigureAwait(false);
        return header is null ? null : await CurrentViewAsync(db, header, cancellationToken).ConfigureAwait(false);
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

    private static async Task<IReadOnlyDictionary<ConceptRef, ConceptVersionView>> BuildViewsAsync(
        KernelDbContext db,
        IReadOnlyDictionary<ConceptId, Concept> headers,
        IReadOnlyList<ConceptRef> targetRefs,
        CancellationToken cancellationToken)
    {
        if (targetRefs.Count == 0)
        {
            return new Dictionary<ConceptRef, ConceptVersionView>();
        }

        var conceptIds = targetRefs.Select(r => r.ConceptId).Distinct().ToList();
        var versions = targetRefs.Select(r => r.Version).Distinct().ToList();
        var revisions = await db.ConceptRevisions.AsNoTracking()
            .Where(r => conceptIds.Contains(r.ConceptId) && versions.Contains(r.Version))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (revisions.Count == 0)
        {
            return new Dictionary<ConceptRef, ConceptVersionView>();
        }

        var revisionByRef = new Dictionary<ConceptRef, ConceptRevision>(revisions.Count);
        foreach (var revision in revisions)
        {
            revisionByRef[new ConceptRef(revision.ConceptId, revision.Version)] = revision;
        }

        var matchedRefs = targetRefs.Where(revisionByRef.ContainsKey).ToList();
        if (matchedRefs.Count == 0)
        {
            return new Dictionary<ConceptRef, ConceptVersionView>();
        }

        var matchedConceptIds = matchedRefs.Select(r => r.ConceptId).Distinct().ToList();
        var mappingRows = await db.ConceptMappings.AsNoTracking()
            .Where(m => matchedConceptIds.Contains(m.ConceptId) && m.Status != MappingStatus.Rejected)
            .Select(m => new { m.ConceptId, m.Status })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mappingStates = new Dictionary<ConceptId, ConceptMappingState>(matchedConceptIds.Count);
        foreach (var row in mappingRows)
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

        var views = new Dictionary<ConceptRef, ConceptVersionView>(matchedRefs.Count);
        foreach (var conceptRef in matchedRefs)
        {
            var header = headers[conceptRef.ConceptId];
            var revision = revisionByRef[conceptRef];
            var state = mappingStates.GetValueOrDefault(header.Id, ConceptMappingState.Unmapped);
            views[conceptRef] = new ConceptVersionView(
                new ConceptRef(header.Id, revision.Version),
                header.Origin,
                header.ExternalCode,
                header.Kind,
                revision.PreferredLabel,
                revision.Definition,
                header.Status,
                header.Version,
                state,
                revision.RecordedAt);
        }

        return views;
    }

    private static async Task<ConceptVersionView?> CurrentViewAsync(KernelDbContext db, Concept header, CancellationToken cancellationToken)
    {
        var revision = await db.ConceptRevisions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ConceptId == header.Id && r.Version == header.Version, cancellationToken)
            .ConfigureAwait(false);
        return revision is null ? null : await ToViewAsync(db, header, revision, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ConceptVersionView> ToViewAsync(KernelDbContext db, Concept header, ConceptRevision revision, CancellationToken cancellationToken)
    {
        var statuses = await db.ConceptMappings.AsNoTracking()
            .Where(m => m.ConceptId == header.Id && m.Status != MappingStatus.Rejected)
            .Select(m => m.Status)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var state = ConceptMappingState.Unmapped;
        if (statuses.Contains(MappingStatus.Confirmed))
        {
            state = ConceptMappingState.Mapped;
        }
        else if (statuses.Count > 0)
        {
            state = ConceptMappingState.Proposed;
        }

        return new ConceptVersionView(
            new ConceptRef(header.Id, revision.Version),
            header.Origin,
            header.ExternalCode,
            header.Kind,
            revision.PreferredLabel,
            revision.Definition,
            header.Status,
            header.Version,
            state,
            revision.RecordedAt);
    }

    private static CodeSystemEntryView ToView(CodeSystemEntry e) =>
        new(e.CodeSystemVersionId, e.Code, e.Display, e.GroupCode, e.SubmissionValue, e.Definition, e.Synonyms);

    private static ConceptMappingView ToView(ConceptMapping m) =>
        new(m.Id, m.Concept, m.Target, m.Relation, m.Status, m.Evidence);
}
