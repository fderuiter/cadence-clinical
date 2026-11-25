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
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Kernel;

/// <summary>
/// Unit coverage for <see cref="HttpKernelLockGuard"/>: gateway-signed <c>GET /api/v1/kernel/locks/in-force</c>
/// requests, per-request <see cref="HttpContext.Items"/> memoization, <c>K.Observation.Locked</c> (<c>423 Locked</c>)
/// mapping on covering <c>Frozen</c> or <c>Locked</c> assertions, fail-closed problem handling on network/kernel
/// errors, and <see cref="ServiceAccount"/> signing for background consumers (#5376, #5466).
/// </summary>
public sealed class HttpKernelLockGuardTests
{
    private const string GatewaySecret = TestSecurityContext.GatewaySecret;
    private const string TenantCode = "tenant-lock-unit";
    private const string StudyCode = "STUDY-LOCK-01";
    private const string SiteCode = "SITE-101";
    private const string SubjectCode = "SUBJ-001";

    [Theory]
    [InlineData("Frozen", "study_site")]
    [InlineData("Locked", "study")]
    public async Task EnsureUnlockedAsync_WhenKernelReportsCoveringLock_ReturnsObservationLockedMappedTo423AndMemoizesPerRequest(
        string lockKind,
        string scopeKind)
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"study_code":"{{StudyCode}}","locked":true,"lock_kind":"{{lockKind}}","scope_kind":"{{scopeKind}}"}""",
                Encoding.UTF8,
                "application/json"),
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://kernel.internal") };
        var accessor = CreateHttpContextAccessor();
        var guard = new HttpKernelLockGuard(httpClient, accessor, new StaticOptionsMonitor(GatewaySecret), new FakeClock());

        var first = await guard.EnsureUnlockedAsync(StudyCode, SiteCode, SubjectCode, ct);
        var second = await guard.EnsureUnlockedAsync(StudyCode, SiteCode, SubjectCode, ct);

        first.IsFailure.Should().BeTrue();
        first.Error!.Code.Should().Be(HttpKernelLockGuard.ObservationLockedCode);
        first.Error.Description.Should().Contain(lockKind).And.Contain(scopeKind);
        second.IsFailure.Should().BeTrue();
        handler.Requests.Should().ContainSingle("identical scope checks within one HTTP request must memoize in HttpContext.Items");

        var sent = handler.Requests[0];
        sent.RequestUri!.PathAndQuery.Should().Be(
            $"/api/v1/kernel/locks/in-force?study_code={StudyCode}&site_code={SiteCode}&subject_code={SubjectCode}");
        sent.Headers.GetValues(GatewayHeaders.TenantId).Should().ContainSingle().Which.Should().Be(TenantCode);
        sent.Headers.GetValues(GatewayHeaders.UserId).Should().ContainSingle().Which.Should().Be("crc-lock-1");
        sent.Headers.Contains(GatewayHeaders.Signature).Should().BeTrue("outbound kernel queries must carry a fresh gateway HMAC signature");

        // Verify DomainErrorHttpMapping maps K.Observation.Locked to 423 Locked.
        var httpContext = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        httpContext.RequestServices = services.BuildServiceProvider();
        await first.Error.ToProblemResult().ExecuteAsync(httpContext);
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status423Locked);
    }

    [Fact]
    public async Task EnsureUnlockedAsync_WhenKernelReportsUnlocked_ReturnsSuccessAndQueriesAgainForDistinctSubject()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"study_code":"{{StudyCode}}","locked":false}""",
                Encoding.UTF8,
                "application/json"),
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://kernel.internal") };
        var guard = new HttpKernelLockGuard(httpClient, CreateHttpContextAccessor(), new StaticOptionsMonitor(GatewaySecret), new FakeClock());

        (await guard.EnsureUnlockedAsync(StudyCode, SiteCode, "SUBJ-A", ct)).IsSuccess.Should().BeTrue();
        (await guard.EnsureUnlockedAsync(StudyCode, SiteCode, "SUBJ-B", ct)).IsSuccess.Should().BeTrue();

        handler.Requests.Should().HaveCount(2, "distinct subjects have distinct lock scopes");
    }

    [Fact]
    public async Task EnsureUnlockedAsync_WhenKernelUnreachableOrReturnsProblem_FailsClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var failingHandler = new RecordingHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        using var failingClient = new HttpClient(failingHandler) { BaseAddress = new Uri("https://kernel.internal") };
        var failingGuard = new HttpKernelLockGuard(failingClient, CreateHttpContextAccessor(), new StaticOptionsMonitor(GatewaySecret), new FakeClock());

        var netFailure = await failingGuard.EnsureUnlockedAsync(StudyCode, SiteCode, null, ct);
        netFailure.IsFailure.Should().BeTrue();
        netFailure.Error!.Code.Should().Be("KERNEL_LOCK_CHECK_UNAVAILABLE");

        var problemHandler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"code":"K.Authorization.Denied","detail":"Missing capability"}""",
                Encoding.UTF8,
                "application/json"),
        });
        using var problemClient = new HttpClient(problemHandler) { BaseAddress = new Uri("https://kernel.internal") };
        var problemGuard = new HttpKernelLockGuard(problemClient, CreateHttpContextAccessor(), new StaticOptionsMonitor(GatewaySecret), new FakeClock());

        var problemFailure = await problemGuard.EnsureUnlockedAsync(StudyCode, null, null, ct);
        problemFailure.IsFailure.Should().BeTrue();
        problemFailure.Error!.Code.Should().Be("K.Authorization.Denied");
    }

    [Fact]
    public async Task EnsureUnlockedForServiceAsync_SignsWithServiceAccountAndRejectsLockedStudy()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"study_code":"{{StudyCode}}","locked":true,"lock_kind":"Locked","scope_kind":"study"}""",
                Encoding.UTF8,
                "application/json"),
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://kernel.internal") };
        var guard = new HttpKernelLockGuard(httpClient, new HttpContextAccessor(), new StaticOptionsMonitor(GatewaySecret), new FakeClock());

        var result = await guard.EnsureUnlockedForServiceAsync(
            ServiceAccounts.EConsentConsumer,
            TenantCode,
            StudyCode,
            SiteCode,
            SubjectCode,
            ct);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(HttpKernelLockGuard.ObservationLockedCode);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.GetValues(GatewayHeaders.UserId)
            .Should().ContainSingle().Which.Should().Be(ServiceAccounts.EConsentConsumer.UserId);
        handler.Requests[0].Headers.GetValues(GatewayHeaders.Roles)
            .Should().ContainSingle().Which.Should().Be("SYSTEM_SERVICE");
    }

    [Fact]
    public async Task EnsureUnlockedBatchAsync_SendsSingleBatchKernelLockQuery_MemoizesPerRequest_AndElevatesVerifiedEtmfServiceCaller()
    {
        var ct = TestContext.Current.CancellationToken;
        string? capturedBody = null;
        var handler = new RecordingHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"study_code":"{{StudyCode}}","locked":true,"lock_kind":"Frozen","scope_kind":"study_site"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(CadenceClaimTypes.Subject, "svc:etmf"),
            new Claim(CadenceClaimTypes.TenantId, TenantCode),
        ], GatewayAuthenticationHandler.SchemeName));
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "svc:etmf",
            RawRoles = [PermissionMatrix.EtmfServiceRole],
            AssignedSites = ["SITE-101", "SITE-102", "SITE-103"],
            AssignedStudies = [StudyCode],
            TenantId = TenantCode,
            ProductFamilies = [nameof(Cadence.Kernel.Tenancy.ProductFamily.Orbit)],
        };

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://kernel.internal") };
        var guard = new HttpKernelLockGuard(
            httpClient,
            new HttpContextAccessor { HttpContext = context },
            new StaticOptionsMonitor(GatewaySecret),
            new FakeClock());

        var first = await guard.EnsureUnlockedBatchAsync(StudyCode, ["SITE-102", "SITE-101", "SITE-103"], ct);
        var second = await guard.EnsureUnlockedBatchAsync(StudyCode, ["SITE-103", "SITE-101", "SITE-102"], ct);

        first.IsFailure.Should().BeTrue();
        first.Error!.Code.Should().Be(HttpKernelLockGuard.ObservationLockedCode);
        first.Error.Description.Should().Contain("Frozen").And.Contain("study_site");
        second.IsFailure.Should().BeTrue();

        handler.Requests.Should().ContainSingle("all sites in a batch webhook must be checked in one HTTP call and memoized across site orderings");
        var sent = handler.Requests[0];
        sent.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri!.PathAndQuery.Should().Be("/api/v1/kernel/locks/in-force/batch");
        sent.Headers.GetValues(GatewayHeaders.UserId).Should().ContainSingle().Which.Should().Be("svc:etmf");
        sent.Headers.GetValues(GatewayHeaders.Roles).Should().ContainSingle().Which.Should().Contain("SYSTEM_SERVICE");
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain($"\"study_code\":\"{StudyCode}\"");
        capturedBody.Should().Contain("\"SITE-101\"").And.Contain("\"SITE-102\"").And.Contain("\"SITE-103\"");
    }

    private static HttpContextAccessor CreateHttpContextAccessor()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
        [
            new Claim(CadenceClaimTypes.Subject, "crc-lock-1"),
            new Claim(CadenceClaimTypes.TenantId, TenantCode),
        ], GatewayAuthenticationHandler.SchemeName);
        context.User = new ClaimsPrincipal(identity);
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "crc-lock-1",
            RawRoles = ["CRC"],
            AssignedSites = [SiteCode],
            AssignedStudies = [StudyCode],
            TenantId = TenantCode,
            ProductFamilies = [nameof(Cadence.Kernel.Tenancy.ProductFamily.Capture)],
        };
        return new HttpContextAccessor { HttpContext = context };
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            Requests.Add(clone);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StaticOptionsMonitor(string gatewaySecret) : IOptionsMonitor<GatewayAuthenticationOptions>
    {
        public GatewayAuthenticationOptions CurrentValue { get; } = new() { GatewaySecret = gatewaySecret };

        public GatewayAuthenticationOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<GatewayAuthenticationOptions, string?> listener) => null;
    }
}
