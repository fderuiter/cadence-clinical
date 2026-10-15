using System.Text.Json;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Database;
using Cadence.Security.Blinding;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// EF-backed <see cref="IAuditLogWriter"/>. It writes no audit rows itself: it attaches the semantic action to
/// the context's next save through <see cref="AuditActions"/>, and the shared <see cref="GxPAuditInterceptor"/>
/// (the only audit writer) records it on the changed entity's own entry, or as an action-only entry when the
/// save changes nothing. One change therefore yields one entry on one chain keyed by the aggregate's CLR type
/// name (#4768).
/// @req:PRD-SYS-053
/// </summary>
public sealed class EfAuditLogWriter(ExecutionDbContext dbContext) : IAuditLogWriter
{
    public Task WriteAsync(
        string entityName,
        string recordId,
        string action,
        string userId,
        string changeReason,
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues,
        CancellationToken ct)
    {
        AuditActions.Record(dbContext, new AuditAction(
            entityName, recordId, action, userId, changeReason, BuildChangesJson(oldValues, newValues)));
        return Task.CompletedTask;
    }

    private static string? BuildChangesJson(
        IReadOnlyDictionary<string, object?>? oldValues, IReadOnlyDictionary<string, object?>? newValues)
    {
        if (oldValues is null && newValues is null)
        {
            return null;
        }

        var maskedOld = oldValues is not null ? FieldMasker.MaskPayload(oldValues, null) : null;
        var maskedNew = newValues is not null ? FieldMasker.MaskPayload(newValues, null) : null;

        return JsonSerializer.Serialize(new { old = maskedOld, @new = maskedNew });
    }
}
