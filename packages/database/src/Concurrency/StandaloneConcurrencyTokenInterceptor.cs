using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Database.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Database.Concurrency;

/// <summary>
/// Advances <see cref="int"/> and <see cref="long"/> optimistic concurrency tokens (<c>VersionIndex</c> and
/// shadow <c>_version</c>) on <see cref="EntityState.Modified"/> entries as <c>OriginalValue + 1</c> during
/// <c>SaveChanges</c> and <c>SaveChangesAsync</c>.
/// <para>
/// Computing from <c>OriginalValue</c> rather than <c>CurrentValue</c> ensures version numbers advance once per
/// committed change from the loaded baseline without compounding across transient execution-strategy retries or
/// manual <c>VersionIndex++</c> increments in application handlers.
/// </para>
/// </summary>
public sealed class StandaloneConcurrencyTokenInterceptor : SaveChangesInterceptor
{
    public static readonly StandaloneConcurrencyTokenInterceptor Instance = new();

    public const string ShadowVersionPropertyName = "_version";
    public const string VersionIndexPropertyName = "VersionIndex";

    public static long? ResolveExpectedVersion(long? explicitExpectedVersion, HttpContext? httpContext = null)
    {
        if (explicitExpectedVersion.HasValue)
        {
            return explicitExpectedVersion.Value;
        }

        string? rawIfMatch = httpContext?.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(rawIfMatch))
        {
            return null;
        }

        string normalized = rawIfMatch.Trim();
        if (normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..].Trim();
        }

        normalized = normalized.Trim('"');
        return long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;
    }

    public static void EnforceExpectedVersion<TEntity>(
        DbContext dbContext,
        TEntity entity,
        long? explicitExpectedVersion = null,
        HttpContext? httpContext = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(entity);

        long? expected = ResolveExpectedVersion(explicitExpectedVersion, httpContext);
        if (!expected.HasValue)
        {
            return;
        }

        EntityEntry<TEntity> entry = dbContext.Entry(entity);
        foreach (var property in entry.Metadata.GetProperties())
        {
            if (!property.IsConcurrencyToken)
            {
                continue;
            }

            PropertyEntry propEntry = entry.Property(property.Name);
            if (property.ClrType == typeof(long))
            {
                long current = propEntry.CurrentValue is long c ? c : 0L;
                if (current != expected.Value)
                {
                    throw new DbUpdateConcurrencyException(
                        $"Expected version {expected.Value} for {typeof(TEntity).Name}, but current committed version is {current}.");
                }

                propEntry.OriginalValue = expected.Value;
                return;
            }

            if (property.ClrType == typeof(int))
            {
                int expectedInt = checked((int)expected.Value);
                int current = propEntry.CurrentValue is int c ? c : 0;
                if (current != expectedInt)
                {
                    throw new DbUpdateConcurrencyException(
                        $"Expected version {expectedInt} for {typeof(TEntity).Name}, but current committed version is {current}.");
                }

                propEntry.OriginalValue = expectedInt;
                return;
            }
        }
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is { } context && !HasDelegatingSaveInterceptor(context, includeAuditInterceptor: false))
        {
            IncrementConcurrencyTokens(context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && !HasDelegatingSaveInterceptor(context, includeAuditInterceptor: true))
        {
            IncrementConcurrencyTokens(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    internal static bool HasDelegatingSaveInterceptor(DbContext context, bool includeAuditInterceptor)
    {
        var interceptors = context.GetService<IDbContextOptions>()?
            .FindExtension<CoreOptionsExtension>()?
            .Interceptors;
        if (interceptors is null)
        {
            return false;
        }

        return interceptors.Any(i =>
            i is StandaloneTenantSaveChangesInterceptor
            || (includeAuditInterceptor && i is GxPAuditInterceptor));
    }

    public static void IncrementConcurrencyTokens(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            foreach (var property in entry.Metadata.GetProperties())
            {
                if (!property.IsConcurrencyToken)
                {
                    continue;
                }

                if (property.ClrType == typeof(long))
                {
                    var propertyEntry = entry.Property(property.Name);
                    long baseline = propertyEntry.OriginalValue is long orig ? orig : 0L;
                    propertyEntry.CurrentValue = baseline + 1L;
                }
                else if (property.ClrType == typeof(int))
                {
                    var propertyEntry = entry.Property(property.Name);
                    int baseline = propertyEntry.OriginalValue is int orig ? orig : 0;
                    propertyEntry.CurrentValue = baseline + 1;
                }
            }
        }
    }
}
