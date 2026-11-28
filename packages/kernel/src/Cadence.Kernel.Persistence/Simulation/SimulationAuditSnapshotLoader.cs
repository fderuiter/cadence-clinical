using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Simulation;

internal static class SimulationAuditSnapshotLoader
{
    internal static async Task<IReadOnlyList<SimulationSiteSnapshot>> LoadSitesAsync(
        KernelDbContext db,
        StudyId studyId,
        DateTimeOffset knownAt,
        DateTime knownAtUtc,
        CancellationToken cancellationToken)
    {
        var studySites = await db.StudySites.AsNoTracking()
            .Where(ss => ss.StudyId == studyId && ss.CreatedAt <= knownAtUtc)
            .OrderBy(ss => ss.CreatedAt)
            .ThenBy(ss => ss.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (studySites.Count == 0)
        {
            return [];
        }

        var siteKeys = studySites.Select(ss => ss.Id.ToString()).ToList();
        var postCutSiteAudits = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(StudySite)
                && a.AggregateId != null
                && siteKeys.Contains(a.AggregateId)
                && a.RecordedAt > knownAt)
            .OrderByDescending(a => a.Sequence)
            .Select(a => new { a.AggregateId, a.Action, a.Changes })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var auditsBySite = postCutSiteAudits
            .GroupBy(a => a.AggregateId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var siteIds = studySites.Select(ss => ss.SiteId).Distinct().ToList();
        var sitesById = await db.Sites.AsNoTracking()
            .Where(s => siteIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken)
            .ConfigureAwait(false);

        var snapshots = new List<SimulationSiteSnapshot>(studySites.Count);
        foreach (var ss in studySites)
        {
            var status = ss.Status;
            var isActive = ss.IsActive;
            var createdAfterCut = false;

            if (auditsBySite.TryGetValue(ss.Id.ToString(), out var postAudits))
            {
                foreach (var audit in postAudits)
                {
                    if (string.Equals(audit.Action, "Created", StringComparison.Ordinal))
                    {
                        createdAfterCut = true;
                        break;
                    }

                    if (string.Equals(audit.Action, "Updated", StringComparison.Ordinal))
                    {
                        ApplyStudySiteBeforeSnapshot(audit.Changes, ref status, ref isActive);
                    }
                }
            }

            if (createdAfterCut)
            {
                continue;
            }

            sitesById.TryGetValue(ss.SiteId, out var site);
            var createdAt = new DateTimeOffset(DateTime.SpecifyKind(ss.CreatedAt, DateTimeKind.Utc));
            snapshots.Add(new SimulationSiteSnapshot(
                ss.Id,
                ss.SiteId,
                site?.Country ?? string.Empty,
                status,
                isActive && (site?.IsActive ?? true),
                createdAt));
        }

        return snapshots;
    }

    internal static async Task<IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign>> LoadProtocolDesignsAsync(
        KernelDbContext db,
        StudyId studyId,
        DateTimeOffset knownAt,
        DateTime knownAtUtc,
        CancellationToken cancellationToken)
    {
        var versions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.StudyId == studyId && v.CreatedAt <= knownAtUtc)
            .OrderBy(v => v.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (versions.Count == 0)
        {
            return new Dictionary<ProtocolVersionId, ProtocolDesign>();
        }

        var versionKeys = versions.Select(v => v.Id.ToString()).ToList();
        var postCutAudits = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(ProtocolVersion)
                && a.AggregateId != null
                && versionKeys.Contains(a.AggregateId)
                && a.RecordedAt > knownAt)
            .OrderByDescending(a => a.Sequence)
            .Select(a => new { a.AggregateId, a.Action, a.Changes })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var auditsByVersion = postCutAudits
            .GroupBy(a => a.AggregateId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var designs = new Dictionary<ProtocolVersionId, ProtocolDesign>();
        foreach (var version in versions)
        {
            var content = version.Content;
            var createdAfterCut = false;
            if (auditsByVersion.TryGetValue(version.Id.ToString(), out var audits))
            {
                foreach (var audit in audits)
                {
                    if (string.Equals(audit.Action, "Created", StringComparison.Ordinal))
                    {
                        createdAfterCut = true;
                        break;
                    }

                    if (string.Equals(audit.Action, "Updated", StringComparison.Ordinal))
                    {
                        ApplyProtocolContentBeforeSnapshot(audit.Changes, ref content);
                    }
                }
            }

            if (createdAfterCut)
            {
                continue;
            }

            if (ProtocolDesignDocument.Read(content).Design is { } design)
            {
                designs[version.Id] = design;
            }
        }

        return designs;
    }

    internal static async Task<IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>> LoadConceptsAsync(
        KernelDbContext db,
        IReadOnlyList<ConceptId> conceptIds,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (conceptIds.Count == 0)
        {
            return new Dictionary<ConceptId, BlindedConceptMetadata>();
        }

        var concepts = await db.Concepts.AsNoTracking()
            .Include(c => c.Revisions)
            .Where(c => conceptIds.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<ConceptId, BlindedConceptMetadata>(concepts.Count);
        foreach (var concept in concepts)
        {
            var revAtCut = concept.Revisions
                .Where(r => r.RecordedAt <= knownAt)
                .OrderBy(r => r.Version)
                .LastOrDefault()
                ?? concept.Revisions.OrderBy(r => r.Version).LastOrDefault();

            result[concept.Id] = new BlindedConceptMetadata(
                concept.ExternalCode,
                revAtCut?.PreferredLabel ?? concept.PreferredLabel,
                revAtCut?.Definition ?? concept.Definition);
        }

        return result;
    }

    internal static async Task<(
        IReadOnlyList<LibraryItemView> PinnedLibraryItems,
        IReadOnlyList<LibraryItemView> PinnedRulePacks,
        IReadOnlyList<StandardVersionId> DeclaredStandardVersions,
        IReadOnlyList<(string Standard, string Version)> DeclaredStandards,
        IReadOnlyDictionary<(string Codelist, string Code), string> CodelistDecodes)> LoadStandardsSnapshotAsync(
        KernelDbContext db,
        StudyId studyId,
        DateTimeOffset knownAt,
        DateTime knownAtUtc,
        CancellationToken cancellationToken)
    {
        var profile = await db.Set<StudyStandardsProfile>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.StudyId == studyId && p.CreatedAt <= knownAtUtc, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return ([], [], [], [], new Dictionary<(string Codelist, string Code), string>());
        }

        var libraryPins = profile.LibraryPins;
        var standardVersionIds = profile.StandardVersionIds;
        var terminologyVersions = profile.ControlledTerminologyVersions;
        var profileKey = profile.Id.ToString();
        var postCutProfileAudits = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateType == nameof(StudyStandardsProfile)
                && a.AggregateId == profileKey
                && a.RecordedAt > knownAt)
            .OrderByDescending(a => a.Sequence)
            .Select(a => new { a.Action, a.Changes })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var audit in postCutProfileAudits)
        {
            if (string.Equals(audit.Action, "Created", StringComparison.Ordinal))
            {
                return ([], [], [], [], new Dictionary<(string Codelist, string Code), string>());
            }

            if (string.Equals(audit.Action, "Updated", StringComparison.Ordinal))
            {
                ApplyStandardsProfileBeforeSnapshot(audit.Changes, ref libraryPins, ref standardVersionIds, ref terminologyVersions);
            }
        }

        var pinIds = libraryPins.Distinct().ToArray();
        var pinnedItemsRaw = pinIds.Length == 0
            ? []
            : await db.Set<LibraryItem>().AsNoTracking()
                .Where(i => pinIds.Contains(i.Id) && i.CreatedAt <= knownAtUtc)
                .OrderBy(i => i.Key)
                .ThenBy(i => i.ItemVersion)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var pinnedViews = pinnedItemsRaw.Select(ToLibraryItemView).ToList();

        // Include any base MappingSpecification referenced via 'overrides' that was not directly listed in LibraryPins.
        var overriddenKeys = pinnedViews
            .Where(v => v.Kind == LibraryItemKind.MappingSpecification)
            .Select(v => MappingSpecificationJson.Read(v.Content).Specification?.Overrides)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (overriddenKeys.Length > 0)
        {
            var existingKeys = pinnedViews
                .Where(v => v.Kind == LibraryItemKind.MappingSpecification)
                .Select(v => v.Key)
                .ToHashSet(StringComparer.Ordinal);
            var missingKeys = overriddenKeys.Where(k => !existingKeys.Contains(k)).ToArray();
            if (missingKeys.Length > 0)
            {
                var baseCandidates = await db.Set<LibraryItem>().AsNoTracking()
                    .Where(i => i.Kind == LibraryItemKind.MappingSpecification
                        && missingKeys.Contains(i.Key)
                        && i.Status == GovernanceStatus.Approved
                        && i.CreatedAt <= knownAtUtc)
                    .OrderBy(i => i.Key)
                    .ThenByDescending(i => i.ItemVersion)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var group in baseCandidates.GroupBy(i => i.Key, StringComparer.Ordinal))
                {
                    pinnedViews.Add(ToLibraryItemView(group.First()));
                }
            }
        }

        var pinnedLibraryItems = pinnedViews
            .Where(v => v.Kind == LibraryItemKind.MappingSpecification)
            .OrderBy(v => v.Key, StringComparer.Ordinal)
            .ThenBy(v => v.Version)
            .ToList();

        var pinnedRulePacks = pinnedViews
            .Where(v => v.Kind == LibraryItemKind.RulePack)
            .OrderBy(v => v.Key, StringComparer.Ordinal)
            .ThenBy(v => v.Version)
            .ToList();

        var codelistDecodes = new Dictionary<(string Codelist, string Code), string>();
        var terminologyVersionIds = terminologyVersions
            .Distinct()
            .ToArray();

        if (terminologyVersionIds.Length > 0)
        {
            var entries = await db.CodeSystemEntries.AsNoTracking()
                .Where(e => terminologyVersionIds.Contains(e.CodeSystemVersionId) && e.GroupCode != null)
                .OrderBy(e => e.Ordinal)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.GroupCode))
                {
                    var decoded = !string.IsNullOrWhiteSpace(entry.SubmissionValue) ? entry.SubmissionValue! : entry.Display;
                    codelistDecodes[(entry.GroupCode!, entry.Code)] = decoded;
                }
            }
        }

        var distinctStdIds = standardVersionIds.Distinct().ToArray();
        var declaredStandardMetadata = distinctStdIds.Length == 0
            ? []
            : await db.Set<StandardVersion>().AsNoTracking()
                .Where(s => distinctStdIds.Contains(s.Id) && s.CreatedAt <= knownAtUtc)
                .OrderBy(s => s.StandardCode)
                .ThenBy(s => s.Version)
                .Select(s => new ValueTuple<string, string>(s.StandardCode, s.Version))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        return (pinnedLibraryItems, pinnedRulePacks, standardVersionIds, declaredStandardMetadata, codelistDecodes);
    }

    internal static LibraryItemView ToLibraryItemView(LibraryItem item)
    {
        using var content = JsonDocument.Parse(item.Content);
        return new LibraryItemView(
            item.Id,
            item.Kind,
            item.Key,
            item.ItemVersion,
            item.Title,
            content.RootElement.Clone(),
            item.ContentHash,
            item.Status,
            item.StandardVersionIds,
            item.ApprovalAttestationId,
            item.DeprecationAttestationId)
        {
            VersionIndex = item.VersionIndex,
        };
    }

    private static void ApplyStudySiteBeforeSnapshot(string changesJson, ref string status, ref bool isActive)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(changesJson);
            if (!doc.RootElement.TryGetProperty("before", out var before) || before.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (before.TryGetProperty(nameof(StudySite.Status), out var statusProp)
                && statusProp.ValueKind == JsonValueKind.String
                && statusProp.GetString() is { } beforeStatus)
            {
                status = beforeStatus;
            }

            if (before.TryGetProperty(nameof(StudySite.IsActive), out var activeProp)
                && activeProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                isActive = activeProp.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // Ignore malformed change snapshots.
        }
    }

    private static void ApplyProtocolContentBeforeSnapshot(string changesJson, ref string content)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(changesJson);
            if (doc.RootElement.TryGetProperty("before", out var before)
                && before.ValueKind == JsonValueKind.Object
                && before.TryGetProperty(nameof(ProtocolVersion.Content), out var contentProp)
                && contentProp.ValueKind == JsonValueKind.String
                && contentProp.GetString() is { } beforeContent)
            {
                content = beforeContent;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed change snapshots.
        }
    }

    private static void ApplyStandardsProfileBeforeSnapshot(
        string changesJson,
        ref LibraryItemId[] libraryPins,
        ref StandardVersionId[] standardVersionIds,
        ref CodeSystemVersionId[] terminologyVersions)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(changesJson);
            if (!doc.RootElement.TryGetProperty("before", out var before) || before.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (TryReadGuidArray(before, nameof(StudyStandardsProfile.LibraryPins), out var pinGuids))
            {
                libraryPins = [.. pinGuids.Select(LibraryItemId.FromGuid)];
            }

            if (TryReadGuidArray(before, nameof(StudyStandardsProfile.StandardVersionIds), out var stdGuids))
            {
                standardVersionIds = [.. stdGuids.Select(StandardVersionId.FromGuid)];
            }

            if (TryReadGuidArray(before, nameof(StudyStandardsProfile.ControlledTerminologyVersions), out var termGuids))
            {
                terminologyVersions = [.. termGuids.Select(CodeSystemVersionId.FromGuid)];
            }
        }
        catch (JsonException)
        {
            // Ignore malformed change snapshots.
        }
    }

    private static bool TryReadGuidArray(JsonElement obj, string propertyName, out List<Guid> guids)
    {
        guids = [];
        if (!obj.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var direct))
            {
                guids.Add(direct);
            }
            else if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("Value", out var valProp)
                && valProp.ValueKind == JsonValueKind.String
                && Guid.TryParse(valProp.GetString(), out var nested))
            {
                guids.Add(nested);
            }
        }

        return true;
    }
}
