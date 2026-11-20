using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Time;
using Cadence.Modules.Org;
using Cadence.Modules.Org.Provisioning;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.UnitTests.Org;

public sealed class KeycloakOidcUserProvisionerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "ProvisionOrReuse_NewEmail_CreatesKeycloakUser_BindsTenantAttributes_AndSendsWelcomeEmail")]
    public async Task ProvisionOrReuseNewEmailCreatesKeycloakUserAndSendsWelcomeEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var createdUserId = Guid.Parse("01950000-1111-7000-8000-000000000042");
        var requests = new List<(HttpMethod Method, string Path, string? Body)>();

        using var http = new HttpClient(new StubHandler(async req =>
        {
            string? body = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            requests.Add((req.Method, req.RequestUri!.PathAndQuery, body));

            if (req.RequestUri.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, new { access_token = "adm-token", expires_in = 300 });
            }

            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.EndsWith("/admin/realms/cadence/users", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Array.Empty<object>());
            }

            if (req.Method == HttpMethod.Post && req.RequestUri.AbsolutePath.EndsWith("/admin/realms/cadence/users", StringComparison.Ordinal))
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Created);
                resp.Headers.Location = new Uri($"http://keycloak.local/admin/realms/cadence/users/{createdUserId:D}");
                return resp;
            }

            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.Contains("/roles/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, new { id = "role-1", name = "Site Investigator" });
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }))
        {
            BaseAddress = new Uri("http://keycloak.local"),
        };

        var options = new KeycloakAdminOptions
        {
            AdminBaseUrl = "http://keycloak.local",
            Realm = "cadence",
            ClientId = "cadence-admin",
            ClientSecret = "secret",
        };

        var provisioner = new KeycloakOidcUserProvisioner(
            http,
            options,
            new FixedClock(FixedNow),
            NullLogger<KeycloakOidcUserProvisioner>.Instance);

        var result = await provisioner.ProvisionOrReuseAsync(
            new OidcProvisioningRequest(
                TenantCode: "tenant-alpha",
                Email: "new.pi@site101.test",
                GivenName: "Elena",
                FamilyName: "Rostova",
                RealmRoles: ["Site Investigator"],
                StudyCodes: ["CADENCE-101"],
                SiteCode: "SITE-101"),
            ct);

        result.Outcome.Should().Be(OidcProvisioningOutcome.Created);
        result.WelcomeEmailSent.Should().BeTrue();
        result.PersonId.Should().Be(PersonId.FromGuid(createdUserId));

        requests.Should().Contain(r =>
            r.Method == HttpMethod.Post
            && r.Path == "/admin/realms/cadence/users"
            && r.Body != null
            && r.Body.Contains(""tenant_id":["tenant - alpha"]", StringComparison.Ordinal)
            && r.Body.Contains(""SITE - 101"", StringComparison.Ordinal));
        requests.Should().Contain(r =>
            r.Method == HttpMethod.Put
            && r.Path.Contains($"/admin/realms/cadence/users/{createdUserId:D}/execute-actions-email", StringComparison.Ordinal)
            && r.Body != null
            && r.Body.Contains("UPDATE_PASSWORD", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ProvisionOrReuse_ExistingEmailInOtherTenant_ThrowsCrossTenantConflict_WithoutMutatingUser")]
    public async Task ProvisionOrReuseExistingEmailInOtherTenantThrowsCrossTenantConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var otherUserId = Guid.Parse("01950000-2222-7000-8000-000000000099");
        var mutations = 0;

        using var http = new HttpClient(new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, new { access_token = "adm-token", expires_in = 300 }));
            }

            if (req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath.EndsWith("/admin/realms/cadence/users", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, new object[]
                {
                    new
                    {
                        id = otherUserId.ToString("D"),
                        username = "existing@other.test",
                        email = "existing@other.test",
                        enabled = true,
                        attributes = new Dictionary<string, string[]> { ["tenant_id"] = ["tenant-bravo"] },
                    },
                }));
            }

            mutations++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }))
        {
            BaseAddress = new Uri("http://keycloak.local"),
        };

        var provisioner = new KeycloakOidcUserProvisioner(
            http,
            new KeycloakAdminOptions { AdminBaseUrl = "http://keycloak.local", Realm = "cadence", ClientId = "adm", ClientSecret = "sec" },
            new FixedClock(FixedNow),
            NullLogger<KeycloakOidcUserProvisioner>.Instance);

        var act = () => provisioner.ProvisionOrReuseAsync(
            new OidcProvisioningRequest("tenant-alpha", "existing@other.test", "Sam", "Lee", ["Site Investigator"], ["CADENCE-101"], "SITE-101"),
            ct);

        var ex = await act.Should().ThrowAsync<OidcProvisioningException>();
        ex.Which.Error.Code.Should().Be(OrgErrors.CrossTenantIdentityConflict.Code);
        mutations.Should().Be(0, "another tenant's Keycloak user must never be modified");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object payload) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request);
    }
}
