using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Modules.Org.Contracts;
using Cadence.Modules.Org.Provisioning;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Org;

/// <summary>
/// End-to-end PostgreSQL integration verification of <c>Cadence.Modules.Org</c> hosted inside
/// <see cref="Cadence.Kernel.Host.Program"/> (ADR-2610, ADR-2611): OIDC user provisioning and welcome-email dispatch,
/// <c>IdentityVault</c> blind-index reuse without PII leakage, first-login study/site visibility, bounded delegation
/// and structured refusal explanations, audited identity disclosure, and bitemporal grant revocation.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OrgModulePostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Fixed demo subjects from DevelopmentDemoData
    private const string SponsorAdminSub = "8808e058-a472-4f72-9970-a027eddf35bc"; // deid-ignore
    private const string PiSite101Sub = "cf64a3b4-33cc-4925-8452-75e29ca9526e"; // deid-ignore
    private const string CrcSite101Sub = "1a4345cc-364c-4b8b-881e-93bbea528b0a"; // deid-ignore

    // Synthetic test emails (annotated for deid gate)
    private const string InvitedPiEmail = "invited.pi@example.com"; // deid-ignore: synthetic integration test email
    private const string DelegatedCrcEmail = "delegated.crc@example.com"; // deid-ignore: synthetic integration test email
    private const string BlockedEmail = "never.created@example.com"; // deid-ignore: synthetic integration test email
    private const string InvitedGiven = "Nadia";
    private const string InvitedFamily = "Rostova";

    [Fact(DisplayName = "InviteDelegateRevealAndRevoke_EndToEndAgainstRealPostgres_EnforcesAuthoritySealsIdentityAndEndsAccessImmediately")]
    public async Task InviteDelegateRevealAndRevokeEndToEndAgainstRealPostgresEnforcesAuthoritySealsIdentityAndEndsAccessImmediately()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string tenantCode = $"org-{Guid.NewGuid():N}"[..20];

        using var host = CreateHost(tenantCode);
        var provisioner = host.Services.GetRequiredService<InMemoryOidcUserProvisioner>();

        // 1. Team directory reads pseudonymous labels without disclosing PII or writing disclosure audits
        using var admin = host.CreateSignedClient(new GatewayIdentity(
            SponsorAdminSub,
            ["Sponsor Admin"],
            ["*"],
            TenantId: tenantCode,
            ChangeReason: "Onboard site investigator for SITE-101"));

        var initialTeam = await GetJsonAsync<OrgTeamViewResponse>(admin, "/api/v1/org/team", ct);
        initialTeam.CanInvite.Should().BeTrue();
        initialTeam.Members.Should().AllSatisfy(m =>
        {
            m.Email.Should().BeNull();
            m.DisplayName.Should().StartWith("Member ");
        });

        var site101Scope = initialTeam.Scopes.Single(s => s.Level == "StudySite" && s.Code == "SITE-101");
        var site102Scope = initialTeam.Scopes.Single(s => s.Level == "StudySite" && s.Code == "SITE-102");

        await using (var conn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await conn.OpenAsync(ct);
            (await ScalarLongAsync(
                conn,
                "SELECT count(*) FROM kernel.domain_events e JOIN kernel.tenants t ON t.id = e.tenant_id WHERE t.code = @code AND e.type ILIKE '%IdentityRevealed%'",
                tenantCode,
                ct)).Should().Be(0L);
        }

        // 2. Unauthorized caller (CRC without grant.issue) cannot invite workspace members -> 403 with structured explanation & zero OIDC accounts created
        using var crc = host.CreateSignedClient(new GatewayIdentity(
            CrcSite101Sub,
            ["Site Investigator"],
            ["CADENCE-101"],
            TenantId: tenantCode,
            ChangeReason: "Unauthorized seat attempt"));

        using var unauthInviteResp = await SendJsonAsync(
            crc,
            "/api/v1/org/invitations",
            new InviteMemberRequest(
                Email: BlockedEmail,
                FullName: "Blocked User",
                RoleCode: "DEMO_INVESTIGATOR",
                ScopeLevel: "StudySite",
                ScopeTargetId: site101Scope.TargetId,
                WithGrantOption: true),
            "invite-unauth-1",
            ct);
        unauthInviteResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await unauthInviteResp.Content.ReadAsStringAsync(ct)))
        {
            doc.RootElement.TryGetProperty("explanation", out var exp).Should().BeTrue();
            exp.GetProperty("claim").GetString().Should().NotBeNullOrWhiteSpace();
            exp.GetProperty("conditions").GetArrayLength().Should().BeGreaterThan(0);
        }

        provisioner.SentWelcomeEmails.Should().BeEmpty("OIDC user must never be created when kernel authority refuses");

        // 3. Sponsor Admin invites new colleague -> provisions OIDC account, sends welcome email, seals identity, issues grant
        using var inviteResp = await SendJsonAsync(
            admin,
            "/api/v1/org/invitations",
            new InviteMemberRequest(
                Email: InvitedPiEmail,
                FullName: $"{InvitedGiven} {InvitedFamily}",
                RoleCode: "DEMO_INVESTIGATOR",
                ScopeLevel: "StudySite",
                ScopeTargetId: site101Scope.TargetId,
                WithGrantOption: true),
            "invite-pi-1",
            ct);
        string inviteBody = await inviteResp.Content.ReadAsStringAsync(ct);
        inviteResp.StatusCode.Should().Be(HttpStatusCode.Created, inviteBody);
        var invitePayload = JsonSerializer.Deserialize<InviteMemberResponse>(inviteBody, WireJson)!;
        invitePayload.ReusedExistingAccount.Should().BeFalse();
        invitePayload.WelcomeEmailSent.Should().BeTrue();
        provisioner.SentWelcomeEmails.Should().ContainSingle(e => e.PersonId.Value == invitePayload.PersonId);

        // 4. Repeat invitation for same email (case-insensitive) reuses PersonId via IdentityVault blind index without duplicate OIDC user or welcome email
        using var repeatInviteResp = await SendJsonAsync(
            admin,
            "/api/v1/org/invitations",
            new InviteMemberRequest(
                Email: InvitedPiEmail.ToUpperInvariant(),
                FullName: $"{InvitedGiven} {InvitedFamily}",
                RoleCode: "DEMO_CRC",
                ScopeLevel: "StudySite",
                ScopeTargetId: site101Scope.TargetId,
                WithGrantOption: false),
            "invite-pi-2",
            ct);
        string repeatBody = await repeatInviteResp.Content.ReadAsStringAsync(ct);
        repeatInviteResp.StatusCode.Should().Be(HttpStatusCode.Created, repeatBody);
        var repeatPayload = JsonSerializer.Deserialize<InviteMemberResponse>(repeatBody, WireJson)!;
        repeatPayload.PersonId.Should().Be(invitePayload.PersonId);
        repeatPayload.ReusedExistingAccount.Should().BeTrue();
        repeatPayload.WelcomeEmailSent.Should().BeFalse();
        provisioner.SentWelcomeEmails.Should().HaveCount(1);

        // 5. Newly invited user signs in for the first time and immediately sees CADENCE-101 and holds SITE-101 capability
        string invitedSub = invitePayload.PersonId.ToString("D");
        using var invitedClient = host.CreateSignedClient(new GatewayIdentity(
            invitedSub,
            ["Site Investigator"],
            ["CADENCE-101"],
            TenantId: tenantCode));

        using var searchResp = await invitedClient.GetAsync(new Uri("/api/v1/kernel/search?q=CADENCE-101", UriKind.Relative), ct);
        searchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await searchResp.Content.ReadAsStringAsync(ct)).Should().Contain("CADENCE-101");

        using var canIResp = await invitedClient.GetAsync(
            new Uri($"/api/v1/kernel/authorization/can-i?capability=collection.sign&resource=study_site:{site101Scope.TargetId:D}", UriKind.Relative),
            ct);
        canIResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var canIDoc = JsonDocument.Parse(await canIResp.Content.ReadAsStringAsync(ct)))
        {
            canIDoc.RootElement.GetProperty("permitted").GetBoolean().Should().BeTrue();
        }

        // 6. PI at SITE-101 delegates subset of held duties (CRF_DATA_ENTRY, SUBJECT_INFORMED_CONSENT) -> succeeds with ParentGrantId
        using var pi = host.CreateSignedClient(new GatewayIdentity(
            PiSite101Sub,
            ["Site Investigator"],
            ["CADENCE-101"],
            TenantId: tenantCode,
            ChangeReason: "Delegate site data entry and consent duties"));

        using var delegateResp = await SendJsonAsync(
            pi,
            "/api/v1/org/delegations",
            new DelegateDutiesRequest(
                Email: DelegatedCrcEmail,
                FullName: "Delegated Coordinator",
                Duties: ["CRF_DATA_ENTRY", "SUBJECT_INFORMED_CONSENT"],
                StudyCode: "CADENCE-101",
                SiteCode: "SITE-101"),
            "delegate-crc-1",
            ct);
        string delegateBody = await delegateResp.Content.ReadAsStringAsync(ct);
        delegateResp.StatusCode.Should().Be(HttpStatusCode.Created, delegateBody);
        var delegatePayload = JsonSerializer.Deserialize<DelegateDutiesResponse>(delegateBody, WireJson)!;
        delegatePayload.GrantIds.Should().NotBeEmpty();

        // 7. PI attempting to delegate unheld capability (observation.lock) or sibling site (SITE-102) is refused with structured explanation
        using var unheldDelegateResp = await SendJsonAsync(
            pi,
            "/api/v1/org/delegations",
            new DelegateDutiesRequest(
                Email: DelegatedCrcEmail,
                FullName: "Delegated Coordinator",
                Capabilities: ["observation.lock"],
                StudyCode: "CADENCE-101",
                SiteCode: "SITE-101"),
            "delegate-unheld-1",
            ct);
        ((int)unheldDelegateResp.StatusCode).Should().BeOneOf(403, 422);
        using (var unheldDoc = JsonDocument.Parse(await unheldDelegateResp.Content.ReadAsStringAsync(ct)))
        {
            unheldDoc.RootElement.TryGetProperty("explanation", out var exp).Should().BeTrue();
            exp.GetProperty("conditions").GetArrayLength().Should().BeGreaterThan(0);
        }

        using var wrongSiteDelegateResp = await SendJsonAsync(
            pi,
            "/api/v1/org/delegations",
            new DelegateDutiesRequest(
                Email: DelegatedCrcEmail,
                FullName: "Delegated Coordinator",
                Duties: ["CRF_DATA_ENTRY"],
                StudyCode: "CADENCE-101",
                SiteCode: site102Scope.Code),
            "delegate-wrong-site-1",
            ct);
        ((int)wrongSiteDelegateResp.StatusCode).Should().BeOneOf(403, 422);

        // 8. Audited IdentityVault reveal writes immutable disclosure audit entry
        using var revealAdmin = host.CreateSignedClient(new GatewayIdentity(
            SponsorAdminSub,
            ["Sponsor Admin"],
            ["*"],
            TenantId: tenantCode,
            ChangeReason: "Regulatory inspection verification"));

        using var revealResp = await SendJsonAsync(
            revealAdmin,
            $"/api/v1/org/members/{invitePayload.PersonId:D}/reveal",
            new { },
            "reveal-invited-pi-1",
            ct);
        string revealBody = await revealResp.Content.ReadAsStringAsync(ct);
        revealResp.StatusCode.Should().Be(HttpStatusCode.OK, revealBody);
        var revealed = JsonSerializer.Deserialize<RevealMemberIdentityResponse>(revealBody, WireJson)!;
        revealed.Email.Should().Be(InvitedPiEmail);
        revealed.GivenName.Should().Be(InvitedGiven);
        revealed.FamilyName.Should().Be(InvitedFamily);

        // 9. Revoking grants immediately ends invitee access while preserving historical grant_periods and audit_entries
        using var revokeAdmin = host.CreateSignedClient(new GatewayIdentity(
            SponsorAdminSub,
            ["Sponsor Admin"],
            ["*"],
            TenantId: tenantCode,
            ChangeReason: "Investigator completed trial assignment"));

        foreach (Guid grantToRevoke in new[] { invitePayload.GrantId, repeatPayload.GrantId })
        {
            using var revokeResp = await SendJsonAsync(
                revokeAdmin,
                $"/api/v1/org/grants/{grantToRevoke:D}/revoke",
                new RevokeOrgGrantRequest(),
                $"revoke-{grantToRevoke:N}",
                ct);
            revokeResp.StatusCode.Should().Be(HttpStatusCode.OK, await revokeResp.Content.ReadAsStringAsync(ct));
        }

        using var canIAfterRevokeResp = await invitedClient.GetAsync(
            new Uri($"/api/v1/kernel/authorization/can-i?capability=collection.sign&resource=study_site:{site101Scope.TargetId:D}", UriKind.Relative),
            ct);
        canIAfterRevokeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var revokedDoc = JsonDocument.Parse(await canIAfterRevokeResp.Content.ReadAsStringAsync(ct)))
        {
            revokedDoc.RootElement.GetProperty("permitted").GetBoolean().Should().BeFalse();
        }

        using var searchAfterRevokeResp = await invitedClient.GetAsync(new Uri("/api/v1/kernel/search?q=CADENCE-101", UriKind.Relative), ct);
        searchAfterRevokeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await searchAfterRevokeResp.Content.ReadAsStringAsync(ct)).Should().Be("[]");

        // 10. Verify bitemporal grant_periods history, disclosure audit, and ZERO PII in kernel tables
        await using (var conn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await conn.OpenAsync(ct);
            await using var periodCmd = new NpgsqlCommand("SELECT count(*) FROM kernel.grant_periods WHERE grant_id = @gid", conn);
            periodCmd.Parameters.AddWithValue("gid", invitePayload.GrantId);
            ((long)(await periodCmd.ExecuteScalarAsync(ct))!).Should().BeGreaterThanOrEqualTo(1L);

            (await ScalarLongAsync(
                conn,
                "SELECT count(*) FROM kernel.audit_entries a JOIN kernel.tenants t ON t.id = a.tenant_id WHERE t.code = @code AND a.reason_for_change = 'Regulatory inspection verification'",
                tenantCode,
                ct)).Should().BeGreaterThanOrEqualTo(1L);

            (await ScalarLongAsync(
                conn,
                "SELECT count(*) FROM kernel.audit_entries a JOIN kernel.tenants t ON t.id = a.tenant_id WHERE t.code = @code AND (a.changes ILIKE '%invited.pi%' OR a.changes ILIKE '%delegated.crc%' OR a.changes ILIKE '%Rostova%')",
                tenantCode,
                ct)).Should().Be(0L, "email addresses and names must never appear in kernel audit entries");

            (await ScalarLongAsync(
                conn,
                "SELECT count(*) FROM kernel.domain_events e JOIN kernel.tenants t ON t.id = e.tenant_id WHERE t.code = @code AND (e.payload::text ILIKE '%invited.pi%' OR e.payload::text ILIKE '%delegated.crc%' OR e.payload::text ILIKE '%Rostova%')",
                tenantCode,
                ct)).Should().Be(0L, "email addresses and names must never appear in kernel domain event payloads");
        }
    }

    private WebApplicationFactory<Cadence.Kernel.Host.Program> CreateHost(string tenantCode) =>
        new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .UseSetting("Kernel:DevelopmentTenant:Code", tenantCode)
            .UseSetting("Kernel:DevelopmentTenant:DemoData", "true")
            .UseSetting("Org:InMemoryProvisioner", "true"));

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string path, CancellationToken ct)
    {
        using var res = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<T>(body, WireJson)!;
    }

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpClient client,
        string path,
        T payload,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(payload, options: WireJson),
        };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(req, ct);
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql, string code, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("code", code);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
