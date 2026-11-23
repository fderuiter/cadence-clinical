using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Core.Domain;
using Cadence.Kernel.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Security;

/// <summary>
/// Verifies <see cref="HttpKernelLockGuard"/> and <see cref="ExecutionScopeGuard.RequireUnlockedAsync"/>
/// forward signed ambient gateway headers to <c>GET /api/v1/kernel/locks/check</c>, map HTTP 423 locked
/// scopes to 423 ProblemDetails, and fail closed when the Clinical Kernel cannot be verified.
/// </summary>
public sealed class HttpKernelLockGuardTests
{
    private static readonly string GatewaySigningKey = new('b', 64);

    [Fact]
    public async Task CheckWriteAllowedAsync_WhenKernelReturnsUnlocked_SignsGatewayHeadersAndReturnsNull()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        string tenantId = Guid.NewGuid().ToString("D");

        HttpRequestMessage? captured = null;
        using var handler = new StubHttpMessageHandler((req, _) =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"locked":false}""", Encoding.UTF8, "application/json"),
            });
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var context = BuildHttpContext(
            tenantId: tenantId,
            userId: "crc.site101",
            roles: ["crc"],
            sites: ["SITE-101"],
            studies: ["STUDY-101"]);

        var guard = new HttpKernelLockGuard(
            client,
            new HttpContextAccessor { HttpContext = context },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var error = await guard.CheckWriteAllowedAsync(
            "STUDY-101",
            "SITE-101",
            "SUBJ-001",
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.PathAndQuery.Should().Be(
            "/api/v1/kernel/locks/check?study_code=STUDY-101&site_code=SITE-101&subject_code=SUBJ-001");
        captured.Headers.GetValues(GatewayHeaders.TenantId).Should().ContainSingle(tenantId);
        captured.Headers.GetValues(GatewayHeaders.UserId).Should().ContainSingle("crc.site101");
        captured.Headers.Contains(GatewayHeaders.Signature).Should().BeTrue();
    }

    [Theory]
    [InlineData("K.Observation.Locked")]
    [InlineData("K.Assertion.Locked")]
    public async Task CheckWriteAllowedAsync_WhenKernelReturns423_MapsTo423ProblemResult(string kernelCode)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        using var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Locked)
        {
            Content = new StringContent(
                $$$"""{"title":"Locked","status":423,"detail":"Study STUDY-101 is locked.","code":"{{{kernelCode}}}"}""",
                Encoding.UTF8,
                "application/problem+json"),
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var guard = new HttpKernelLockGuard(
            client,
            new HttpContextAccessor { HttpContext = BuildHttpContext(Guid.NewGuid().ToString("D"), "dm.01", ["data_manager"], ["*"], ["STUDY-101"]) },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var error = await guard.CheckWriteAllowedAsync("STUDY-101", cancellationToken: TestContext.Current.CancellationToken);

        error.Should().NotBeNull();
        error!.Code.Should().Be(kernelCode);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        await error.ToProblemResult().ExecuteAsync(httpContext);
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status423Locked);
    }

    [Fact]
    public async Task CheckWriteAllowedAsync_WhenKernelReturnsForbidden_MapsToForbiddenDomainError()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        using var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"title":"Forbidden","status":403,"detail":"Caller lacks grant on STUDY-101.","code":"K.Authorization.Denied"}""",
                Encoding.UTF8,
                "application/problem+json"),
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var guard = new HttpKernelLockGuard(
            client,
            new HttpContextAccessor { HttpContext = BuildHttpContext(Guid.NewGuid().ToString("D"), "crc.site101", ["crc"], ["SITE-101"], ["STUDY-101"]) },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var error = await guard.CheckWriteAllowedAsync("STUDY-101", "SITE-101", cancellationToken: TestContext.Current.CancellationToken);

        error.Should().NotBeNull();
        error!.Type.Should().Be(ErrorType.Forbidden);
        error.Code.Should().Be("K.Authorization.Denied");
    }

    [Fact]
    public async Task CheckWriteAllowedAsync_WhenKernelReturns500OrThrowsNetworkError_FailsClosedWithConflict()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var context = BuildHttpContext(Guid.NewGuid().ToString("D"), "dm.01", ["data_manager"], ["*"], ["STUDY-101"]);

        using (var error500Handler = new StubHttpMessageHandler((_, _) =>
                   Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))))
        using (var error500Client = new HttpClient(error500Handler) { BaseAddress = new Uri("http://kernel.internal") })
        {
            var guard = new HttpKernelLockGuard(
                error500Client,
                new HttpContextAccessor { HttpContext = context },
                new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
                clock);

            var error = await guard.CheckWriteAllowedAsync("STUDY-101", cancellationToken: TestContext.Current.CancellationToken);
            error.Should().NotBeNull();
            error!.Type.Should().Be(ErrorType.Conflict);
            error.Code.Should().Be("KERNEL_LOCK_CHECK_FAILED");
        }

        using (var unreachableHandler = new StubHttpMessageHandler((_, _) =>
                   throw new HttpRequestException("Connection refused")))
        using (var unreachableClient = new HttpClient(unreachableHandler) { BaseAddress = new Uri("http://kernel.internal") })
        {
            var guard = new HttpKernelLockGuard(
                unreachableClient,
                new HttpContextAccessor { HttpContext = context },
                new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
                clock);

            var error = await guard.CheckWriteAllowedAsync("STUDY-101", cancellationToken: TestContext.Current.CancellationToken);
            error.Should().NotBeNull();
            error!.Type.Should().Be(ErrorType.Conflict);
            error.Code.Should().Be("KERNEL_LOCK_CHECK_UNAVAILABLE");
        }
    }

    [Fact]
    public async Task RequireUnlockedAsync_WhenGuardThrowsUnexpectedException_FailsClosedWith409()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IKernelLockGuard>(new ThrowingLockGuard())
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };

        var result = await ExecutionScopeGuard.RequireUnlockedAsync(
            context,
            "STUDY-101",
            "SITE-101",
            "SUBJ-001",
            TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        await result!.ExecuteAsync(context);
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    private static DefaultHttpContext BuildHttpContext(
        string tenantId,
        string userId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> sites,
        IReadOnlyList<string> studies)
    {
        var principal = new Principal
        {
            UserId = userId,
            Roles = roles,
            RawRoles = roles,
            AssignedSites = sites,
            SponsorId = null,
            UnblindedAccess = false,
            TenantId = tenantId,
            AssignedStudies = studies,
            TenantReadOnly = false,
            ProductFamilies = [nameof(ProductFamily.Capture)],
        };

        var claims = new List<Claim>
        {
            new(CadenceClaimTypes.Subject, userId),
            new(CadenceClaimTypes.TenantId, tenantId),
        };
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, GatewayAuthenticationHandler.SchemeName)),
        };
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = principal;
        return context;
    }

    private sealed class ThrowingLockGuard : IKernelLockGuard
    {
        public Task<DomainError?> CheckWriteAllowedAsync(
            string? studyCode,
            string? siteCode = null,
            string? subjectCode = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated unexpected failure");
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
