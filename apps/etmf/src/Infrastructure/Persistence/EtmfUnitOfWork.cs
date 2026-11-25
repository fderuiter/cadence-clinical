using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Etmf.Infrastructure.Persistence;

/// <summary>
/// Unit-of-work seam consolidating persistence commits for the eTMF service.
/// </summary>
public sealed class EtmfUnitOfWork(EtmfDbContext dbContext)
{
    internal EtmfDbContext Context => dbContext;

    public Task<int> CommitAsync(CancellationToken ct = default) =>
        dbContext.SaveChangesAsync(ct);
}
