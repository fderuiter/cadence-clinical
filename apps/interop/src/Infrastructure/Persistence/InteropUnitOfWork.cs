using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application;

namespace Cadence.Apps.Interop.Infrastructure.Persistence;

public sealed class InteropUnitOfWork(InteropDbContext dbContext) : IInteropUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
