using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Ctms.Application;

/// <summary>
/// Idempotently seeds baseline <c>CADENCE-101</c> startup greenlight gates for <c>SITE-101</c> and <c>SITE-102</c>
/// so site startup evaluation, country matrix review, and greenlight certification operate on live persistence.
/// </summary>
public static class CtmsDbSeeder
{
    public const string SeedStudyId = "CADENCE-101";
    public const string SeedActor = "svc:ctms-seeder";
    public const string SeedReason = "Initial CADENCE-101 site startup greenlight baseline";

    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private static readonly Guid DefaultTestTenantGuid =
        DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-test");

    public static async Task EnsureSeededAsync(CtmsDbContext db, IClock clock, CancellationToken ct = default)
    {
        string tenantSuffix = await ResolveSeedTenantSuffixAsync(db, ct).ConfigureAwait(false);
        bool hasSite101 = await db.CtmsSiteGreenlightGates
            .AsNoTracking()
            .AnyAsync(g => g.StudyId == SeedStudyId && g.SiteId == "SITE-101", ct)
            .ConfigureAwait(false);
        bool hasSite102 = await db.CtmsSiteGreenlightGates
            .AsNoTracking()
            .AnyAsync(g => g.StudyId == SeedStudyId && g.SiteId == "SITE-102", ct)
            .ConfigureAwait(false);

        if (hasSite101 && hasSite102)
        {
            return;
        }

        await SeedLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var existingSites = await db.CtmsSiteGreenlightGates
                .AsNoTracking()
                .Where(g => g.StudyId == SeedStudyId && (g.SiteId == "SITE-101" || g.SiteId == "SITE-102"))
                .Select(g => g.SiteId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            DateTimeOffset now = clock.UtcNow;
            bool added = false;

            if (!existingSites.Contains("SITE-101", StringComparer.Ordinal))
            {
                db.CtmsSiteGreenlightGates.Add(new CtmsSiteGreenlightGate($"gate-cadence-101-site-101{tenantSuffix}")
                {
                    StudyId = SeedStudyId,
                    SiteId = "SITE-101",
                    SiteName = "Memorial Clinical Research Center",
                    CountryCode = "US",
                    ReadinessPercentage = 100,
                    FacetRegulatoryIrb = true,
                    FacetCtaBudget = true,
                    FacetEisfBaseline = true,
                    FacetDoaLog = true,
                    FacetStaffTraining = true,
                    FacetIpRelease = true,
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = SeedActor,
                    ReasonForChange = SeedReason,
                    VersionIndex = 1,
                });
                added = true;
            }

            if (!existingSites.Contains("SITE-102", StringComparer.Ordinal))
            {
                db.CtmsSiteGreenlightGates.Add(new CtmsSiteGreenlightGate($"gate-cadence-101-site-102{tenantSuffix}")
                {
                    StudyId = SeedStudyId,
                    SiteId = "SITE-102",
                    SiteName = "University Cardiology Site",
                    CountryCode = "US",
                    ReadinessPercentage = 67,
                    FacetRegulatoryIrb = true,
                    FacetCtaBudget = true,
                    FacetEisfBaseline = false,
                    FacetDoaLog = true,
                    FacetStaffTraining = true,
                    FacetIpRelease = false,
                    UnfulfilledReasons = "Incomplete eISF Baseline; Pending Investigational Product Depot Release",
                    CreatedAt = now.UtcDateTime,
                    CreatedBy = SeedActor,
                    ReasonForChange = SeedReason,
                    VersionIndex = 1,
                });
                added = true;
            }

            if (added)
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static async Task<string> ResolveSeedTenantSuffixAsync(CtmsDbContext db, CancellationToken ct)
    {
        var ext = db.GetService<IDbContextOptions>()?.FindExtension<StandaloneTenantOptionsExtension>();
        Guid? tenantId = ext?.TenantAccessor is not null
            ? await ext.TenantAccessor.GetCurrentTenantIdAsync(ct).ConfigureAwait(false)
            : null;
        if (tenantId is null || tenantId.Value == Guid.Empty || tenantId.Value == DefaultTestTenantGuid)
        {
            return string.Empty;
        }

        return $"-{tenantId.Value.ToString("N")[..8]}";
    }
}

public sealed class CtmsStartupSeederHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<CtmsStartupSeederHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var probeScope = scopeFactory.CreateAsyncScope();
            var probeDb = probeScope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            if (!await probeDb.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var tenantResolver = probeScope.ServiceProvider.GetService<ITenantCodeResolver>();
            var tenantScope = probeScope.ServiceProvider.GetService<ITenantScope>();
            var tenantAccessor = probeScope.ServiceProvider.GetService<ICurrentTenantAccessor>();

            if (tenantResolver is not null && tenantScope is not null)
            {
                var tenantIds = await tenantResolver.ListActiveTenantIdsAsync(cancellationToken).ConfigureAwait(false);
                if (tenantIds.Count > 0)
                {
                    foreach (Guid tenantId in tenantIds)
                    {
                        using var bound = tenantScope.Bind(tenantId);
                        await using var seededScope = scopeFactory.CreateAsyncScope();
                        var db = seededScope.ServiceProvider.GetRequiredService<CtmsDbContext>();
                        var clock = seededScope.ServiceProvider.GetRequiredService<IClock>();
                        await CtmsDbSeeder.EnsureSeededAsync(db, clock, cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }
            }

            if (tenantAccessor is null || await tenantAccessor.GetCurrentTenantIdAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                var clock = probeScope.ServiceProvider.GetRequiredService<IClock>();
                await CtmsDbSeeder.EnsureSeededAsync(probeDb, clock, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Skipping startup CTMS baseline seed because database is not ready yet.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
