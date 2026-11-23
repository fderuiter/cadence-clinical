using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Tenancy;
using Cadence.Security.Authentication;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Database.Tenancy;

/// <summary>
/// Stamps the verified caller tenant UUID onto every newly added tenant-scoped record, prevents cross-tenant
/// mutation on updates (K-ID-2, ADR-2250, ADR-2514), and enforces signed <c>TenantReadOnly</c>,
/// <c>ProductFamilies</c>, and <c>TenantScoping</c> commercial posture on standalone persistence.
/// </summary>
public sealed class StandaloneTenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly StandaloneTenantSaveChangesInterceptor Instance = new();

    private static readonly ProductFamily[] BlueprintFamilies = [ProductFamily.Blueprint];
    private static readonly ProductFamily[] OrbitFamilies = [ProductFamily.Orbit];
    private static readonly ProductFamily[] EngageFamilies = [ProductFamily.Engage];
    private static readonly ProductFamily[] VigilFamilies = [ProductFamily.Vigil];
    private static readonly ProductFamily[] LensFamilies = [ProductFamily.Lens];
    private static readonly ProductFamily[] AssureFamilies = [ProductFamily.Assure];
    private static readonly ProductFamily[] InteropFamilies = [ProductFamily.Capture, ProductFamily.Lens];
    private static readonly ProductFamily[] ExecutionFamilies =
    [
        ProductFamily.Capture,
        ProductFamily.Allocate,
        ProductFamily.Vigil,
        ProductFamily.Lens,
        ProductFamily.Orbit,
    ];
    private static readonly ProductFamily[] AnyFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
        ProductFamily.Orbit,
        ProductFamily.Engage,
        ProductFamily.Allocate,
        ProductFamily.Vigil,
        ProductFamily.Lens,
        ProductFamily.Assure,
    ];

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
        bool hasTenantScopedWrite = context.ChangeTracker.Entries()
            .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && e.Metadata.FindProperty(StandaloneTenantModelExtensions.TenantPropertyName) is not null);

        if (hasTenantScopedWrite && ext?.TenantAccessor is { IsSystemSuppressed: false } accessor)
        {
            if (accessor.IsCurrentTenantReadOnly && !accessor.IsHttpWriteExemptFromExpiry)
            {
                throw new InvalidOperationException(
                    $"{KernelErrors.SubscriptionExpired.Code}: cannot persist tenant-scoped standalone entities while workspace subscription is expired.");
            }

            if (!accessor.IsHttpProductFamilyExempt
                && accessor.CurrentTenantProductFamilies is { } activeFamilies
                && ResolveContextFamilies(context.GetType().Name) is { } requiredFamilies
                && !HasAnyFamily(activeFamilies, requiredFamilies))
            {
                string requiredList = string.Join(", ", requiredFamilies.Select(static f => f.ToString()));
                throw new InvalidOperationException(
                    $"{CommercialErrors.ProductFamilyNotEntitled.Code}: cannot persist tenant-scoped standalone entities in {context.GetType().Name} without an entitled product family ({requiredList}).");
            }
        }

        var tenantEntries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified
                && e.Metadata.FindProperty(StandaloneTenantModelExtensions.TenantPropertyName) is not null)
            .ToList();

        if (tenantEntries.Count == 0)
        {
            return;
        }

        if (ext?.TenantAccessor is { IsSystemSuppressed: false, IsHttpWriteExemptFromExpiry: false, CurrentTenantScoping: { } scoping })
        {
            EnforceEntityScopingCaps(tenantEntries, scoping);
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

    private static IReadOnlyList<ProductFamily>? ResolveContextFamilies(string contextTypeName) =>
        contextTypeName switch
        {
            "StudiesDbContext" or "ProtocolsDbContext" => BlueprintFamilies,
            "CtmsDbContext"
                or "EtmfDbContext"
                or "EisfDbContext"
                or "FileshareDbContext"
                or "PaymentsDbContext" => OrbitFamilies,
            "EconsentDbContext" => EngageFamilies,
            "SafetyDbContext" => VigilFamilies,
            "ImagingDbContext" => LensFamilies,
            "QualityDbContext" or "KnowledgeDbContext" or "TrainingDbContext" => AssureFamilies,
            "InteropDbContext" => InteropFamilies,
            "ExecutionDbContext" or "ExecutionModuleDbContext" => ExecutionFamilies,
            _ => AnyFamilies,
        };

    private static bool HasAnyFamily(IReadOnlyList<string> activeFamilies, IReadOnlyList<ProductFamily> requiredFamilies)
    {
        for (int i = 0; i < requiredFamilies.Count; i++)
        {
            string expected = requiredFamilies[i].ToString();
            for (int j = 0; j < activeFamilies.Count; j++)
            {
                if (string.Equals(activeFamilies[j], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void EnforceEntityScopingCaps(IReadOnlyList<EntityEntry> tenantEntries, TenantScopingCaps scoping)
    {
        foreach (EntityEntry entry in tenantEntries)
        {
            if (scoping.Countries.Count > 0)
            {
                string? country = TryReadMutatedString(entry, "Country")
                    ?? TryReadMutatedString(entry, "CountryCode")
                    ?? TryReadMutatedString(entry, "IsoCountryCode");
                if (country is not null
                    && !scoping.Countries.Any(c => string.Equals(c, country, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"{CommercialErrors.ScopingCapExceeded.Code}: Country '{country}' is outside the workspace subscription's contracted countries ({string.Join(", ", scoping.Countries)}).");
                }
            }

            if (!string.IsNullOrWhiteSpace(scoping.StudyPhase))
            {
                string? phase = TryReadMutatedString(entry, "StudyPhase")
                    ?? TryReadMutatedString(entry, "Phase");
                if (phase is not null
                    && !string.Equals(phase, scoping.StudyPhase, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{CommercialErrors.ScopingCapExceeded.Code}: Study phase '{phase}' does not match the workspace subscription's contracted phase ('{scoping.StudyPhase}').");
                }
            }

            if ((TryReadMutatedPositiveInt(entry, "StudyDurationMonths", out int duration)
                    || TryReadMutatedPositiveInt(entry, "PlannedDurationMonths", out duration))
                && duration > scoping.StudyDurationMonths)
            {
                throw new InvalidOperationException(
                    $"{CommercialErrors.ScopingCapExceeded.Code}: Study duration ({duration} months) exceeds the workspace subscription's contracted cap ({scoping.StudyDurationMonths} months).");
            }

            if ((TryReadMutatedPositiveInt(entry, "TargetSiteCount", out int sites)
                    || TryReadMutatedPositiveInt(entry, "PlannedSites", out sites)
                    || TryReadMutatedPositiveInt(entry, "SiteCount", out sites))
                && sites > scoping.SiteCount)
            {
                throw new InvalidOperationException(
                    $"{CommercialErrors.ScopingCapExceeded.Code}: Site count ({sites}) exceeds the workspace subscription's contracted cap ({scoping.SiteCount}).");
            }

            if ((TryReadMutatedPositiveInt(entry, "TargetEnrollment", out int participants)
                    || TryReadMutatedPositiveInt(entry, "PlannedEnrollment", out participants)
                    || TryReadMutatedPositiveInt(entry, "ParticipantCount", out participants))
                && participants > scoping.ParticipantCount)
            {
                throw new InvalidOperationException(
                    $"{CommercialErrors.ScopingCapExceeded.Code}: Participant count ({participants}) exceeds the workspace subscription's contracted cap ({scoping.ParticipantCount}).");
            }
        }
    }

    private static string? TryReadMutatedString(EntityEntry entry, string propertyName)
    {
        if (entry.Metadata.FindProperty(propertyName) is not { } propMeta || propMeta.ClrType != typeof(string))
        {
            return null;
        }

        PropertyEntry prop = entry.Property(propertyName);
        if (entry.State != EntityState.Added && !prop.IsModified)
        {
            return null;
        }

        return prop.CurrentValue is string raw && !string.IsNullOrWhiteSpace(raw) ? raw.Trim() : null;
    }

    private static bool TryReadMutatedPositiveInt(EntityEntry entry, string propertyName, out int value)
    {
        value = 0;
        if (entry.Metadata.FindProperty(propertyName) is not { } propMeta
            || (propMeta.ClrType != typeof(int) && propMeta.ClrType != typeof(int?)))
        {
            return false;
        }

        PropertyEntry prop = entry.Property(propertyName);
        if (entry.State != EntityState.Added && !prop.IsModified)
        {
            return false;
        }

        if (prop.CurrentValue is int parsed && parsed > 0)
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
