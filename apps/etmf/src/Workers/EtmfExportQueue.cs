using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cadence.Apps.Etmf.Workers;

public sealed class EtmfExportQueue
{
    private readonly Channel<(string JobId, string? TenantCode)> _channel =
        Channel.CreateBounded<(string JobId, string? TenantCode)>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });

    public ValueTask EnqueueAsync(string jobId, CancellationToken ct = default) =>
        EnqueueAsync(jobId, tenantCode: null, ct);

    public async ValueTask EnqueueAsync(string jobId, string? tenantCode, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync((jobId, tenantCode), ct);
    }

    public async ValueTask<string> DequeueAsync(CancellationToken ct = default)
    {
        var item = await _channel.Reader.ReadAsync(ct);
        return item.JobId;
    }

    public ValueTask<(string JobId, string? TenantCode)> DequeueWithTenantAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAsync(ct);

    public bool TryDequeue(out string jobId) =>
        TryDequeue(out jobId, out _);

    public bool TryDequeue(out string jobId, out string? tenantCode)
    {
        if (_channel.Reader.TryRead(out var item))
        {
            jobId = item.JobId;
            tenantCode = item.TenantCode;
            return true;
        }

        jobId = default!;
        tenantCode = null;
        return false;
    }
}
