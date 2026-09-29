using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Database;

/// <summary>
/// Generic EF Core repository implementation using <see cref="DbContext"/>.
/// @req:PRD-SYS-001
/// </summary>
public class EfRepository<T>(DbContext context) : IEfRepository<T> where T : class
{
    protected DbContext Context { get; } = context;

    protected DbSet<T> DbSet { get; } = context.Set<T>();

    public virtual IQueryable<T> Query(bool includeSoftDeleted = false)
    {
        return includeSoftDeleted ? DbSet.IgnoreQueryFilters() : DbSet.AsNoTracking();
    }

    public virtual async Task<T?> GetByIdAsync<TId>(TId id, CancellationToken ct = default) where TId : notnull
    {
        return await DbSet.FindAsync([id], cancellationToken: ct).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
    {
        return await DbSet.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
    }

    public virtual async Task<T> AddAsync(T entity, CancellationToken ct = default)
    {
        await DbSet.AddAsync(entity, ct).ConfigureAwait(false);
        return entity;
    }

    public virtual Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        DbSet.Update(entity);
        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        DbSet.Remove(entity);
        return Task.CompletedTask;
    }

    public virtual Task SoftDeleteAsync(T entity, CancellationToken ct = default)
    {
        if (entity is ISoftDelete softDelete)
        {
            softDelete.IsDeleted = true;
            softDelete.DeletedAt = DateTimeOffset.UtcNow;
            DbSet.Update(entity);
        }
        else
        {
            DbSet.Remove(entity);
        }

        return Task.CompletedTask;
    }

    public virtual async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await Context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
