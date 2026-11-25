using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Ctms.Infrastructure.Persistence;

public sealed class CtmsUnitOfWork(CtmsDbContext db)
{
    public Task<int> CommitAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
