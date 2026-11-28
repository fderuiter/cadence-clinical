using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Evidence;

internal static class ExportProtocolHistoryReader
{
    internal static async Task<IReadOnlyList<ExportProtocolSnapshot>> ReadProtocolsAsync(
        KernelDbContext db,
        StudyId study,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        IReadOnlySet<ProtocolVersionId> referencedVersionIds,
        CancellationToken cancellationToken)
    {
        var knownAtUtc = knownAt.UtcDateTime;
        var versions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => v.StudyId == study && v.CreatedAt <= knownAtUtc)
            .OrderBy(v => v.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (versions.Count == 0)
        {
            return [];
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

        var effectivities = await db.Effectivities.AsNoTracking()
            .Include(e => e.Periods)
            .Where(e => e.StudyId == study)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var effectiveVersionIds = effectivities
            .Where(e => e.StatusAsOf(validAt, knownAt) == EffectivityStatus.Effective)
            .Select(e => e.ProtocolVersionId)
            .ToHashSet();

        var snapshots = await db.FormVersionSnapshots.AsNoTracking()
            .Where(s => s.StudyId == study && (s.RecordedAt != default ? s.RecordedAt <= knownAt : s.CreatedAt <= knownAtUtc))
            .OrderByDescending(s => s.RecordedAt)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var odmByVersion = snapshots
            .Where(s => !string.IsNullOrWhiteSpace(s.OdmXml))
            .GroupBy(s => s.ProtocolVersionId)
            .ToDictionary(g => g.Key, g => g.First().OdmXml);

        var reconstructed = new List<(ProtocolVersionId Id, int Number, ProtocolVersionStatus Status, string Content, string? ContentHash)>(versions.Count);
        foreach (var v in versions)
        {
            var status = v.Status;
            var content = v.Content;
            var contentHash = v.ContentHash;
            var createdAfterCut = false;

            if (auditsByVersion.TryGetValue(v.Id.ToString(), out var postAudits))
            {
                foreach (var audit in postAudits)
                {
                    if (string.Equals(audit.Action, "Created", StringComparison.Ordinal))
                    {
                        createdAfterCut = true;
                        break;
                    }

                    if (!string.Equals(audit.Action, "Updated", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ApplyBeforeSnapshot(audit.Changes, ref status, ref content, ref contentHash);
                }
            }

            if (createdAfterCut)
            {
                continue;
            }

            if (status == ProtocolVersionStatus.Superseded
                && !versions.Any(other => other.Number > v.Number && effectiveVersionIds.Contains(other.Id)))
            {
                status = ProtocolVersionStatus.Approved;
            }

            reconstructed.Add((v.Id, v.Number, status, content, contentHash));
        }

        if (reconstructed.Count == 0)
        {
            return [];
        }

        var filtered = reconstructed
            .Where(r => referencedVersionIds.Contains(r.Id) || effectiveVersionIds.Contains(r.Id))
            .ToList();

        return filtered
            .Select(r =>
            {
                var compiledOdm = r.Status is ProtocolVersionStatus.Approved or ProtocolVersionStatus.Superseded
                    ? odmByVersion.GetValueOrDefault(r.Id)
                    : null;
                return new ExportProtocolSnapshot(
                    r.Id,
                    r.Number,
                    r.Status,
                    ProtocolDesignDocument.Read(r.Content).Design,
                    r.Content,
                    r.ContentHash,
                    compiledOdm);
            })
            .ToList();
    }

    private static void ApplyBeforeSnapshot(
        string changesJson,
        ref ProtocolVersionStatus status,
        ref string content,
        ref string? contentHash)
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

            if (before.TryGetProperty(nameof(ProtocolVersion.Content), out var contentProp)
                && contentProp.ValueKind == JsonValueKind.String)
            {
                content = contentProp.GetString() ?? content;
            }

            if (before.TryGetProperty(nameof(ProtocolVersion.ContentHash), out var hashProp))
            {
                contentHash = hashProp.ValueKind == JsonValueKind.String ? hashProp.GetString() : null;
            }

            if (before.TryGetProperty(nameof(ProtocolVersion.Status), out var statusProp))
            {
                if (statusProp.ValueKind == JsonValueKind.String
                    && Enum.TryParse<ProtocolVersionStatus>(statusProp.GetString(), ignoreCase: true, out var parsedStatus))
                {
                    status = parsedStatus;
                }
                else if (statusProp.ValueKind == JsonValueKind.Number
                    && statusProp.TryGetInt32(out var statusCode)
                    && Enum.IsDefined((ProtocolVersionStatus)statusCode))
                {
                    status = (ProtocolVersionStatus)statusCode;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed change snapshots.
        }
    }
}
