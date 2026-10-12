using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cadence.Apps.Etmf.Workers;

public sealed class EtmfExportQueue
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait
    });

    public async ValueTask EnqueueAsync(string jobId, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(jobId, ct);
    }

    public ValueTask<string> DequeueAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAsync(ct);
    }

    public bool TryDequeue(out string jobId)
    {
        return _channel.Reader.TryRead(out jobId!);
    }
}
