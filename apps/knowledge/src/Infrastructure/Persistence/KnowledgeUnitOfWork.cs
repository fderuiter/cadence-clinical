using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence;

public sealed class KnowledgeUnitOfWork(KnowledgeDbContext dbContext)
{
    public KnowledgeDbContext DbContext { get; } = dbContext;

    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        DbContext.SaveChangesAsync(cancellationToken);
}
