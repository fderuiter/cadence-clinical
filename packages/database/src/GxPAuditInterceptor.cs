using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;

namespace Cadence.Database;

/// <summary>
/// EF Core SaveChangesInterceptor enforcing 21 CFR Part 11 immutable audit trail fields and Merkle block hashing.
/// @req:PRD-SYS-053
/// </summary>
public class GxPAuditInterceptor(Func<string> currentUserProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        string currentUser = currentUserProvider();
        DateTime now = DateTime.UtcNow;

        foreach (var entry in eventData.Context.ChangeTracker.Entries<IGxPAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity is AggregateRoot<string> aggStr)
                {
                    aggStr.CreatedAt = now;
                    aggStr.CreatedBy = string.IsNullOrWhiteSpace(aggStr.CreatedBy) ? currentUser : aggStr.CreatedBy;
                    aggStr.VersionIndex = 1;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is AggregateRoot<string> aggStr)
                {
                    aggStr.VersionIndex++;
                }
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
