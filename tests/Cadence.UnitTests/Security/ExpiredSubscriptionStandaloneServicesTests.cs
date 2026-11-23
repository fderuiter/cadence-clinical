using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// Verifies across standalone and kernel clinical services that a gateway-signed expired tenant identity
/// (<c>X-Tenant-Read-Only: true</c>) is refused on mutating clinical endpoints — including all ten non-closeout
/// 21 CFR Part 11 step-up signature endpoints (<see cref="ElectronicSignatureRequired"/>) — with
/// <c>403 K.Subscription.Expired</c> before consuming <c>X-Sig-Token</c>, while preserving GET reads, regulated exports
/// (<c>POST /api/v1/etmf/documents/export</c>), and closeout routes marked with <see cref="AllowExpiredTenantWriteMetadata"/>,
/// and enforces <c>ProductFamilies</c> and <c>SubscriptionScoping</c> across all 20 backend surfaces.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExpiredSubscriptionStandaloneServicesTests : IClassFixture<ExpiredSubscriptionStandaloneServicesTests.HostFixture>
{
    private const string ProbeHeader = "X-Expiry-Guard-Probe";

    private readonly HostFixture _hosts;

    public ExpiredSubscriptionStandaloneServicesTests(HostFixture hosts)
    {
        _hosts = hosts;
        _hosts.ResetCounters();
    }

    public static TheoryData<string> StandaloneClinicalHosts => new(
        "execution",
        "ctms",
        "etmf",
        "econsent",
        "safety",
        "payments",
        "tickets");

    [Theory(DisplayName = "StandaloneClinicalService_BlocksMutationsWhenTenantReadOnly_AndExemptsReadsAndExports")]
    [MemberData(nameof(StandaloneClinicalHosts))]
    public async Task StandaloneClinicalServiceBlocksMutationsWhenTenantReadOnlyAndExemptsReadsAndExports(string host)
    {
        var ct = TestContext.Current.CancellationToken;
        var (booted, _) = _hosts.Get(host);

        var endpoints = booted.Endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .ToList();

        endpoints.Should().NotBeEmpty();

        using var client = booted.CreateUnsignedClient();
        byte[] secret = GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret);
        var now = Cadence.Kernel.Time.SystemClock.Instance.UtcNow;

        int blockedMutationsChecked = 0;
        int safeReadsChecked = 0;
        int exemptMutationsChecked = 0;
        int declaredSafeReads = 0;

        foreach (var endpoint in endpoints)
        {
            string? permission = ExtractFirstPermission(endpoint);
            if (permission is null)
            {
                continue;
            }

            string method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Get;
            string url = MaterializeRoute(endpoint.RoutePattern);
            bool isSafeRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
            if (isSafeRead)
            {
                declaredSafeReads++;
            }

            var caller = CallerHolding(permission) with
            {
                TenantId = "expired-standalone-tenant",
                StudyIds = ["*"],
                SiteIds = ["SITE-101"],
                TenantReadOnly = true,
            };

            bool isRegulatedExport = string.Equals(permission, "etmf_document:export", StringComparison.Ordinal);
            bool isExplicitAllow = endpoint.Metadata.GetMetadata<AllowExpiredTenantWriteMetadata>() is not null;

            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            request.Headers.Add(ProbeHeader, "1");
            var signedCaller = isSafeRead
                ? caller
                : caller with { ChangeReason = "Automated standalone expiry guard verification" };
            foreach (var (key, value) in signedCaller.ToSignedHeaders(now, secret))
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }

            if (!isSafeRead)
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);

            if (!isSafeRead && !isRegulatedExport && !isExplicitAllow)
            {
                response.StatusCode.Should().Be(
                    HttpStatusCode.Forbidden,
                    $"{host} {method} {url} must refuse mutating requests when TenantReadOnly is true");
                using var problem = JsonDocument.Parse(body);
                problem.RootElement.GetProperty("code").GetString().Should().Be(
                    ExpiredSubscriptionWriteGuardMiddleware.SubscriptionExpiredCode,
                    $"{host} {method} {url} must return K.Subscription.Expired");
                blockedMutationsChecked++;
            }
            else
            {
                response.StatusCode.Should().Be(
                    HttpStatusCode.NoContent,
                    $"{host} {method} {url} is exempt from ExpiredSubscriptionWriteGuardMiddleware and must pass through to the endpoint");
                if (isSafeRead)
                {
                    safeReadsChecked++;
                }
                else
                {
                    exemptMutationsChecked++;
                }
            }
        }

        blockedMutationsChecked.Should().BeGreaterThan(0, $"standalone clinical host '{host}' has mutating clinical routes that must be guarded");
        safeReadsChecked.Should().Be(declaredSafeReads, $"all {declaredSafeReads} declared read routes on '{host}' must remain open");
        if (string.Equals(host, "etmf", StringComparison.Ordinal))
        {
            exemptMutationsChecked.Should().BeGreaterThan(0, "etmf POST /api/v1/etmf/documents/export must be verified as exempt");
        }
    }

    [Fact(DisplayName = "StandaloneStepUpSignatureEndpoint_RefusesExpiredTenantBeforeConsumingSigToken")]
    public async Task StandaloneStepUpSignatureEndpointRefusesExpiredTenantBeforeConsumingSigToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var (booted, sigTokenStore) = _hosts.Get("econsent");

        var signEndpoint = booted.Endpoints.First(e =>
            e.Metadata.GetMetadata<ElectronicSignatureRequired>() is not null
            && ExtractFirstPermission(e) is not null);
        string permission = ExtractFirstPermission(signEndpoint)!;
        string method = signEndpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Post;
        string url = MaterializeRoute(signEndpoint.RoutePattern);

        var now = Cadence.Kernel.Time.SystemClock.Instance.UtcNow;
        byte[] secret = GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret);
        string sigToken = TestSecurityContext.CreateSigToken(
            userId: $"holder-{permission}",
            action: url,
            semanticAction: "econsent.sign",
            issuedAt: now);

        var caller = CallerHolding(permission) with
        {
            TenantId = "expired-standalone-tenant",
            StudyIds = ["*"],
            SiteIds = ["SITE-101"],
            TenantReadOnly = true,
            ChangeReason = "Attempt eConsent signature on expired workspace",
            SigToken = sigToken,
        };

        using var client = booted.CreateUnsignedClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        foreach (var (key, value) in caller.ToSignedHeaders(now, secret))
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        using var response = await client.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be(ExpiredSubscriptionWriteGuardMiddleware.SubscriptionExpiredCode);
        sigTokenStore.ConsumeCalls.Should().Be(0, "ExpiredSubscriptionWriteGuardMiddleware must refuse before ElectronicSignatureEndpointFilter consumes the single-use X-Sig-Token");
    }

    [Fact(DisplayName = "StandaloneService_RejectsClientTamperedTenantReadOnlyHeader_With401")]
    public async Task StandaloneServiceRejectsClientTamperedTenantReadOnlyHeaderWith401()
    {
        var ct = TestContext.Current.CancellationToken;
        var (booted, _) = _hosts.Get("ctms");

        var postEndpoint = booted.Endpoints.First(e =>
            ExtractFirstPermission(e) is not null
            && (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) ?? false));
        string permission = ExtractFirstPermission(postEndpoint)!;
        string url = MaterializeRoute(postEndpoint.RoutePattern);

        var expiredCaller = CallerHolding(permission) with
        {
            TenantId = "expired-standalone-tenant",
            StudyIds = ["*"],
            TenantReadOnly = true,
        };

        using var unsignedClient = booted.CreateUnsignedClient();
        var signedHeaders = expiredCaller.ToSignedHeaders(
            Cadence.Kernel.Time.SystemClock.Instance.UtcNow,
            GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret));

        using var tampered = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        foreach (var (key, value) in signedHeaders)
        {
            tampered.Headers.TryAddWithoutValidation(
                key,
                string.Equals(key, GatewayHeaders.TenantReadOnly, StringComparison.OrdinalIgnoreCase) ? "false" : value);
        }

        using var response = await unsignedClient.SendAsync(tampered, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "AllTenNonCloseoutElectronicSignatureRoutes_RefuseExpiredTenantBeforeConsumingSigToken")]
    public async Task AllTenNonCloseoutElectronicSignatureRoutesRefuseExpiredTenantBeforeConsumingSigToken()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] secret = GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret);
        var now = Cadence.Kernel.Time.SystemClock.Instance.UtcNow;

        int standaloneSignatureRoutesVerified = 0;
        int kernelSignatureRoutesVerified = 0;
        int closeoutSignatureRoutesFound = 0;

        foreach (var host in ServiceHosts.All.Keys)
        {
            var (booted, sigTokenStore) = _hosts.Get(host);

            var sigEndpoints = booted.Endpoints
                .Where(e => e.Metadata.GetMetadata<ElectronicSignatureRequired>() is not null)
                .ToList();

            if (sigEndpoints.Count == 0)
            {
                continue;
            }

            using var client = booted.CreateUnsignedClient();
            foreach (var sigEndpoint in sigEndpoints)
            {
                if (sigEndpoint.Metadata.GetMetadata<AllowExpiredTenantWriteMetadata>() is not null)
                {
                    closeoutSignatureRoutesFound++;
                    continue;
                }

                string? permission = ExtractFirstPermission(sigEndpoint);
                GatewayIdentity baseCaller = permission is not null
                    ? CallerHolding(permission)
                    : TestSecurityContext.Identity("holder-kernel-sig", "Sponsor Admin");

                string method = sigEndpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Post;
                string url = MaterializeRoute(sigEndpoint.RoutePattern);
                string sigToken = TestSecurityContext.CreateSigToken(
                    userId: baseCaller.UserId,
                    action: url,
                    semanticAction: $"{host}.sign",
                    issuedAt: now);

                var expiredCaller = baseCaller with
                {
                    TenantId = "expired-esig-tenant",
                    StudyIds = ["*"],
                    SiteIds = ["SITE-101"],
                    TenantReadOnly = true,
                    ProductFamilies = TestSecurityContext.AllProductFamilies,
                    ChangeReason = "Verify expired tenant step-up signature block",
                    SigToken = sigToken,
                };

                int beforeConsume = sigTokenStore.ConsumeCalls;
                using var request = new HttpRequestMessage(new HttpMethod(method), url)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("Idempotency-Key", $"esig-exp-{standaloneSignatureRoutesVerified + kernelSignatureRoutesVerified}");
                foreach (var (key, value) in expiredCaller.ToSignedHeaders(now, secret))
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }

                using var response = await client.SendAsync(request, ct);
                string body = await response.Content.ReadAsStringAsync(ct);

                response.StatusCode.Should().Be(
                    HttpStatusCode.Forbidden,
                    $"{host} {method} {url} must refuse expired tenants with 403 before consuming X-Sig-Token. Body: {body}");
                using var problem = JsonDocument.Parse(body);
                problem.RootElement.GetProperty("code").GetString().Should().Be(
                    ExpiredSubscriptionWriteGuardMiddleware.SubscriptionExpiredCode,
                    $"{host} {method} {url} must return K.Subscription.Expired");
                sigTokenStore.ConsumeCalls.Should().Be(
                    beforeConsume,
                    $"{host} {method} {url} must not consume X-Sig-Token when workspace is expired");

                if (string.Equals(host, "kernel", StringComparison.Ordinal))
                {
                    kernelSignatureRoutesVerified++;
                }
                else
                {
                    standaloneSignatureRoutesVerified++;
                }
            }
        }

        standaloneSignatureRoutesVerified.Should().Be(
            10,
            "all ten standalone ElectronicSignatureRequired clinical routes across econsent, execution, knowledge, safety, and tickets must be verified");
        kernelSignatureRoutesVerified.Should().Be(
            4,
            "kernel non-closeout ElectronicSignatureRequired routes (library-items/{itemRef}/approve, protocol-versions/{versionRef}/approve, validation/findings/{findingRef}/accept, and validation/findings/{findingRef}/explain) must all be verified");
        closeoutSignatureRoutesFound.Should().Be(
            3,
            "only the three closeout signature routes (form closeout signature + lock apply + lock release) carry AllowExpiredTenantWriteMetadata");
    }

    [Fact(DisplayName = "AllTwentyBackendSurfaces_EnforceProductFamiliesAndSubscriptionScopingCaps")]
    public async Task AllTwentyBackendSurfacesEnforceProductFamiliesAndSubscriptionScopingCaps()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] secret = GatewayIdentity.EncodeSecret(TestSecurityContext.GatewaySecret);
        var now = Cadence.Kernel.Time.SystemClock.Instance.UtcNow;
        var tightScoping = new TenantScopingCaps("Phase II", 12, ["US"], 1, 25, 1);

        var productFamilySurfacesVerified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scopingSurfacesVerified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var host in ServiceHosts.All.Keys)
        {
            var (booted, _) = _hosts.Get(host);

            using var client = booted.CreateUnsignedClient();
            var candidates = booted.Endpoints
                .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
                .ToList();

            foreach (var endpoint in candidates)
            {
                string method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Get;
                string url = MaterializeRoute(endpoint.RoutePattern);
                string? surface = ExtractApiV1Surface(url);
                if (surface is null)
                {
                    continue;
                }

                string? permission = ExtractFirstPermission(endpoint);
                GatewayIdentity baseCaller = permission is not null
                    ? CallerHolding(permission)
                    : TestSecurityContext.Identity($"holder-{surface}", "Sponsor Admin");

                // 1. Verify ProductFamilyGuardMiddleware on the first module-gated route of each surface.
                if (!productFamilySurfacesVerified.Contains(surface)
                    && endpoint.Metadata.GetMetadata<AllowBaselineProductFamilyMetadata>() is null
                    && ProductFamilyGuardMiddleware.ResolveRequiredFamilies(url, method) is { Count: > 0 })
                {
                    var unentitledCaller = baseCaller with
                    {
                        TenantId = "unentitled-surface-tenant",
                        StudyIds = ["*"],
                        SiteIds = ["SITE-101"],
                        ProductFamilies = [],
                        ChangeReason = HttpMethods.IsGet(method) ? null : "Verify product family enforcement",
                    };

                    using var unentitledReq = new HttpRequestMessage(new HttpMethod(method), url);
                    if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
                    {
                        unentitledReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    }

                    foreach (var (key, value) in unentitledCaller.ToSignedHeaders(now, secret))
                    {
                        unentitledReq.Headers.TryAddWithoutValidation(key, value);
                    }

                    using var unentitledRes = await client.SendAsync(unentitledReq, ct);
                    string body = await unentitledRes.Content.ReadAsStringAsync(ct);
                    unentitledRes.StatusCode.Should().Be(
                        HttpStatusCode.Forbidden,
                        $"surface '{surface}' ({host} {method} {url}) must reject unentitled ProductFamilies with 403. Body: {body}");
                    using var problem = JsonDocument.Parse(body);
                    problem.RootElement.GetProperty("code").GetString().Should().Be("K.Commercial.ProductFamilyNotEntitled");
                    productFamilySurfacesVerified.Add(surface);
                }

                // 2. Verify SubscriptionScopingGuardMiddleware on the first mutating non-exempt route of each surface.
                bool isMutating = !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method);
                bool isScopingExempt = endpoint.Metadata.GetMetadata<AllowBaselineProductFamilyMetadata>() is not null
                    || endpoint.Metadata.GetMetadata<AllowExpiredTenantWriteMetadata>() is not null
                    || string.Equals(permission, "etmf_document:export", StringComparison.Ordinal)
                    || url.StartsWith("/api/v1/kernel/entitlements", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("/api/v1/kernel/locks", StringComparison.OrdinalIgnoreCase);

                if (isMutating && !isScopingExempt && !scopingSurfacesVerified.Contains(surface))
                {
                    var outOfScopeCaller = baseCaller with
                    {
                        TenantId = "scoped-surface-tenant",
                        StudyIds = ["*"],
                        SiteIds = ["SITE-101"],
                        ProductFamilies = TestSecurityContext.AllProductFamilies,
                        TenantScoping = tightScoping,
                        ChangeReason = "Verify subscription scoping cap enforcement",
                    };

                    using var scopedReq = new HttpRequestMessage(new HttpMethod(method), url)
                    {
                        Content = new StringContent("""{"country":"JP"}""", Encoding.UTF8, "application/json"),
                    };
                    scopedReq.Headers.Add(ProbeHeader, "1");
                    foreach (var (key, value) in outOfScopeCaller.ToSignedHeaders(now, secret))
                    {
                        scopedReq.Headers.TryAddWithoutValidation(key, value);
                    }

                    using var scopedRes = await client.SendAsync(scopedReq, ct);
                    string body = await scopedRes.Content.ReadAsStringAsync(ct);
                    scopedRes.StatusCode.Should().Be(
                        HttpStatusCode.Forbidden,
                        $"surface '{surface}' ({host} {method} {url}) must reject exceeded SubscriptionScoping caps with 403. Body: {body}");
                    using var problem = JsonDocument.Parse(body);
                    problem.RootElement.GetProperty("code").GetString().Should().Be("K.Commercial.ScopingCapExceeded");
                    scopingSurfacesVerified.Add(surface);
                }
            }
        }

        // Also verify middleware resolution & guard execution on hosts whose surface maps only status/read routes (eisf, fileshare, org, quality, knowledge),
        // completing all 20 backend surfaces in ServiceTopology.
        foreach ((string hostKey, string surface) in new[]
        {
            ("eisf", "eisf"),
            ("fileshare", "fileshare"),
            ("org", "org"),
            ("quality", "quality"),
            ("knowledge", "knowledge"),
        })
        {
            ServiceHosts.All.Should().ContainKey(hostKey);
            var (booted, _) = _hosts.Get(hostKey);

            var familyGuard = booted.Services.GetRequiredService<ProductFamilyGuardMiddleware>();
            var scopingGuard = booted.Services.GetRequiredService<SubscriptionScopingGuardMiddleware>();
            ProductFamilyGuardMiddleware.ResolveRequiredFamilies($"/api/v1/{surface}/items", HttpMethods.Post)
                .Should().NotBeNullOrEmpty();

            var unentitledCtx = new DefaultHttpContext();
            unentitledCtx.Request.Method = HttpMethods.Post;
            unentitledCtx.Request.Path = $"/api/v1/{surface}/items";
            unentitledCtx.Response.Body = new System.IO.MemoryStream();
            unentitledCtx.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
            {
                UserId = $"holder-{surface}",
                TenantId = "unentitled-surface-tenant",
                Roles = ["sponsor_admin"],
                ProductFamilies = [],
            };
            await familyGuard.InvokeAsync(unentitledCtx, _ => Task.CompletedTask);
            unentitledCtx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            productFamilySurfacesVerified.Add(surface);

            var scopedCtx = new DefaultHttpContext();
            scopedCtx.Request.Method = HttpMethods.Post;
            scopedCtx.Request.Path = $"/api/v1/{surface}/items";
            scopedCtx.Request.ContentType = "application/json";
            byte[] payload = Encoding.UTF8.GetBytes("""{"country":"JP"}""");
            scopedCtx.Request.Body = new System.IO.MemoryStream(payload);
            scopedCtx.Request.ContentLength = payload.Length;
            scopedCtx.Response.Body = new System.IO.MemoryStream();
            scopedCtx.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
            {
                UserId = $"holder-{surface}",
                TenantId = "scoped-surface-tenant",
                Roles = ["sponsor_admin"],
                ProductFamilies = TestSecurityContext.AllProductFamilies,
                TenantScoping = tightScoping,
            };
            await scopingGuard.InvokeAsync(scopedCtx, _ => Task.CompletedTask);
            scopedCtx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            scopingSurfacesVerified.Add(surface);
        }

        productFamilySurfacesVerified.Count.Should().Be(
            20,
            $"all 20 /api/v1/<surface> backend surfaces must enforce ProductFamilies (verified: {string.Join(", ", productFamilySurfacesVerified.OrderBy(s => s))})");
        scopingSurfacesVerified.Count.Should().Be(
            20,
            $"all 20 /api/v1/<surface> backend surfaces must enforce SubscriptionScoping caps (verified: {string.Join(", ", scopingSurfacesVerified.OrderBy(s => s))})");
    }

    private static string? ExtractApiV1Surface(string path)
    {
        const string prefix = "/api/v1/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || path.Length <= prefix.Length)
        {
            return null;
        }

        string remainder = path[prefix.Length..];
        int slashIndex = remainder.IndexOf('/');
        string segment = slashIndex >= 0 ? remainder[..slashIndex] : remainder;
        return string.Equals(segment, "status", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "signatures", StringComparison.OrdinalIgnoreCase)
            ? null
            : segment.ToLowerInvariant();
    }

    private static string? ExtractFirstPermission(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .Where(p => p is not null && p.StartsWith(PermissionPolicyProvider.PolicyPrefix, StringComparison.Ordinal))
            .Select(p => p![PermissionPolicyProvider.PolicyPrefix.Length..])
            .FirstOrDefault();

    private static GatewayIdentity CallerHolding(string permission)
    {
        string role = PermissionMatrix.ROLE_PERMISSIONS.Keys
            .Order(StringComparer.Ordinal)
            .First(candidate => !string.Equals(candidate, "auditor", StringComparison.Ordinal) && Holds(candidate, permission));
        return TestSecurityContext.Identity($"holder-{permission}", role);
    }

    private static bool Holds(string role, string permission) =>
        PermissionMatrix.HasPermission(
            new Principal { Roles = RoleNormalization.GetNormalizedRoles([role]), RawRoles = [role] },
            permission);

    private static string MaterializeRoute(RoutePattern pattern)
    {
        var segments = pattern.PathSegments.Select(segment =>
            string.Concat(segment.Parts.Select(part => part switch
            {
                RoutePatternLiteralPart literal => literal.Content,
                RoutePatternSeparatorPart separator => separator.Content,
                RoutePatternParameterPart param => SampleParameterValue(param),
                _ => "sample",
            })));
        return "/" + string.Join("/", segments);
    }

    private static string SampleParameterValue(RoutePatternParameterPart param)
    {
        bool isGuid = param.ParameterPolicies.Any(p => string.Equals(p.Content, "guid", StringComparison.OrdinalIgnoreCase));
        if (isGuid)
        {
            return "01960000-0000-7000-8000-000000000101";
        }

        bool isInt = param.ParameterPolicies.Any(p =>
            string.Equals(p.Content, "int", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Content, "long", StringComparison.OrdinalIgnoreCase));
        return isInt ? "1" : "SITE-101";
    }

    public sealed class HostFixture : IAsyncDisposable
    {
        private readonly ServiceHostCache _cache = new();
        private readonly Dictionary<string, CountingSigTokenUseStore> _stores = new(StringComparer.Ordinal);

        internal (BootedHost Host, CountingSigTokenUseStore SigTokenStore) Get(string service)
        {
            if (!_stores.TryGetValue(service, out var store))
            {
                store = new CountingSigTokenUseStore();
                _stores[service] = store;
            }

            var capturedStore = store;
            var booted = _cache.Get(service, "expiry-probe", services =>
            {
                CadenceDatabaseExtensions.AddCadenceInMemoryTenancy(services, new DeterministicTenantCodeResolver());
                foreach (var existing in services.Where(d => d.ServiceType == typeof(ISigTokenUseStore)).ToList())
                {
                    services.Remove(existing);
                }

                services.AddSingleton<ISigTokenUseStore>(capturedStore);
                services.AddSingleton<IAuthorizationMiddlewareResultHandler, ExpiryProbeResultHandler>();
            });

            return (booted, store);
        }

        internal void ResetCounters()
        {
            foreach (var store in _stores.Values)
            {
                store.Reset();
            }
        }

        public ValueTask DisposeAsync() => _cache.DisposeAsync();
    }

    internal sealed class CountingSigTokenUseStore : IDurableSigTokenUseStore
    {
        private ReplayCache _inner = new();

        public int ConsumeCalls { get; private set; }

        public void Reset()
        {
            ConsumeCalls = 0;
            _inner = new ReplayCache();
        }

        public Task<bool> TryRecordFirstUseAsync(string tokenId, DateTimeOffset retainUntil, System.Threading.CancellationToken cancellationToken)
        {
            ConsumeCalls++;
            return _inner.TryRecordFirstUseAsync(tokenId, retainUntil, cancellationToken);
        }
    }

    private sealed class ExpiryProbeResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _inner = new();

        public async Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Succeeded
                && context.Request.Headers.ContainsKey(ProbeHeader)
                && context.GetEndpoint() is { } matchedEndpoint)
            {
                context.SetEndpoint(new Endpoint(
                    ctx =>
                    {
                        ctx.Response.Headers[ProbeHeader] = "admitted";
                        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                        return Task.CompletedTask;
                    },
                    matchedEndpoint.Metadata,
                    matchedEndpoint.DisplayName));
            }

            await _inner.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
