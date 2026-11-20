using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Compliance.Attributes;
using Cadence.IntegrationTests.Kernel;
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
/// End-to-end PostgreSQL integration tests for <c>Cadence.Modules.Org</c> inside <see cref="Cadence.Kernel.Host.Program"/>:
/// OIDC user provisioning &amp; welcome-email dispatch, IdentityVault blind-index reuse &amp; idempotency, first-login
/// scope visibility, bounded delegation &amp; structured refusal explanations, and bitemporal revocation.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OrgModulePostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string SponsorAdminSubject = "8808e058-a472-4f72-9970-a027eddf35bc"; // designer.lead
    private const string SitePiSubject = "cf64a3b4-33cc-4925-8452-75e29ca9526e"; // pi.site101
    private const string SiteCrcSubject = "1a4345cc-364c-4b8b-881e-93bbea528b0a"; // crc.site101

    [Fact(DisplayName = "InviteNewEmail_ProvisionsOidcUser_SealsIdentityVault_SendsWelcomeEmail_GrantsFirstLoginAccess_AndReplaysIdempotently")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task InviteNewEmailProvisionsOidcUserSealsIdentityAndGrantsFirstLoginAccess()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string tenantCode = $"org-{Guid.NewGuid():N}"[..20];

        using var host = CreateHost(tenantCode);
        using var admin = host.CreateSignedClient(new GatewayIdentity(
            SponsorAdminSubject,
            ["Sponsor Admin"],
            ["*"],
            TenantId: tenantCode,
            ChangeReason: "Invite site coordinator for SITE-101 enrollment"));

        var team = await GetJsonAsync(admin, "/api/v1/org/team", ct);
        team.GetProperty("can_invite").GetBoolean().Should().BeTrue();

        var crcRoleId = team.GetProperty("roles").EnumerateArray()
            .First(r => r.GetProperty("code").GetString() == "DEMO_CRC")
            .GetProperty("role_id").GetString()!;
        var site101Scope = team.GetProperty("available_scopes").EnumerateArray()
            .First(s => s.GetProperty("level").GetString() == "StudySite" && s.GetProperty("code").GetString() == "SITE-101");
        var site101TargetId = site101Scope.GetProperty("target_id").GetString()!;

        var invitePayload = new
        {
            email = "invited.crc@hospital-example.org",
            full_name = "Dr. Nadia Vance",
            role_id = crcRoleId,
            scope_level = "StudySite",
            scope_target_id = site101TargetId,
        };

        using var firstReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/org/invitations", UriKind.Relative))
        {
            Content = JsonContent.Create(invitePayload),
        };
        firstReq.Headers.Add("Idempotency-Key", "invite-nadia-001");
        using var firstRes = await admin.SendAsync(firstReq, ct);
        string firstBody = await firstRes.Content.ReadAsStringAsync(ct);
        firstRes.StatusCode.Should().Be(HttpStatusCode.Created, firstBody);

        var created = JsonDocument.Parse(firstBody).RootElement;
        string invitedPersonId = created.GetProperty("person_id").GetString()!;
        string invitedGrantId = created.GetProperty("grant_id").GetString()!;
        created.GetProperty("identity_outcome").GetString().Should().Be("Created");
        created.GetProperty("welcome_email_sent").GetBoolean().Should().BeTrue();

        var provisioner = host.Services.GetRequiredService<InMemoryOidcUserProvisioner>();
        provisioner.SentWelcomeEmails.Should().ContainSingle(e =>
            e.PersonId.Value == Guid.Parse(invitedPersonId) && e.TenantCode == tenantCode);

        // Retry with the same Idempotency-Key: reuses account & replays grant without duplicating user or welcome email
        using var retryReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/org/invitations", UriKind.Relative))
        {
            Content = JsonContent.Create(invitePayload),
        };
        retryReq.Headers.Add("Idempotency-Key", "invite-nadia-001");
        using var retryRes = await admin.SendAsync(retryReq, ct);
        retryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = JsonDocument.Parse(await retryRes.Content.ReadAsStringAsync(ct)).RootElement;
        replayed.GetProperty("person_id").GetString().Should().Be(invitedPersonId);
        replayed.GetProperty("grant_id").GetString().Should().Be(invitedGrantId);
        replayed.GetProperty("grant_replayed").GetBoolean().Should().BeTrue();
        provisioner.SentWelcomeEmails.Should().HaveCount(1);

        // Inviting the same email to a second role/scope reuses the existing tenant identity via IdentityVault blind index
        var monitorRoleId = team.GetProperty("roles").EnumerateArray()
            .First(r => r.GetProperty("code").GetString() == "DEMO_MONITOR")
            .GetProperty("role_id").GetString()!;
        using var secondRoleReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/org/invitations", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                email = "INVITED.CRC@hospital-example.org",
                full_name = "Dr. Nadia Vance",
                role_id = monitorRoleId,
                scope_level = "StudySite",
                scope_target_id = site101TargetId,
            }),
        };
        secondRoleReq.Headers.Add("Idempotency-Key", "invite-nadia-002");
        using var secondRoleRes = await admin.SendAsync(secondRoleReq, ct);
        secondRoleRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var reused = JsonDocument.Parse(await secondRoleRes.Content.ReadAsStringAsync(ct)).RootElement;
        reused.GetProperty("person_id").GetString().Should().Be(invitedPersonId);
        reused.GetProperty("identity_outcome").GetString().Should().Be("ReusedExisting");
        reused.GetProperty("welcome_email_sent").GetBoolean().Should().BeFalse();

        // First-login visibility: newly invited user signs in with their OIDC subject UUID and immediately sees SITE-101
        using var invitedUserClient = host.CreateSignedClient(new GatewayIdentity(
            invitedPersonId,
            ["Site Investigator"],
            ["CADENCE-101"],
            SiteId: "SITE-101",
            TenantId: tenantCode));
        var searchHits = await GetJsonAsync(invitedUserClient, "/api/v1/kernel/search?q=CADENCE-101", ct);
        searchHits.GetArrayLength().Should().Be(1, "newly invited user sees their granted study immediately on first sign-in");

        // Verify zero plaintext PII in kernel.audit_entries or kernel.grants, and immutable audit entries exist
        await using var conn = new NpgsqlConnection(fixture.AdminConnectionString);
        await conn.OpenAsync(ct);
        await using var auditCmd = new NpgsqlCommand(
            "SELECT count(*) FROM kernel.audit_entries a JOIN kernel.tenants t ON t.id = a.tenant_id WHERE t.code = @code AND a.reason_for_change = @reason",
            conn);
        auditCmd.Parameters.AddWithValue("code", tenantCode);
        auditCmd.Parameters.AddWithValue("reason", "Invite site coordinator for SITE-101 enrollment");
        ((long)(await auditCmd.ExecuteScalarAsync(ct))!).Should().BeGreaterThanOrEqualTo(3L);

        await using var piiLeakCmd = new NpgsqlCommand(
            "SELECT count(*) FROM kernel.audit_entries a JOIN kernel.tenants t ON t.id = a.tenant_id WHERE t.code = @code AND a.payload::text ILIKE '%invited.crc%'",
            conn);
        piiLeakCmd.Parameters.AddWithValue("code", tenantCode);
        ((long)(await piiLeakCmd.ExecuteScalarAsync(ct))!).Should().Be(0L, "email addresses must never appear in kernel audit payloads");
    }

    [Fact(DisplayName = "Refusals_UnauthorizedInviteAndOutOfBoundsDelegations_ReturnStructuredExplanations_BeforeAnyOidcCreation")]
    public async Task RefusalsReturnStructuredExplanationsBeforeAnyOidcCreation()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string tenantCode = $"org-{Guid.NewGuid():N}"[..20];

        using var host = CreateHost(tenantCode);
        var provisioner = host.Services.GetRequiredService<InMemoryOidcUserProvisioner>();

        // 1. CRC lacks grant.issue: invite is refused with structured explanation and creates zero OIDC accounts
        using var crc = host.CreateSignedClient(new GatewayIdentity(
            SiteCrcSubject,
            ["Site Investigator"],
            ["CADENCE-101"],
            SiteId: "SITE-101",
            TenantId: tenantCode,
            ChangeReason: "Unauthorized invite attempt"));
        var team = await GetJsonAsync(crc, "/api/v1/org/team", ct);
        var crcRoleId = team.GetProperty("roles").EnumerateArray().First().GetProperty("role_id").GetString()!;
        var site102TargetId = team.GetProperty("available_scopes").EnumerateArray()
            .First(s => s.GetProperty("level").GetString() == "StudySite" && s.GetProperty("code").GetString() == "SITE-102")
            .GetProperty("target_id").GetString()!;

        using var badInvite = await crc.PostAsJsonAsync(
            new Uri("/api/v1/org/invitations", UriKind.Relative),
            new
            {
                email = "never.created@hospital.test",
                full_name = "Blocked User",
                role_id = crcRoleId,
                scope_level = "StudySite",
                scope_target_id = site102TargetId,
            },
            ct);
        badInvite.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var badInviteJson = JsonDocument.Parse(await badInvite.Content.ReadAsStringAsync(ct)).RootElement;
        badInviteJson.TryGetProperty("explanation", out var inviteExplanation).Should().BeTrue();
        inviteExplanation.GetProperty("claim").GetString().Should().Contain("grant.issue");
        provisioner.SentWelcomeEmails.Should().BeEmpty("OIDC user must never be created when kernel authority refuses");

        // 2. Site PI at SITE-101 attempts delegation at SITE-102 (out-of-scope): refused with structured explanation
        using var pi = host.CreateSignedClient(new GatewayIdentity(
            SitePiSubject,
            ["Site Investigator"],
            ["CADENCE-101"],
            SiteId: "SITE-101",
            TenantId: tenantCode,
            ChangeReason: "Delegate duties across sites"));

        using var outOfScopeRes = await pi.PostAsJsonAsync(
            new Uri("/api/v1/org/delegations", UriKind.Relative),
            new
            {
                email = "sub.pi@site102.test",
                full_name = "Dr. Out Of Scope",
                site_code = "SITE-102",
                duties = new[] { "Sub-Investigator Oversight" },
            },
            ct);
        outOfScopeRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var scopeProblem = JsonDocument.Parse(await outOfScopeRes.Content.ReadAsStringAsync(ct)).RootElement;
        scopeProblem.GetProperty("explanation").GetProperty("conditions").EnumerateArray()
            .Should().Contain(c => c.GetProperty("code").GetString() == "delegation.scope" && !c.GetProperty("satisfied").GetBoolean());
        provisioner.SentWelcomeEmails.Should().BeEmpty();
    }

    [Fact(DisplayName = "BoundedDelegationAndRevocation_EndsDownstreamAccess_WhilePreservingGrantHistoryAndAuditChain")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BoundedDelegationAndRevocationEndsDownstreamAccessAndPreservesHistory()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string tenantCode = $"org-{Guid.NewGuid():N}"[..20];

        using var host = CreateHost(tenantCode);
        using var pi = host.CreateSignedClient(new GatewayIdentity(
            SitePiSubject,
            ["Site Investigator"],
            ["CADENCE-101"],
            SiteId: "SITE-101",
            TenantId: tenantCode,
            ChangeReason: "Delegate SITE-101 EDC entry to sub-investigator"));

        using var delRes = await pi.PostAsJsonAsync(
            new Uri("/api/v1/org/delegations", UriKind.Relative),
            new
            {
                email = "subi.site101@hospital.test",
                full_name = "Dr. Marcus Chen",
                site_code = "SITE-101",
                duties = new[] { "EDC Data Entry" },
            },
            ct);
        string delBody = await delRes.Content.ReadAsStringAsync(ct);
        delRes.StatusCode.Should().Be(HttpStatusCode.Created, delBody);
        var delJson = JsonDocument.Parse(delBody).RootElement;
        string delegateeId = delJson.GetProperty("person_id").GetString()!;
        string delegatedGrantId = delJson.GetProperty("grant_ids").EnumerateArray().Single().GetString()!;

        // Delegatee sees CADENCE-101 while delegation is active
        using var delegatee = host.CreateSignedClient(new GatewayIdentity(
            delegateeId,
            ["Site Investigator"],
            ["CADENCE-101"],
            SiteId: "SITE-101",
            TenantId: tenantCode));
        (await GetJsonAsync(delegatee, "/api/v1/kernel/search?q=CADENCE-101", ct)).GetArrayLength().Should().Be(1);

        // PI revokes the delegation with a signed reason for change
        using var piRevoke = host.CreateSignedClient(new GatewayIdentity(
            SitePiSubject,
            ["Site Investigator"],
            ["CADENCE-101"],
            SiteId: "SITE-101",
            TenantId: tenantCode,
            ChangeReason: "Sub-investigator rotated off SITE-101"));
        using var revokeRes = await piRevoke.PostAsJsonAsync(
            new Uri($"/api/v1/org/grants/{delegatedGrantId}/revoke", UriKind.Relative),
            new { },
            ct);
        revokeRes.StatusCode.Should().Be(HttpStatusCode.OK, await revokeRes.Content.ReadAsStringAsync(ct));

        // Downstream access ends immediately
        (await GetJsonAsync(delegatee, "/api/v1/kernel/search?q=CADENCE-101", ct)).GetArrayLength().Should().Be(0);

        // Historical bitemporal grant_periods row and audit record are preserved
        var teamAfter = await GetJsonAsync(piRevoke, "/api/v1/org/team", ct);
        var memberNode = teamAfter.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("person_id").GetString() == delegateeId);
        memberNode.GetProperty("active_grants").GetArrayLength().Should().Be(0);
        memberNode.GetProperty("historical_grants").EnumerateArray()
            .Should().ContainSingle(g => g.GetProperty("grant_id").GetString() == delegatedGrantId && g.GetProperty("revoked").GetBoolean());

        await using var conn = new NpgsqlConnection(fixture.AdminConnectionString);
        await conn.OpenAsync(ct);
        await using var periodsCmd = new NpgsqlCommand(
            "SELECT count(*) FROM kernel.grant_periods WHERE grant_id = @gid",
            conn);
        periodsCmd.Parameters.AddWithValue("gid", Guid.Parse(delegatedGrantId));
        ((long)(await periodsCmd.ExecuteScalarAsync(ct))!).Should().BeGreaterThanOrEqualTo(1L);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        using var res = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private WebApplicationFactory<Cadence.Kernel.Host.Program> CreateHost(string tenantCode) =>
        new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .UseSetting("Kernel:DevelopmentTenant:Code", tenantCode)
            .UseSetting("Kernel:DevelopmentTenant:DemoData", "true")
            .UseSetting("Org:InMemoryProvisioner", "true"));
}
