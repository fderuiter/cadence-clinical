using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Tickets.Application;

namespace Cadence.Apps.Tickets.Infrastructure.Persistence;

public sealed class TicketsUnitOfWork(TicketsDbContext dbContext) : ITicketsUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
