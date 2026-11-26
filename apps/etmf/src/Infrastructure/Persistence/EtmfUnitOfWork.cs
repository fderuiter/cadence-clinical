using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;

namespace Cadence.Apps.Etmf.Infrastructure.Persistence;

public sealed class EtmfUnitOfWork(EtmfDbContext dbContext) : IEtmfUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
