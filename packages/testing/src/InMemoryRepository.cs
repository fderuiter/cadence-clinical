using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Testing;

/// <summary>
/// Thread-safe in-memory repository fake providing atomic simulation of entity persistence for unit testing.
/// @req:PRD-SYS-053
/// </summary>
public class InMemoryRepository<TEntity, TId> where TEntity : AggregateRoot<TId> where TId : notnull
{
    private readonly ConcurrentDictionary<TId, TEntity> _store = new();

    public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default)
    {
        _store.TryGetValue(id, out var entity);
        return Task.FromResult(entity);
    }

    public Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<TEntity> list = _store.Values.ToList().AsReadOnly();
        return Task.FromResult(list);
    }

    public Task SaveAsync(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.VersionIndex++;
        _store[entity.Id] = entity;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(TId id, CancellationToken ct = default)
    {
        return Task.FromResult(_store.TryRemove(id, out _));
    }
}

