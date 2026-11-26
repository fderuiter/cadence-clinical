using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Application;

namespace Cadence.Apps.Econsent.Infrastructure.Persistence;

public sealed class EconsentUnitOfWork(EconsentDbContext dbContext) : IEconsentUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
