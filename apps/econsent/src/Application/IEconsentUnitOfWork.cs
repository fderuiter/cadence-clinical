using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Econsent.Application;

public interface IEconsentUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
