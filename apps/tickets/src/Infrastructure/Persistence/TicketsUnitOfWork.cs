using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Tickets.Infrastructure.Persistence;

/// <summary>
/// Unit-of-work seam consolidating persistence commits for the Tickets service.
/// </summary>
public sealed class TicketsUnitOfWork(TicketsDbContext dbContext)
{
    internal TicketsDbContext Context => dbContext;

    public Task<int> CommitAsync(CancellationToken ct = default) =>
        dbContext.SaveChangesAsync(ct);
}
