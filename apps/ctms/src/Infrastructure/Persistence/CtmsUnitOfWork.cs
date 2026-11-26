using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Infrastructure.Persistence;

public sealed class CtmsUnitOfWork(CtmsDbContext dbContext) : ICtmsUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        PersistContextAsync(dbContext, cancellationToken);

    public async Task CommitExternalContextAsync(object? externalContext, CancellationToken cancellationToken = default)
    {
        if (externalContext is DbContext externalDb)
        {
            await PersistContextAsync(externalDb, cancellationToken);
        }
    }

    private static Task<int> PersistContextAsync(DbContext context, CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
