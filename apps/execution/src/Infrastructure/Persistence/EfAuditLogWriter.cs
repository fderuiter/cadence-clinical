using System.Text.Json;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Core.Domain.Audit;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// EF-backed <see cref="IAuditLogWriter"/> (MIG-SUBJ-017, `02_technical_design.md` §4.4). Appends the
/// resulting <see cref="AuditLogEntry"/> straight to the injected <see cref="ExecutionDbContext"/>'s
/// <see cref="ExecutionDbContext.AuditLogEntries"/> — never a second, separate `DbContext` — so the row
/// commits inside the identical `SaveChangesAsync` call as whatever entity mutation prompted it (§4.4's
/// "commits inside the same transaction" requirement).
///
/// <para>
/// The Merkle chain is scoped per <c>tableName</c> (<see cref="AuditLogEntry.EntityName"/>), computed
/// exactly the way `GxPAuditInterceptor` computes its own per-`EntityName` chain: the predecessor is the
/// most recently persisted <see cref="AuditLogEntry"/> for that same `EntityName`, and
/// <see cref="AuditLogEntry.VersionIndex"/> increments by one from that predecessor's (starting at 1 for
/// the first entry in the chain). The two chains never collide: `GxPAuditInterceptor` keys its chain on
/// the mutated CLR aggregate's type name (e.g. `"ClinicalSubject"`), while this writer keys on the
/// caller-supplied table name (e.g. `"clinical_subjects"`) — distinct key spaces for the interceptor's
/// automatic field-stamping and this writer's application-initiated, semantically-named rows, the two
/// operating alongside one another rather than one replacing the other.
/// </para>
///
/// <para>
/// This lookup queries only already-persisted rows (matching the interceptor's own single-query-per-call
/// pattern); it does not additionally scan the `ChangeTracker` for not-yet-saved <see cref="AuditLogEntry"/>
/// instances added earlier in the same, still-open unit of work. Every documented caller
/// (`02_technical_design.md` §3.2/§5.2) issues exactly one `WriteAsync` call immediately followed by its
/// own `SaveChangesAsync` before the next logical operation runs, so this is not a gap in practice — it is
/// called out here so a future caller that batches multiple `WriteAsync` calls for the same
/// <c>tableName</c> ahead of a single `SaveChangesAsync` knows to extend this lookup accordingly.
/// </para>
/// @req:PRD-SYS-053
/// </summary>
public sealed class EfAuditLogWriter(ExecutionDbContext dbContext, IClock clock) : IAuditLogWriter
{
    public async Task WriteAsync(
        string tableName,
        string recordId,
        string action,
        string userId,
        string changeReason,
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues,
        CancellationToken ct)
    {
        var predecessor = await dbContext.AuditLogEntries
            .Where(e => e.EntityName == tableName)
            .Where(e => !dbContext.AuditLogEntries.Any(sub => sub.EntityName == tableName && sub.PredecessorHash == e.BlockHash))
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.VersionIndex)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.BlockHash, e.VersionIndex })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        string predecessorHash = predecessor?.BlockHash ?? string.Empty;
        long versionIndex = (predecessor?.VersionIndex ?? 0) + 1;
        DateTime timestamp = clock.UtcNow.UtcDateTime;
        string? changesJson = BuildChangesJson(oldValues, newValues);

        string blockHash = AuditLogEntry.ComputeBlockHash(
            tableName, recordId, action, timestamp, userId, versionIndex, changesJson, predecessorHash);

        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            EntityName = tableName,
            EntityId = recordId,
            Action = action,
            Timestamp = timestamp,
            UserId = userId,
            ReasonForChange = changeReason,
            VersionIndex = versionIndex,
            ChangesJson = changesJson,
            PredecessorHash = predecessorHash,
            BlockHash = blockHash,
        });
    }

    private static string? BuildChangesJson(
        IReadOnlyDictionary<string, object?>? oldValues, IReadOnlyDictionary<string, object?>? newValues) =>
        oldValues is null && newValues is null
            ? null
            : JsonSerializer.Serialize(new { old = oldValues, @new = newValues });
}
