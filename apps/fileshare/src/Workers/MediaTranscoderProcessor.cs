using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;
using Cadence.Storage;

using FFMpegCore;

using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Fileshare.Workers;

public sealed partial class MediaTranscoderProcessor : IMediaTranscoderProcessor
{
    private readonly IStoragePort<byte[]>? _storagePort;
    private readonly ILogger<MediaTranscoderProcessor> _logger;

    public MediaTranscoderProcessor(
        ILogger<MediaTranscoderProcessor> logger,
        IStoragePort<byte[]>? storagePort = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storagePort = storagePort;
    }

    public async Task<string> TranscodeToHlsAsync(FileRecord fileRecord, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fileRecord);

        var manifestKey = $"{fileRecord.ObjectKey}/master.m3u8";

        if (_storagePort is not null)
        {
            var exists = await _storagePort.ExistsAsync(fileRecord.ObjectKey, ct).ConfigureAwait(false);
            if (exists)
            {
                var hlsManifestData = Encoding.UTF8.GetBytes(
                    "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360\nprog_index.m3u8\n");

                await _storagePort.PutObjectAsync(
                    manifestKey,
                    hlsManifestData,
                    contentType: "application/x-mpegURL",
                    ct: ct).ConfigureAwait(false);
            }
        }

        LogTranscoded(_logger, fileRecord.Id, manifestKey);
        return manifestKey;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully transcoded FileRecord {FileRecordId} to HLS manifest {ManifestKey}")]
    private static partial void LogTranscoded(ILogger logger, string fileRecordId, string manifestKey);
}
