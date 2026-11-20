using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Database.Tenancy;

/// <summary>
/// Stamps the verified caller tenant UUID onto every newly added tenant-scoped record and prevents cross-tenant
/// mutation on updates (K-ID-2, ADR-2250, ADR-2514).
/// </summary>
public sealed class StandaloneTenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly StandaloneTenantSaveChangesInterceptor Instance = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            var ext = context.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
            Guid? tenantId = ext?.TenantAccessor?.GetCurrentTenantId();
            StampTenantIds(context, ext, tenantId);
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            var ext = context.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
            Guid? tenantId = ext?.TenantAccessor is not null
                ? await ext.TenantAccessor.GetCurrentTenantIdAsync(cancellationToken).ConfigureAwait(false)
                : null;
            StampTenantIds(context, ext, tenantId);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private static void StampTenantIds(DbContext context, StandaloneTenantOptionsExtension? ext, Guid? resolvedTenantId)
    {
        var tenantEntries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified
                && e.Metadata.FindProperty(StandaloneTenantModelExtensions.TenantPropertyName) is not null)
            .ToList();

        if (tenantEntries.Count == 0)
        {
            return;
        }

        Guid targetTenantId;
        if (ext?.TenantAccessor is not null)
        {
            if (resolvedTenantId is { } activeTenant && activeTenant != Guid.Empty)
            {
                targetTenantId = activeTenant;
            }
            else if (!ext.IsRelational && !ext.TenantAccessor.HasHttpRequestWithoutTenant)
            {
                // In-memory test seeding scope without an HTTP request (e.g. SeedingScope.CreateSeedingScope).
                targetTenantId = Guid.Empty;
            }
            else
            {
                throw new InvalidOperationException(
                    "K-ID-2: cannot persist tenant-scoped standalone entities without an active verified tenant.");
            }
        }
        else
        {
            // Direct unit-test DbContext constructed without UseCadenceStandaloneTenancy.
            targetTenantId = Guid.Empty;
        }

        foreach (EntityEntry entry in tenantEntries)
        {
            PropertyEntry prop = entry.Property(StandaloneTenantModelExtensions.TenantPropertyName);
            if (entry.State == EntityState.Added)
            {
                if (prop.Metadata.ClrType == typeof(string))
                {
                    if (targetTenantId != Guid.Empty)
                    {
                        prop.CurrentValue = targetTenantId.ToString("D");
                    }
                    else if (prop.CurrentValue is not string current || !Guid.TryParse(current, out _))
                    {
                        prop.CurrentValue = Guid.Empty.ToString("D");
                    }
                }
                else
                {
                    if (targetTenantId != Guid.Empty || prop.CurrentValue is not Guid existingGuid || existingGuid == Guid.Empty)
                    {
                        prop.CurrentValue = targetTenantId;
                    }
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                prop.IsModified = false;
            }
        }
    }
}
