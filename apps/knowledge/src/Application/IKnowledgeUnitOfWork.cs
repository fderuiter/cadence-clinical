using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Knowledge.Application;

public interface IKnowledgeUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
