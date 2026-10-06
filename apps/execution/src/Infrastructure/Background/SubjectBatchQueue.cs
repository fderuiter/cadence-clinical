using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Infrastructure.Background;

public interface ISubjectBatchQueue
{
    ValueTask EnqueueAsync(Guid batchId, CancellationToken ct = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct = default);
}

public sealed class SubjectBatchQueue : ISubjectBatchQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(Guid batchId, CancellationToken ct = default)
    {
        return _channel.Writer.WriteAsync(batchId, ct);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}
