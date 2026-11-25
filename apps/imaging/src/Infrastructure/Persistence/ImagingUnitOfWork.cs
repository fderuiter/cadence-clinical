using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Imaging.Infrastructure.Persistence;

public sealed class ImagingUnitOfWork(ImagingDbContext dbContext)
{
    public ImagingDbContext Db => dbContext;

    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
