using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Hexagonal;

public interface IRepositoryPort<T>
{
    Task<T?> GetByIdAsync(string entityId, CancellationToken ct = default);
    Task<T> SaveAsync(T entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(string entityId, CancellationToken ct = default);
}

public interface IUseCasePort<TRequest, TResponse>
{
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken ct = default);
}

public interface IExternalServiceClientPort
{
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}

public interface IAuditLoggerPort
{
    Task LogActionAsync(
        string userId,
        string action,
        string resourceType,
        string resourceId,
        string reasonForChange,
        IReadOnlyDictionary<string, object?>? details = null,
        CancellationToken ct = default);
}

public interface IEventDispatcherPort
{
    Task DispatchAsync(DomainEvent @event, CancellationToken ct = default);
}
