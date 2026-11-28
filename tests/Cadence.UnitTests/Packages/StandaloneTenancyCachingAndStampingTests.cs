using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Concurrency;
using Cadence.Database.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class StandaloneTenancyCachingAndStampingTests
{
    private static readonly Guid AlphaTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BetaTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GammaTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task TenantScopeAuthorizationHandler_And_CurrentTenantAccessor_MemoizeActiveTenantPerRequest_AndNeverCacheInactiveOrMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        var resolver = new CountingTenantCodeResolver(new Dictionary<string, Guid?>(StringComparer.Ordinal)
        {
            ["tenant-alpha"] = AlphaTenantId,
            ["tenant-inactive"] = null,
        });

        var httpContextAccessor = new HttpContextAccessor();
        var accessor = new CurrentTenantAccessor(resolver, httpContextAccessor);
        var services = new ServiceCollection()
            .AddSingleton<ITenantAccessVerifier>(accessor)
            .BuildServiceProvider();

        var httpContext = CreateAuthenticatedHttpContext("user-1", "tenant-alpha", services);
        httpContextAccessor.HttpContext = httpContext;

        var handler = new TenantScopeAuthorizationHandler();
        var requirement = new TenantScopeRequirement();

        var authContext = new AuthorizationHandlerContext([requirement], httpContext.User, httpContext);
        await handler.HandleAsync(authContext);

        authContext.HasSucceeded.Should().BeTrue();
        resolver.ResolveCalls.Should().Be(1);
        RequestTenantResolutionCache.TryGet(httpContext, "tenant-alpha", out Guid cachedTenantId).Should().BeTrue();
        cachedTenantId.Should().Be(AlphaTenantId);

        // Subsequent sync and async lookups on the same HTTP request reuse HttpContext.Items with zero extra resolver calls.
        (await accessor.GetCurrentTenantIdAsync(ct)).Should().Be(AlphaTenantId);
        AssertSyncTenantId(accessor, AlphaTenantId);
        (await accessor.IsActiveTenantAsync("tenant-alpha", ct)).Should().BeTrue();
        resolver.ResolveCalls.Should().Be(1);

        // Inactive or unknown tenants fail closed with 403 and are never cached in RequestTenantResolutionCache.
        var inactiveHttpContext = CreateAuthenticatedHttpContext("user-2", "tenant-inactive", services);
        httpContextAccessor.HttpContext = inactiveHttpContext;
        var inactiveAuthContext = new AuthorizationHandlerContext([requirement], inactiveHttpContext.User, inactiveHttpContext);
        await handler.HandleAsync(inactiveAuthContext);

        inactiveAuthContext.HasFailed.Should().BeTrue();
        RequestTenantResolutionCache.TryGet(inactiveHttpContext, "tenant-inactive", out _).Should().BeFalse();
        (await accessor.GetCurrentTenantIdAsync(ct)).Should().BeNull();
        resolver.GetCallsFor("tenant-inactive").Should().Be(2, "inactive tenant lookups must not be memoized");
    }

    [Fact]
    public async Task CurrentTenantAccessor_HonorsBindAndSuppressOverrides_WithoutCorruptingMemoizedRequestTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var resolver = new CountingTenantCodeResolver(new Dictionary<string, Guid?>(StringComparer.Ordinal)
        {
            ["tenant-alpha"] = AlphaTenantId,
            ["tenant-gamma"] = GammaTenantId,
        });

        var httpContextAccessor = new HttpContextAccessor();
        var accessor = new CurrentTenantAccessor(resolver, httpContextAccessor);
        var services = new ServiceCollection()
            .AddSingleton<ITenantAccessVerifier>(accessor)
            .BuildServiceProvider();

        var httpContext = CreateAuthenticatedHttpContext("user-1", "tenant-alpha", services);
        httpContextAccessor.HttpContext = httpContext;

        (await accessor.GetCurrentTenantIdAsync(ct)).Should().Be(AlphaTenantId);
        resolver.GetCallsFor("tenant-alpha").Should().Be(1);

        using (accessor.Bind(BetaTenantId))
        {
            (await accessor.GetCurrentTenantIdAsync(ct)).Should().Be(BetaTenantId);
            AssertSyncTenantId(accessor, BetaTenantId);
        }

        using (accessor.Bind("tenant-gamma"))
        {
            (await accessor.GetCurrentTenantIdAsync(ct)).Should().Be(GammaTenantId);
            resolver.GetCallsFor("tenant-gamma").Should().Be(1);
        }

        using (accessor.Suppress())
        {
            accessor.IsSystemSuppressed.Should().BeTrue();
            (await accessor.GetCurrentTenantIdAsync(ct)).Should().BeNull();
            AssertSyncTenantId(accessor, null);
        }

        // Restored request context still hits the original memoized tenant-alpha UUID with zero new lookups.
        (await accessor.GetCurrentTenantIdAsync(ct)).Should().Be(AlphaTenantId);
        AssertSyncTenantId(accessor, AlphaTenantId);
        resolver.GetCallsFor("tenant-alpha").Should().Be(1);
        RequestTenantResolutionCache.TryGet(httpContext, "tenant-gamma", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SaveChangesAsync_ExecutesSingleTenantStampAndSingleConcurrencyIncrement_WhilePreservingAuditMerkleHashesAndOutboxTenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = new DateTimeOffset(2026, 11, 25, 9, 30, 0, TimeSpan.Zero);
        const string actor = "cra.auditor@cadence.clinical";
        const string milestoneId = "MS-DEDUP-001";

        var clockCombined = new FakeClock(start);
        var countingAccessor = new CountingTenantAccessor(AlphaTenantId);
        var combinedBuilder = new DbContextOptionsBuilder<CtmsDbContext>();
        combinedBuilder.UseInMemoryDatabase($"ctms-combined-{Guid.NewGuid():N}");
        combinedBuilder.UseCadenceStandaloneTenancy(countingAccessor, isRelational: false);
        combinedBuilder.AddInterceptors(
            StandaloneTenantSaveChangesInterceptor.Instance,
            StandaloneConcurrencyTokenInterceptor.Instance,
            new GxPAuditInterceptor(() => actor, clockCombined));

        var clockReference = new FakeClock(start);
        var referenceAccessor = new CountingTenantAccessor(AlphaTenantId);
        var referenceBuilder = new DbContextOptionsBuilder<CtmsDbContext>();
        referenceBuilder.UseInMemoryDatabase($"ctms-ref-{Guid.NewGuid():N}");
        referenceBuilder.UseCadenceStandaloneTenancy(referenceAccessor, isRelational: false);
        referenceBuilder.AddInterceptors(new GxPAuditInterceptor(() => actor, clockReference));

        await using var combinedDb = new CtmsDbContext(combinedBuilder.Options);
        await using var referenceDb = new CtmsDbContext(referenceBuilder.Options);

        // Act 1: Insert CtmsSiteMilestone.
        var combinedMilestone = new CtmsSiteMilestone(milestoneId, CtmsSiteMilestoneStatus.Active)
        {
            SiteId = "SITE-101",
            StudyId = "STUDY-101",
            MilestoneType = "SIV",
            ReasonForChange = "Initial SIV schedule",
        };
        combinedDb.CtmsSiteMilestones.Add(combinedMilestone);
        await combinedDb.SaveChangesAsync(ct);

        var referenceMilestone = new CtmsSiteMilestone(milestoneId, CtmsSiteMilestoneStatus.Active)
        {
            SiteId = "SITE-101",
            StudyId = "STUDY-101",
            MilestoneType = "SIV",
            ReasonForChange = "Initial SIV schedule",
        };
        referenceDb.CtmsSiteMilestones.Add(referenceMilestone);
        referenceDb.Entry(referenceMilestone).Property<Guid>(StandaloneTenantModelExtensions.TenantPropertyName).CurrentValue = AlphaTenantId;
        await referenceDb.SaveChangesAsync(ct);

        countingAccessor.AsyncCalls.Should().Be(1, "tenant resolution during SaveChangesAsync must run once, not twice");
        combinedMilestone.VersionIndex.Should().Be(1, "VersionIndex must not be double-incremented on insert");

        // Act 2: Complete the milestone (emitting a stateful aggregate transition event into TransactionalOutbox) and save.
        clockCombined.Advance(TimeSpan.FromMinutes(5));
        clockReference.Advance(TimeSpan.FromMinutes(5));

        combinedMilestone.Complete(actor, clockCombined.UtcNow, "Site initiation completed");
        combinedMilestone.ReasonForChange = "Site initiation completed";
        await combinedDb.SaveChangesAsync(ct);

        referenceMilestone.Complete(actor, clockReference.UtcNow, "Site initiation completed");
        referenceMilestone.ReasonForChange = "Site initiation completed";
        await referenceDb.SaveChangesAsync(ct);

        countingAccessor.AsyncCalls.Should().Be(2, "each SaveChangesAsync must resolve the active tenant at most once");
        combinedMilestone.VersionIndex.Should().Be(2, "VersionIndex must increment exactly once per committed mutation");
        referenceMilestone.VersionIndex.Should().Be(2);

        var combinedAudits = await combinedDb.AuditLogEntries
            .Where(a => a.EntityName == nameof(CtmsSiteMilestone))
            .OrderBy(a => a.VersionIndex)
            .ToListAsync(ct);
        var referenceAudits = await referenceDb.AuditLogEntries
            .Where(a => a.EntityName == nameof(CtmsSiteMilestone))
            .OrderBy(a => a.VersionIndex)
            .ToListAsync(ct);

        combinedAudits.Should().HaveCount(2);
        referenceAudits.Should().HaveCount(2);

        for (int i = 0; i < combinedAudits.Count; i++)
        {
            combinedAudits[i].ChangesJson.Should().Be(referenceAudits[i].ChangesJson);
            combinedAudits[i].PredecessorHash.Should().Be(referenceAudits[i].PredecessorHash);
            combinedAudits[i].BlockHash.Should().Be(referenceAudits[i].BlockHash);
            combinedDb.Entry(combinedAudits[i])
                .Property<Guid>(StandaloneTenantModelExtensions.TenantPropertyName)
                .CurrentValue
                .Should().Be(AlphaTenantId);
        }

        var outboxRows = await combinedDb.TransactionalOutbox.ToListAsync(ct);
        outboxRows.Should().NotBeEmpty();
        foreach (var outbox in outboxRows)
        {
            combinedDb.Entry(outbox)
                .Property<Guid>(StandaloneTenantModelExtensions.TenantPropertyName)
                .CurrentValue
                .Should().Be(AlphaTenantId, "TransactionalOutboxMessage rows flushed during SaveChangesAsync must receive the verified tenant_id");
        }
    }

    private static void AssertSyncTenantId(ICurrentTenantAccessor accessor, Guid? expected) =>
        accessor.GetCurrentTenantId().Should().Be(expected);

    private static DefaultHttpContext CreateAuthenticatedHttpContext(
        string userId,
        string tenantCode,
        IServiceProvider requestServices)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = requestServices,
        };
        var claims = new[]
        {
            new Claim(CadenceClaimTypes.Subject, userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(CadenceClaimTypes.TenantId, tenantCode),
        };
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(claims, GatewayAuthenticationHandler.SchemeName, CadenceClaimTypes.Subject, ClaimTypes.Role));
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userId,
            TenantId = tenantCode,
            Roles = ["CRA"],
            AssignedStudies = ["*"],
            AssignedSites = ["SITE-101"],
        };
        return context;
    }

    private sealed class CountingTenantCodeResolver(IReadOnlyDictionary<string, Guid?> mappings) : ITenantCodeResolver
    {
        private readonly Dictionary<string, int> _callsByCode = new(StringComparer.Ordinal);

        public int ResolveCalls { get; private set; }

        public int GetCallsFor(string code) => _callsByCode.GetValueOrDefault(code);

        public Guid? ResolveTenantId(string tenantCode)
        {
            RecordCall(tenantCode);
            return mappings.GetValueOrDefault(tenantCode);
        }

        public ValueTask<Guid?> ResolveTenantIdAsync(string tenantCode, CancellationToken cancellationToken = default)
        {
            RecordCall(tenantCode);
            return ValueTask.FromResult(mappings.GetValueOrDefault(tenantCode));
        }

        public Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(
                mappings.Values.Where(v => v.HasValue).Select(v => v!.Value).ToList());

        private void RecordCall(string code)
        {
            ResolveCalls++;
            _callsByCode[code] = _callsByCode.GetValueOrDefault(code) + 1;
        }
    }

    private sealed class CountingTenantAccessor(Guid tenantId) : ICurrentTenantAccessor
    {
        public int SyncCalls { get; private set; }

        public int AsyncCalls { get; private set; }

        public Guid? GetCurrentTenantId()
        {
            SyncCalls++;
            return tenantId;
        }

        public ValueTask<Guid?> GetCurrentTenantIdAsync(CancellationToken cancellationToken = default)
        {
            AsyncCalls++;
            return ValueTask.FromResult<Guid?>(tenantId);
        }

        public bool IsSystemSuppressed => false;

        public bool HasHttpRequestWithoutTenant => false;

        public bool IsCurrentTenantReadOnly => false;

        public bool IsHttpWriteExemptFromExpiry => false;

        public bool IsHttpProductFamilyExempt => false;

        public IReadOnlyList<string>? CurrentTenantProductFamilies => null;

        public TenantScopingCaps? CurrentTenantScoping => null;
    }
}
