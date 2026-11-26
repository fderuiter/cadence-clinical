using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application;

namespace Cadence.Apps.Payments.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IPaymentsUnitOfWork"/> backed by <see cref="PaymentsDbContext"/>.
/// </summary>
public sealed class PaymentsUnitOfWork(PaymentsDbContext dbContext) : IPaymentsUnitOfWork
{
    public Task<int> CommitAsync(CancellationToken ct = default) =>
        dbContext.SaveChangesAsync(ct);
}
