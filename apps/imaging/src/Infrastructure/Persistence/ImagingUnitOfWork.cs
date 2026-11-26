using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Application;

namespace Cadence.Apps.Imaging.Infrastructure.Persistence;

public sealed class ImagingUnitOfWork(ImagingDbContext dbContext) : IImagingUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
