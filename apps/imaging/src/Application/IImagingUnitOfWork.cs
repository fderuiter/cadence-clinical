using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Imaging.Application;

public interface IImagingUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
