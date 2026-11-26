using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Interop.Application;

public interface IInteropUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
