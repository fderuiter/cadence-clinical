using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;

namespace Cadence.Apps.Fileshare.Workers;

public interface IDocumentConverterProcessor
{
    Task<string> ConvertToPdfAsync(FileRecord fileRecord, CancellationToken ct);
}
