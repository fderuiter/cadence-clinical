using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Security.Audit;

/// <summary>
/// Interface for persistent audit log record writer backends.
/// @req:PRD-SYS-053
/// </summary>
public interface IAuditLogWriter
{
    void WriteRecord(AuditLogRecord record);
    Task WriteRecordAsync(AuditLogRecord record, CancellationToken cancellationToken = default);
    AuditLogRecord? GetLastRecord();
    IReadOnlyList<AuditLogRecord> FetchAllRecords();
}

/// <summary>
/// Default thread-safe in-memory audit log store.
/// </summary>
public class InMemoryAuditStore : IAuditLogWriter
{
    private readonly List<AuditLogRecord> _chain = new();
    private readonly object _lock = new();

    public void WriteRecord(AuditLogRecord record)
    {
        lock (_lock)
        {
            _chain.Add(record);
        }
    }

    public Task WriteRecordAsync(AuditLogRecord record, CancellationToken cancellationToken = default)
    {
        WriteRecord(record);
        return Task.CompletedTask;
    }

    public AuditLogRecord? GetLastRecord()
    {
        lock (_lock)
        {
            return _chain.LastOrDefault();
        }
    }

    public IReadOnlyList<AuditLogRecord> FetchAllRecords()
    {
        lock (_lock)
        {
            return _chain.ToList();
        }
    }
}
