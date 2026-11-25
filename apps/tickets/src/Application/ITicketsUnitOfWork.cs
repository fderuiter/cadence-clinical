using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Tickets.Application;

public interface ITicketsUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
