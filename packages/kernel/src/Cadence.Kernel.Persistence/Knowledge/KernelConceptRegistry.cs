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

    public async Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
        var header = await db.Concepts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == concept, cancellationToken).ConfigureAwait(false);
        return header is null ? null : await CurrentViewAsync(db, header, cancellationToken).ConfigureAwait(false);
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
