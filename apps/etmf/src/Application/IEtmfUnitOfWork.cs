using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Etmf.Application;

public interface IEtmfUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
