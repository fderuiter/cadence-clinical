using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Scoped persistence seam for committing unit-of-work changes in the Payments service.
/// </summary>
public interface IPaymentsUnitOfWork
{
    Task<int> CommitAsync(CancellationToken ct = default);
}
