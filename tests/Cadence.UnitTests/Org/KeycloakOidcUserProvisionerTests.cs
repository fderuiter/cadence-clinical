using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Kernel;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Kernel;
using Cadence.Kernel.Time;
using Cadence.Modules.Org;
using Cadence.Modules.Org.Provisioning;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Cadence.UnitTests.Org;

public sealed class KeycloakOidcUserProvisionerTests
{
    private const string SyntheticEmail = "crc.invite@example.com"; // deid-ignore: synthetic unit test email
    private const string SyntheticGiven = "Coordinator";
    private const string SyntheticFamily = "Invitee";

    [Fact]
    public async Task ProvisionOrReuseAsync_NewUser_ConfiguresKeycloak26ProfileCreatesUserSendsActionsEmailAndCachesToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var logger = new CapturingLogger<KeycloakOidcUserProvisioner>();
        var createdSubjectId = Guid.Parse("01950000-0000-7000-8000-000000000777"); // deid-ignore
        var requests = new List<(HttpMethod Method, string PathAndQuery, string? Body)>();
        int lookupCount = 0;

        using var httpClient = new HttpClient(new StubHttpHandler(async req =>
        {
            string? body = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            requests.Add((req.Method, req.RequestUri!.PathAndQuery, body));

            if (req.RequestUri.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, """{"access_token":"kc-admin-tok","expires_in":300}""");
            }

            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.EndsWith("/users/profile", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, """{"attributes":[]}""");
            }

            if (req.Method == HttpMethod.Put && req.RequestUri.AbsolutePath.EndsWith("/users/profile", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, body ?? "{}");
            }

            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.EndsWith("/users", StringComparison.Ordinal))
            {
                lookupCount++;
                if (lookupCount == 1)
                {
                    return JsonResponse(HttpStatusCode.OK, "[]");
                }

                return JsonResponse(
                    HttpStatusCode.OK,
                    $$$"""[{"id":"{{{createdSubjectId:D}}}","username":"{{{SyntheticEmail}}}","email":"{{{SyntheticEmail}}}","enabled":true,"attributes":{"tenant_id":["cadence-local"]}}]""");
            }

            if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath.EndsWith("/users", StringComparison.Ordinal))
            {
                var created = new HttpResponseMessage(HttpStatusCode.Created);
                created.Headers.Location = new Uri($"http://keycloak.local/admin/realms/cadence/users/{createdSubjectId:D}");
                return created;
            }

            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.Contains("/roles/", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, """{"id":"role-si","name":"Site Investigator"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }))
        {
            BaseAddress = new Uri("http://keycloak.local"),
        };

        var options = new OrgKeycloakOptions
        {
            AdminBaseUrl = "http://keycloak.local",
            Realm = "cadence",
            ClientId = "cadence-admin",
            ClientSecret = "secret", // pragma: allowlist secret
            SendWelcomeEmail = true,
        };

        using var provisioner = new KeycloakOidcUserProvisioner(httpClient, options, clock, logger);

        var first = await provisioner.ProvisionOrReuseAsync(
            new OidcProvisioningRequest(
                TenantCode: "cadence-local",
                Email: SyntheticEmail,
                GivenName: SyntheticGiven,
                FamilyName: SyntheticFamily,
                RealmRoles: ["Site Investigator"],
                StudyCodes: ["CADENCE-101"],
                SiteCode: "SITE-101"),
            ct);

        first.Outcome.Should().Be(OidcProvisioningOutcome.Created);
        first.PersonId.Should().Be(PersonId.FromGuid(createdSubjectId));
        first.WelcomeEmailSent.Should().BeTrue();

        var second = await provisioner.ProvisionOrReuseAsync(
            new OidcProvisioningRequest(
                TenantCode: "cadence-local",
                Email: SyntheticEmail,
                GivenName: SyntheticGiven,
                FamilyName: SyntheticFamily,
                RealmRoles: ["Site Investigator"],
                StudyCodes: ["CADENCE-101"],
                SiteCode: "SITE-101"),
            ct);

        second.Outcome.Should().Be(OidcProvisioningOutcome.ReusedExisting);
        second.PersonId.Should().Be(PersonId.FromGuid(createdSubjectId));
        second.WelcomeEmailSent.Should().BeFalse();

        // Token endpoint called once (cached across second call)
        requests.Count(r => r.PathAndQuery.Contains("/protocol/openid-connect/token", StringComparison.Ordinal)).Should().Be(1);

        // Keycloak 26 unmanagedAttributePolicy enabled once
        requests.Should().ContainSingle(r =>
            r.Method == HttpMethod.Put
            && r.PathAndQuery.EndsWith("/users/profile", StringComparison.Ordinal)
            && r.Body != null
            && r.Body.Contains("ENABLED", StringComparison.Ordinal));

        // Welcome actions email includes UPDATE_PASSWORD
        requests.Should().ContainSingle(r =>
            r.Method == HttpMethod.Put
            && r.PathAndQuery.Contains("/execute-actions-email", StringComparison.Ordinal)
            && r.Body != null
            && r.Body.Contains("UPDATE_PASSWORD", StringComparison.Ordinal));

        // Zero PII in logs
        foreach (string message in logger.Messages)
        {
            message.Should().NotContain(SyntheticEmail);
            message.Should().NotContain(SyntheticGiven);
            message.Should().NotContain(SyntheticFamily);
        }
    }

    [Fact]
    public async Task ProvisionOrReuseAsync_ExistingUserInDifferentTenant_ThrowsCrossTenantConflictWithoutMutatingUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var logger = new CapturingLogger<KeycloakOidcUserProvisioner>();
        var otherSubjectId = Guid.Parse("01950000-0000-7000-8000-000000000888"); // deid-ignore
        int mutations = 0;

        using var httpClient = new HttpClient(new StubHttpHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"access_token":"kc-admin-tok","expires_in":300}"""));
            }

            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.EndsWith("/users/profile", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"unmanagedAttributePolicy":"ENABLED"}"""));
            }

            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.EndsWith("/users", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    $$$"""[{"id":"{{{otherSubjectId:D}}}","username":"{{{SyntheticEmail}}}","email":"{{{SyntheticEmail}}}","enabled":true,"attributes":{"tenant_id":["other-tenant"]}}]"""));
            }

            mutations++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"));
        }))
        {
            BaseAddress = new Uri("http://keycloak.local"),
        };

        using var provisioner = new KeycloakOidcUserProvisioner(
            httpClient,
            new OrgKeycloakOptions
            {
                AdminBaseUrl = "http://keycloak.local",
                Realm = "cadence",
                ClientId = "cadence-admin",
                ClientSecret = "secret", // pragma: allowlist secret
            },
            clock,
            logger);

        var act = () => provisioner.ProvisionOrReuseAsync(
            new OidcProvisioningRequest("cadence-local", SyntheticEmail, "Sam", "Lee", ["Site Investigator"], ["CADENCE-101"], "SITE-101"),
            ct);

        var ex = await act.Should().ThrowAsync<OidcProvisioningException>();
        ex.Which.Error.Code.Should().Be(OrgErrors.CrossTenantIdentityConflict.Code);
        mutations.Should().Be(0, "another tenant's Keycloak user must never be modified");
    }

    [Fact]
    public void AddCadenceOrgModule_SharesKeycloakAdminConfigWithTrialOnboarding()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeycloakAdmin:BaseUrl"] = "http://kc-shared:8080",
                ["KeycloakAdmin:Realm"] = "cadence-shared",
                ["KeycloakAdmin:ClientId"] = "shared-admin",
                ["KeycloakAdmin:AdminUsername"] = "kc-admin",
                ["KeycloakAdmin:AdminPassword"] = "kc-secret", // pragma: allowlist secret
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FakeClock(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero)));
        services.AddCadenceOrgModule(config, new StubHostEnvironment { EnvironmentName = Environments.Production });

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<OrgKeycloakOptions>();
        opts.AdminBaseUrl.Should().Be("http://kc-shared:8080");
        opts.Realm.Should().Be("cadence-shared");
        opts.ClientId.Should().Be("shared-admin");
        opts.AdminUsername.Should().Be("kc-admin");
        opts.AdminPassword.Should().Be("kc-secret"); // pragma: allowlist secret
        provider.GetRequiredService<IOidcUserProvisioner>().Should().BeOfType<KeycloakOidcUserProvisioner>();
    }

    [Fact]
    public async Task DoaEndpoints_EmailDelegationAndRevocation_ForwardsToKernelBridgeAndPreservesExplanations()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase($"ctms_doa_bridge_{Guid.NewGuid():N}")
            .Options;
        await using var db = new CtmsDbContext(dbOptions);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 20, 14, 0, 0, TimeSpan.Zero));
        db.CtmsSiteGreenlightGates.Add(new CtmsSiteGreenlightGate("gate-site-101")
        {
            SiteId = "SITE-101",
            StudyId = "CADENCE-101",
            CreatedAt = clock.UtcNow.UtcDateTime,
            CreatedBy = "pi.site101",
            ReasonForChange = "Seed site gate",
        });
        await db.SaveChangesAsync(ct);

        var bridge = new RecordingKernelDelegationBridge();

        var services = new ServiceCollection();
        services.AddSingleton<IKernelDelegationBridge>(bridge);
        await using var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.Request.Headers["X-Change-Reason"] = "Delegate screening to site coordinator";

        var createdResult = await DoaEndpoints.DelegateTasksAsync(
            new DelegationTaskRequest(
                Site: "SITE-101",
                StaffUserId: null,
                TaskCodes: ["CRF_DATA_ENTRY", "SUBJECT_SCREENING"],
                StartDate: "2026-11-20",
                ReasonForChange: "Delegate screening to site coordinator",
                StaffEmail: SyntheticEmail,
                StaffName: "Site Coordinator"),
            db,
            httpContext,
            clock,
            ct);

        bridge.DelegatedInputs.Should().ContainSingle();
        var stored = await db.CtmsDelegations.SingleAsync(ct);
        stored.StaffUserId.Should().Be(bridge.ReturnedPersonId.ToString("D"));
        stored.KernelGrantIds.Should().Be(System.Text.Json.JsonSerializer.Serialize(bridge.ReturnedGrantIds));
        createdResult.Should().NotBeNull();

        // Revoking the CTMS delegation revokes the linked kernel grants
        await DoaEndpoints.RevokeDelegationAsync(
            new RevokeDelegationRequest(stored.Id, "End coordinator rotation"),
            db,
            httpContext,
            clock,
            ct);

        bridge.RevokedGrantBatches.Should().ContainSingle();
        bridge.RevokedGrantBatches[0].Should().BeEquivalentTo(bridge.ReturnedGrantIds);

        // Refusal preserves RFC 9457 structured explanation JSON verbatim
        bridge.NextFailureJson = """{"title":"Delegation exceeds held authority","status":422,"explanation":{"claim":"Capability not held"}}""";
        var refusalResult = await DoaEndpoints.DelegateTasksAsync(
            new DelegationTaskRequest(
                Site: "SITE-101",
                StaffUserId: null,
                TaskCodes: ["SAE_REPORTING"],
                StartDate: "2026-11-20",
                ReasonForChange: "Attempt unheld delegation",
                StaffEmail: SyntheticEmail),
            db,
            httpContext,
            clock,
            ct);

        var refusalContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        using var bodyStream = new System.IO.MemoryStream();
        refusalContext.Response.Body = bodyStream;
        await refusalResult.ExecuteAsync(refusalContext);
        refusalContext.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        string refusalBody = Encoding.UTF8.GetString(bodyStream.ToArray());
        refusalBody.Should().Contain("Capability not held");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Cadence.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class RecordingKernelDelegationBridge : IKernelDelegationBridge
    {
        public bool IsEnabled => true;
        public Guid ReturnedPersonId { get; } = Guid.Parse("01950000-0000-7000-8000-000000000901"); // deid-ignore
        public IReadOnlyList<Guid> ReturnedGrantIds { get; } =
        [
            Guid.Parse("01950000-0000-7000-8000-000000000902"), // deid-ignore
            Guid.Parse("01950000-0000-7000-8000-000000000903"), // deid-ignore
        ];
        public string? NextFailureJson { get; set; }
        public List<KernelDelegateDutiesInput> DelegatedInputs { get; } = [];
        public List<IReadOnlyList<Guid>> RevokedGrantBatches { get; } = [];

        public Task<KernelBridgeOutcome<KernelDelegatedStaffResult>> DelegateDutiesAsync(
            HttpContext callerContext,
            KernelDelegateDutiesInput input,
            string reasonForChange,
            CancellationToken ct)
        {
            if (NextFailureJson is not null)
            {
                string raw = NextFailureJson;
                NextFailureJson = null;
                return Task.FromResult(KernelBridgeOutcome<KernelDelegatedStaffResult>.Failure(
                    StatusCodes.Status422UnprocessableEntity,
                    "application/problem+json",
                    raw));
            }

            DelegatedInputs.Add(input);
            return Task.FromResult(KernelBridgeOutcome<KernelDelegatedStaffResult>.Success(
                new KernelDelegatedStaffResult(ReturnedPersonId, ReturnedGrantIds, false, true)));
        }

        public Task<KernelBridgeOutcome<bool>> RevokeGrantsAsync(
            HttpContext callerContext,
            IReadOnlyList<Guid> grantIds,
            string reasonForChange,
            CancellationToken ct)
        {
            RevokedGrantBatches.Add(grantIds);
            return Task.FromResult(KernelBridgeOutcome<bool>.Success(true));
        }
    }
}
