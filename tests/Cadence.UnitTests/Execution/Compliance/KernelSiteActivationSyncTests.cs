using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Core.Domain;
using Cadence.Database;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Compliance;

/// <summary>
/// Unit qualification for <see cref="HttpKernelSiteActivationSynchronizer"/> and
/// <see cref="SiteComplianceApplicationService.ActivateSiteAsync"/> kernel effectivity gating (<c>K-TIME-4</c>).
/// </summary>
public sealed class KernelSiteActivationSyncTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly string TestSecret = new('k', 32);

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public async Task SynchronizeSiteActivationAsync_SendsGatewaySignedRequestWithIdempotencyAndChangeReason()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        using var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            captured = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"Active"}""", Encoding.UTF8, "application/json"),
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.test") };
        var synchronizer = new HttpKernelSiteActivationSynchronizer(
            httpClient,
            CreateHttpContextAccessor(tenantId: "tenant-201", userId: "manager@cadence.example"),
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = TestSecret }),
            new FakeClock(FixedNow));

        var result = await synchronizer.SynchronizeSiteActivationAsync(
            "STUDY-201",
            "SITE-201",
            "Ethics approval filed and site ready",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.RequestUri!.PathAndQuery.Should().Be("/api/v1/kernel/study-sites/activate-by-code");
        captured.Headers.GetValues(GatewayHeaders.UserId).Single().Should().Be("manager@cadence.example");
        captured.Headers.GetValues(GatewayHeaders.TenantId).Single().Should().Be("tenant-201");
        captured.Headers.GetValues(GatewayHeaders.TenantProductFamilies).Single().Should().Be("Blueprint,Capture,Orbit");
        captured.Headers.Contains(GatewayHeaders.Signature).Should().BeTrue();
        captured.Headers.Contains(GatewayHeaders.ChangeReason).Should().BeTrue();
        captured.Headers.GetValues("Idempotency-Key").Single().Should().Be("execution-site-activate:STUDY-201:SITE-201");

        using var json = JsonDocument.Parse(capturedBody!);
        json.RootElement.GetProperty("study_code").GetString().Should().Be("STUDY-201");
        json.RootElement.GetProperty("site_code").GetString().Should().Be("SITE-201");
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public async Task SynchronizeSiteActivationAsync_WhenKernelReturnsNoEffectiveVersionConflict_PropagatesConflictCodeAndDetail()
    {
        using var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"code":"K.Effectivity.NoEffectiveVersion","detail":"No protocol version is effective at site SITE-201 (K-TIME-4)."}""",
                Encoding.UTF8,
                "application/problem+json"),
        }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.test") };
        var synchronizer = new HttpKernelSiteActivationSynchronizer(
            httpClient,
            CreateHttpContextAccessor(tenantId: "tenant-201", userId: "manager@cadence.example"),
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = TestSecret }),
            new FakeClock(FixedNow));

        var result = await synchronizer.SynchronizeSiteActivationAsync(
            "STUDY-201",
            "SITE-201",
            "Attempt activation prior to effectivity",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("K.Effectivity.NoEffectiveVersion");
        result.Error.Description.Should().Contain("No protocol version is effective at site SITE-201");
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public async Task SynchronizeSiteActivationAsync_WhenKernelIsUnreachable_FailsClosedWithConflict()
    {
        using var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("Connection refused"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.test") };
        var synchronizer = new HttpKernelSiteActivationSynchronizer(
            httpClient,
            CreateHttpContextAccessor(tenantId: "tenant-201", userId: "manager@cadence.example"),
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = TestSecret }),
            new FakeClock(FixedNow));

        var result = await synchronizer.SynchronizeSiteActivationAsync(
            "STUDY-201",
            "SITE-201",
            "Attempt activation during outage",
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("KERNEL_SYNC_UNAVAILABLE");
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public async Task SynchronizeSiteActivationAsync_WhenTenantReadOnlyOrUnentitled_RefusesWithoutCallingKernel()
    {
        int httpCalls = 0;
        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            httpCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.test") };
        var options = new StaticOptionsMonitor<GatewayAuthenticationOptions>(
            new GatewayAuthenticationOptions { GatewaySecret = TestSecret });
        var clock = new FakeClock(FixedNow);

        var readOnlySync = new HttpKernelSiteActivationSynchronizer(
            httpClient,
            CreateHttpContextAccessor(tenantId: "tenant-201", userId: "manager@cadence.example", tenantReadOnly: true),
            options,
            clock);
        var readOnlyResult = await readOnlySync.SynchronizeSiteActivationAsync(
            "STUDY-201",
            "SITE-201",
            "Attempt activation while expired",
            TestContext.Current.CancellationToken);

        readOnlyResult.IsFailure.Should().BeTrue();
        readOnlyResult.Error!.Type.Should().Be(ErrorType.Forbidden);
        readOnlyResult.Error.Code.Should().Be("K.Subscription.Expired");

        var unentitledSync = new HttpKernelSiteActivationSynchronizer(
            httpClient,
            CreateHttpContextAccessor(
                tenantId: "tenant-201",
                userId: "manager@cadence.example",
                productFamilies: ["Engage"]),
            options,
            clock);
        var unentitledResult = await unentitledSync.SynchronizeSiteActivationAsync(
            "STUDY-201",
            "SITE-201",
            "Attempt activation without study-setup family",
            TestContext.Current.CancellationToken);

        unentitledResult.IsFailure.Should().BeTrue();
        unentitledResult.Error!.Type.Should().Be(ErrorType.Forbidden);
        unentitledResult.Error.Code.Should().Be("K.Commercial.ProductFamilyNotEntitled");
        httpCalls.Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-005")]
    [GxPRequirement("PRD-SSU-003")]
    public async Task ActivateSiteAsync_WhenKernelSynchronizerRejectsMissingEffectivity_LeavesSiteInactiveWithoutActivationAudit()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();
        var clock = new FakeClock(FixedNow);

        await using (var seedContext = CreateDbContext(databaseName, clock))
        {
            var gate = SiteComplianceCache.Create(
                new StudyCode("STUDY-201"),
                new SiteCode("SITE-201"),
                "SITE_ACTIVATION",
                isComplete: true,
                missingDocuments: Array.Empty<string>());
            seedContext.SiteComplianceCaches.Add(gate);
            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var actContext = CreateDbContext(databaseName, clock))
        {
            var rejectingSync = new StubKernelSiteActivationSynchronizer(
                DomainError.Conflict(
                    "K.Effectivity.NoEffectiveVersion",
                    "No protocol version is effective at site SITE-201 (K-TIME-4)."));
            var service = new SiteComplianceApplicationService(
                new SiteComplianceRepository(actContext),
                auditLog,
                new FakeCurrentAuditContextAccessor("manager@cadence.example", "Activate site"),
                clock,
                rejectingSync);

            var result = await service.ActivateSiteAsync(
                "SITE-201",
                new SiteActivationRequest("STUDY-201"),
                TestContext.Current.CancellationToken);

            result.IsFailure.Should().BeTrue();
            result.Error!.Code.Should().Be("K.Effectivity.NoEffectiveVersion");
        }

        await using var verifyContext = CreateDbContext(databaseName, clock);
        var persisted = await verifyContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        persisted.IsActivated.Should().BeFalse();
        auditLog.Calls.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-005")]
    [GxPRequirement("PRD-SSU-003")]
    public async Task ActivateSiteAsync_WhenKernelSynchronizerSucceeds_MarksSiteActivatedAndWritesAuditEntry()
    {
        string databaseName = Guid.NewGuid().ToString();
        var auditLog = new RecordingAuditLogWriter();
        var clock = new FakeClock(FixedNow);

        await using (var seedContext = CreateDbContext(databaseName, clock))
        {
            var gate = SiteComplianceCache.Create(
                new StudyCode("STUDY-201"),
                new SiteCode("SITE-201"),
                "SITE_ACTIVATION",
                isComplete: true,
                missingDocuments: Array.Empty<string>());
            seedContext.SiteComplianceCaches.Add(gate);
            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var acceptingSync = new StubKernelSiteActivationSynchronizer(Result<bool>.Success(true));
        await using (var actContext = CreateDbContext(databaseName, clock))
        {
            var service = new SiteComplianceApplicationService(
                new SiteComplianceRepository(actContext),
                auditLog,
                new FakeCurrentAuditContextAccessor("manager@cadence.example", "Activate site"),
                clock,
                acceptingSync);

            var result = await service.ActivateSiteAsync(
                "SITE-201",
                new SiteActivationRequest("STUDY-201"),
                TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
        }

        acceptingSync.Calls.Should().ContainSingle().Which.Should().Be(("STUDY-201", "SITE-201", "Activate site"));

        await using var verifyContext = CreateDbContext(databaseName, clock);
        var persisted = await verifyContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        persisted.IsActivated.Should().BeTrue();
        persisted.ActivatedBy.Should().Be("manager@cadence.example");
        auditLog.Calls.Should().ContainSingle().Which.Action.Should().Be("SITE_ACTIVATION");
    }

    private static ExecutionDbContext CreateDbContext(string databaseName, FakeClock clock) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new GxPAuditInterceptor(() => "manager@cadence.example", clock))
            .Options);

    private static IHttpContextAccessor CreateHttpContextAccessor(
        string tenantId,
        string userId,
        bool tenantReadOnly = false,
        IReadOnlyList<string>? productFamilies = null)
    {
        var claims = new[]
        {
            new Claim(CadenceClaimTypes.Subject, userId),
            new Claim(CadenceClaimTypes.TenantId, tenantId),
        };
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Gateway")),
        };
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userId,
            TenantId = tenantId,
            TenantReadOnly = tenantReadOnly,
            ProductFamilies = productFamilies ?? new[] { "Blueprint", "Capture", "Orbit" },
            Roles = new[] { "data_manager" },
            AssignedSites = new[] { "SITE-201" },
            AssignedStudies = new[] { "STUDY-201" },
            RawRoles = new[] { "sponsor_dm" },
        };
        return new HttpContextAccessor { HttpContext = httpContext };
    }

    private sealed class StubKernelSiteActivationSynchronizer(Result<bool> outcome) : IKernelSiteActivationSynchronizer
    {
        public List<(string StudyCode, string SiteCode, string ChangeReason)> Calls { get; } = new();

        public Task<Result<bool>> SynchronizeSiteActivationAsync(
            string studyCode,
            string siteCode,
            string changeReason,
            CancellationToken cancellationToken)
        {
            Calls.Add((studyCode, siteCode, changeReason));
            return Task.FromResult(outcome);
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
