using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Core.Domain;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Verifies <see cref="HttpKernelEnrollmentSynchronizer"/> signs ambient gateway identity headers, forwards
/// <c>X-Change-Reason</c> and deterministic <c>Idempotency-Key</c>, and maps upstream kernel ProblemDetails to
/// <see cref="DomainError"/>.
/// </summary>
public sealed class HttpKernelEnrollmentSynchronizerTests
{
    private static readonly string GatewaySigningKey = new('a', 64);

    [Fact]
    public async Task SynchronizeScreeningEnrollmentAsync_SignsGatewayHeadersAndReturnsCreatedEnrollmentId()
    {
        var now = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(now);
        var participantId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid().ToString();

        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        using var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            captured = req;
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { enrollment_id = enrollmentId }),
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var context = BuildHttpContext(
            tenantId: tenantId,
            userId: "crc.site101",
            roles: ["crc"],
            sites: ["SITE-101"],
            studies: ["CADENCE-101"]);

        var synchronizer = new HttpKernelEnrollmentSynchronizer(
            client,
            new HttpContextAccessor { HttpContext = context },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var result = await synchronizer.SynchronizeScreeningEnrollmentAsync(
            participantId,
            "CADENCE-101",
            "SITE-101",
            "SUBJ-101-099",
            now,
            "Register participant in Clinical Canvas",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(enrollmentId);

        captured.Should().NotBeNull();
        captured!.RequestUri!.PathAndQuery.Should().Be("/api/v1/kernel/enrollments/screen");
        captured.Headers.GetValues("Idempotency-Key").Should().ContainSingle("execution-subject-screen:CADENCE-101:SUBJ-101-099");
        captured.Headers.GetValues(GatewayHeaders.ChangeReason).Should().ContainSingle("Register participant in Clinical Canvas");
        captured.Headers.GetValues(GatewayHeaders.TenantId).Should().ContainSingle(tenantId);
        captured.Headers.GetValues(GatewayHeaders.UserId).Should().ContainSingle("crc.site101");
        captured.Headers.Contains(GatewayHeaders.Signature).Should().BeTrue();

        using var payload = JsonDocument.Parse(capturedBody!);
        payload.RootElement.GetProperty("participant_id").GetGuid().Should().Be(participantId);
        payload.RootElement.GetProperty("study_code").GetString().Should().Be("CADENCE-101");
        payload.RootElement.GetProperty("site_code").GetString().Should().Be("SITE-101");
        payload.RootElement.GetProperty("subject_code").GetString().Should().Be("SUBJ-101-099");
    }

    [Fact]
    public async Task SynchronizeScreeningEnrollmentAsync_WhenKernelReturnsForbiddenProblem_MapsToForbiddenDomainError()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        using var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"title":"K.Authorization.Denied","status":403,"detail":"Caller lacks participant.screen at SITE-102.","code":"K.Authorization.Denied"}""",
                Encoding.UTF8,
                "application/problem+json"),
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var context = BuildHttpContext(
            tenantId: Guid.NewGuid().ToString(),
            userId: "crc.site101",
            roles: ["crc"],
            sites: ["SITE-101"],
            studies: ["CADENCE-101"]);

        var synchronizer = new HttpKernelEnrollmentSynchronizer(
            client,
            new HttpContextAccessor { HttpContext = context },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var result = await synchronizer.SynchronizeScreeningEnrollmentAsync(
            Guid.NewGuid(),
            "CADENCE-101",
            "SITE-102",
            "SUBJ-102-001",
            clock.UtcNow,
            "Attempt cross-site registration",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("K.Authorization.Denied");
        result.Error.Description.Should().Contain("participant.screen");
    }

    [Fact]
    public async Task SynchronizeScreeningEnrollmentAsync_WhenKernelReturnsLockedProblem_PreservesLockedCode()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        using var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Locked)
        {
            Content = new StringContent(
                """{"title":"K.Observation.Locked","status":423,"detail":"Study CADENCE-101 is Frozen.","code":"K.Observation.Locked"}""",
                Encoding.UTF8,
                "application/problem+json"),
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var context = BuildHttpContext(
            tenantId: Guid.NewGuid().ToString(),
            userId: "crc.site101",
            roles: ["crc"],
            sites: ["SITE-101"],
            studies: ["CADENCE-101"]);

        var synchronizer = new HttpKernelEnrollmentSynchronizer(
            client,
            new HttpContextAccessor { HttpContext = context },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var result = await synchronizer.SynchronizeScreeningEnrollmentAsync(
            Guid.NewGuid(),
            "CADENCE-101",
            "SITE-101",
            "SUBJ-101-LOCKED",
            clock.UtcNow,
            "Attempt registration under Frozen study",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("K.Observation.Locked");
        result.Error.Description.Should().Contain("Frozen");
    }

    [Fact]
    public async Task SynchronizeScreeningEnrollmentAsync_WithoutTenantClaim_SkipsSyncCleanly()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        int httpCalls = 0;
        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            httpCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var context = new DefaultHttpContext();
        var synchronizer = new HttpKernelEnrollmentSynchronizer(
            client,
            new HttpContextAccessor { HttpContext = context },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var result = await synchronizer.SynchronizeScreeningEnrollmentAsync(
            Guid.NewGuid(),
            "STUDY-LEGACY",
            "SITE-01",
            "SUBJ-001",
            clock.UtcNow,
            "Legacy non-tenant test",
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Guid.Empty);
        httpCalls.Should().Be(0);
    }

    [Fact]
    public async Task SynchronizeScreeningEnrollmentAsync_RefusesReadOnlyOrUnentitledPostureWithoutCallingKernel()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        int httpCalls = 0;
        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            httpCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var expiredContext = BuildHttpContext(
            tenantId: Guid.NewGuid().ToString(),
            userId: "crc.site101",
            roles: ["crc"],
            sites: ["SITE-101"],
            studies: ["CADENCE-101"],
            tenantReadOnly: true,
            productFamilies: ["Blueprint", "Capture"]);

        var expiredSync = new HttpKernelEnrollmentSynchronizer(
            client,
            new HttpContextAccessor { HttpContext = expiredContext },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var expiredResult = await expiredSync.SynchronizeScreeningEnrollmentAsync(
            Guid.NewGuid(), "CADENCE-101", "SITE-101", "SUBJ-EXP", clock.UtcNow, "Attempt on expired tenant", TestContext.Current.CancellationToken);
        expiredResult.IsSuccess.Should().BeFalse();
        expiredResult.Error!.Code.Should().Be("K.Subscription.Expired");

        var unentitledContext = BuildHttpContext(
            tenantId: Guid.NewGuid().ToString(),
            userId: "crc.site101",
            roles: ["crc"],
            sites: ["SITE-101"],
            studies: ["CADENCE-101"],
            tenantReadOnly: false,
            productFamilies: ["Blueprint"]);

        var unentitledSync = new HttpKernelEnrollmentSynchronizer(
            client,
            new HttpContextAccessor { HttpContext = unentitledContext },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var unentitledResult = await unentitledSync.SynchronizeScreeningEnrollmentAsync(
            Guid.NewGuid(), "CADENCE-101", "SITE-101", "SUBJ-NO-CAP", clock.UtcNow, "Attempt without Capture", TestContext.Current.CancellationToken);
        unentitledResult.IsSuccess.Should().BeFalse();
        unentitledResult.Error!.Code.Should().Be("K.Commercial.ProductFamilyNotEntitled");
        httpCalls.Should().Be(0);
    }

    private static DefaultHttpContext BuildHttpContext(
        string tenantId,
        string userId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> sites,
        IReadOnlyList<string> studies,
        bool tenantReadOnly = false,
        IReadOnlyList<string>? productFamilies = null)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(CadenceClaimTypes.Subject, userId),
                new Claim(CadenceClaimTypes.TenantId, tenantId),
            ], "CadenceGateway")),
        };
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userId,
            TenantId = tenantId,
            Roles = roles,
            AssignedSites = sites,
            RawRoles = roles,
            AssignedStudies = studies,
            TenantReadOnly = tenantReadOnly,
            ProductFamilies = productFamilies ?? ["Blueprint", "Capture"],
        };
        return context;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
