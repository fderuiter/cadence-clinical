using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cadence.Apps.Execution.Application.Exports;

public interface IExportJobQueue
{
    ValueTask EnqueueAsync(ExportJob job, CancellationToken ct = default);
    ValueTask<ExportJob> DequeueAsync(CancellationToken ct = default);
}

public sealed class ExportJobQueue : IExportJobQueue
{
    private readonly Channel<ExportJob> _channel = Channel.CreateBounded<ExportJob>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(ExportJob job, CancellationToken ct = default)
    {
        return _channel.Writer.WriteAsync(job, ct);
    }

    public ValueTask<ExportJob> DequeueAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAsync(ct);
    }
}
