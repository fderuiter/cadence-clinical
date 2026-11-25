using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Interop.Infrastructure.Persistence;

public sealed class InteropUnitOfWork(InteropDbContext db)
{
    public InteropDbContext Db => db;

    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
