using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Database;

/// <summary>
/// Generic EF Core repository interface supporting soft delete filters and asynchronous persistence.
/// @req:PRD-SYS-001
/// </summary>
public interface IEfRepository<T> where T : class
{
    IQueryable<T> Query(bool includeSoftDeleted = false);

    Task<T?> GetByIdAsync<TId>(TId id, CancellationToken ct = default) where TId : notnull;

    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default);

    Task<T> AddAsync(T entity, CancellationToken ct = default);

    Task UpdateAsync(T entity, CancellationToken ct = default);

    Task DeleteAsync(T entity, CancellationToken ct = default);

    Task SoftDeleteAsync(T entity, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
