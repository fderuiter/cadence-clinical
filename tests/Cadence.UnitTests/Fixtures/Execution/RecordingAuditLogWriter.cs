using Cadence.Apps.Execution.Application.Abstractions;

namespace Cadence.UnitTests.Fixtures.Execution;

/// <summary>
/// Audit call captured by <see cref="RecordingAuditLogWriter"/>.
/// </summary>
public sealed record AuditCall(
    string EntityName,
    string RecordId,
    string Action,
    string UserId,
    string ChangeReason,
    IReadOnlyDictionary<string, object?>? OldValues = null,
    IReadOnlyDictionary<string, object?>? NewValues = null);

/// <summary>
/// Shared test double for <see cref="IAuditLogWriter"/> capturing audit calls for assertions in test cases.
/// </summary>
public sealed class RecordingAuditLogWriter : IAuditLogWriter
{
    public List<AuditCall> Calls { get; } = new();

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
        Calls.Add(new AuditCall(entityName, recordId, action, userId, changeReason, oldValues, newValues));
        return Task.CompletedTask;
    }
}
