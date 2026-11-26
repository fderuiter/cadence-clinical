using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Ctms.Application;

public interface ICtmsUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);

    Task CommitExternalContextAsync(object? externalContext, CancellationToken cancellationToken = default);
}
