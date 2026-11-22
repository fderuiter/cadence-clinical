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
/// Verifies across all seven standalone clinical services (<c>execution</c>, <c>ctms</c>, <c>etmf</c>, <c>econsent</c>,
/// <c>safety</c>, <c>payments</c>, <c>tickets</c>) that a gateway-signed expired tenant identity
/// (<c>X-Tenant-Read-Only: true</c>) is refused on mutating clinical endpoints with <c>403 K.Subscription.Expired</c>
/// while preserving GET reads, regulated exports (<c>POST /api/v1/etmf/documents/export</c>), and 21 CFR Part 11
/// step-up signature endpoints (<see cref="ElectronicSignatureRequired"/>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExpiredSubscriptionStandaloneServicesTests
{
    private const string ProbeHeader = "X-Expiry-Guard-Probe";

    public static TheoryData<string> StandaloneClinicalHosts => new(
        "execution",
        "ctms",
        "etmf",
        "econsent",
        "safety",
        "payments",
        "tickets");

    [Theory(DisplayName = "StandaloneClinicalService_BlocksMutationsWhenTenantReadOnly_AndExemptsReadsExportsAndStepUpSignatures")]
    [MemberData(nameof(StandaloneClinicalHosts))]
    public async Task StandaloneClinicalServiceBlocksMutationsWhenTenantReadOnlyAndExemptsReadsExportsAndStepUpSignatures(string host)
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantResolver = new DeterministicTenantCodeResolver();

        await using var booted = ServiceHosts.All[host](services =>
        {
            CadenceDatabaseExtensions.AddCadenceInMemoryTenancy(services, tenantResolver);
            foreach (var existing in services.Where(d => d.ServiceType == typeof(ISigTokenUseStore)).ToList())
            {
                services.Remove(existing);
            }

            services.AddSingleton<ISigTokenUseStore, ProbeSigTokenUseStore>();
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, ExpiryProbeResultHandler>();
        });

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
            bool isStepUpSignature = endpoint.Metadata.GetMetadata<ElectronicSignatureRequired>() is not null;
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

            if (!isSafeRead && !isRegulatedExport && !isStepUpSignature && !isExplicitAllow)
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

        if (string.Equals(host, "econsent", StringComparison.Ordinal))
        {
            exemptMutationsChecked.Should().BeGreaterThan(0, "econsent step-up electronic signature endpoints must be verified as exempt");
        }
    }

    [Fact(DisplayName = "StandaloneService_RejectsClientTamperedTenantReadOnlyHeader_With401")]
    public async Task StandaloneServiceRejectsClientTamperedTenantReadOnlyHeaderWith401()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantResolver = new DeterministicTenantCodeResolver();
        await using var booted = ServiceHosts.All["ctms"](services =>
        {
            CadenceDatabaseExtensions.AddCadenceInMemoryTenancy(services, tenantResolver);
        });

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

    private sealed class ProbeSigTokenUseStore : ReplayCache, IDurableSigTokenUseStore;

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
