using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;

namespace Cadence.Apps.Fileshare.Workers;

public interface IMediaTranscoderProcessor
{
    Task<string> TranscodeToHlsAsync(FileRecord fileRecord, CancellationToken ct);
}
