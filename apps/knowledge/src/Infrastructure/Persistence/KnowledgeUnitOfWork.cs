using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Application;

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence;

public sealed class KnowledgeUnitOfWork(KnowledgeDbContext dbContext) : IKnowledgeUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
