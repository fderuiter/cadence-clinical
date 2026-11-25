using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Econsent.Infrastructure.Persistence;

public sealed class EconsentUnitOfWork(EconsentDbContext dbContext)
{
    public EconsentDbContext DbContext { get; } = dbContext;

    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        DbContext.SaveChangesAsync(cancellationToken);
}
