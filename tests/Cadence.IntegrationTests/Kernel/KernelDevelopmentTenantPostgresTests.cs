using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Execution;
using Cadence.Kernel.Commands;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The development tenant bootstrap (ADR-2542, #5430) against PostgreSQL: with <c>Kernel:DevelopmentTenant:Code</c> set,
/// the real kernel host, connected as the application role, provisions the tenant on start through the command
/// pipeline, so a caller signed in with that tenant code is served instead of refused. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelDevelopmentTenantPostgresTests(ExecutionFormModulePostgresFixture fixture) : IClassFixture<ExecutionFormModulePostgresFixture>
{
    private const string Operator = "svc:kernel-development-bootstrap";

    // The Execution module's kernel event consumers and SLA scheduler service account (ADR-2622, ADR-2625, ADR-2626, #5461),
    // granted by the demo data until #5566 provisions module service accounts.
    private static readonly string[] ModuleConsumers =
    [
        "svc:execution-forms",
        "svc:execution-queries",
        "svc:execution-edit-checks",
        "svc:execution-query-sla",
        "svc:execution-sdv-alerts",
    ];

    /// <summary>
    /// The first start provisions the tenant with an audit entry naming the bootstrap's service account, and a caller
    /// with that tenant code is served (a kernel search, where a reader with no grant gets no hits); a second start finds the tenant and provisions nothing more.
    /// </summary>
    [Fact(DisplayName = "DevelopmentTenant_IsProvisionedOnStart_ThroughThePipeline_Once")]
    public async Task DevelopmentTenantIsProvisionedOnStartThroughThePipelineOnce()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string code = $"dev-{Guid.NewGuid():N}"[..20];

        using (var first = Host(code))
        {
            using var client = first.CreateSignedClient(new GatewayIdentity("crc@dev-tenant.test", [], [], TenantId: code));
            using var response = await client.GetAsync(new Uri("/api/v1/kernel/search?q=DEMO", UriKind.Relative), ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        }

        using (var second = Host(code))
        {
            using var client = second.CreateSignedClient(new GatewayIdentity("crc@dev-tenant.test", [], [], TenantId: code));
            using var response = await client.GetAsync(new Uri("/api/v1/kernel/search?q=DEMO", UriKind.Relative), ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        (await ScalarAsync(connection, "SELECT count(*) FROM kernel.tenants WHERE code = @code AND is_active", code))
            .Should().Be(1L, "the second start finds the tenant and provisions no other");
        (await ScalarAsync(connection, "SELECT created_by FROM kernel.tenants WHERE code = @code", code)).Should().Be(Operator);
        (await ScalarAsync(
                connection,
                "SELECT count(*) FROM kernel.audit_entries a JOIN kernel.tenants t ON t.id = a.tenant_id WHERE t.code = @code AND a.actor_id = @actor",
                code))
            .Should().Be(1L, "provisioning wrote one audit entry, as the bootstrap's service account");
    }

    /// <summary>A caller signed in with a tenant code no one provisioned is still refused (ADR-2514).</summary>
    [Fact(DisplayName = "OtherTenantCodes_AreStillRefused")]
    public async Task OtherTenantCodesAreStillRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        using var host = Host($"dev-{Guid.NewGuid():N}"[..20]);
        using var client = host.CreateSignedClient(new GatewayIdentity("crc@dev-tenant.test", [], [], TenantId: "never-provisioned"));
        using var response = await client.GetAsync(new Uri("/api/v1/kernel/search?q=DEMO", UriKind.Relative), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Actor.ServiceAccount("kernel-development-bootstrap").Id.Should().Be(Operator);
    }

    /// <summary>
    /// With demo data on (ADR-2543, #5452), the bootstrap writes the demo study through the pipeline and grants the realm's
    /// users by their fixed subject ids: the site coordinator sees the study, their own study site and its participants,
    /// while the other study site stays masked; a person with no grant finds nothing. A second start replays every
    /// command and writes nothing more.
    /// </summary>
    [Fact(DisplayName = "DemoData_GrantsTheCoordinatorTheirSiteOnly_AndReplaysOnRestart")]
    public async Task DemoDataGrantsTheCoordinatorTheirSiteOnlyAndReplaysOnRestart()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string code = $"dev-{Guid.NewGuid():N}"[..20];
        long auditAfterFirstStart;

        using (var first = Host(code, demoData: true))
        {
            using var crc = first.CreateSignedClient(new GatewayIdentity(CrcSubject, [], [], TenantId: code));
            var study = (await GetAsync(crc, "/api/v1/kernel/search?q=CADENCE-101", ct)).EnumerateArray()
                .Should().ContainSingle(h => h.GetProperty("code").GetString() == "CADENCE-101").Subject.GetProperty("ref").GetString()!;

            var studySites = (await GetAsync(crc, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(study)}/children", ct)).EnumerateArray()
                .Select(c => c.GetProperty("node")).Where(n => n.GetProperty("kind").GetString() == "study_site").ToList();
            studySites.Should().HaveCount(2);
            var own = studySites.Should().ContainSingle(n => n.GetProperty("mask").ValueKind == JsonValueKind.Null).Subject;
            studySites.Should().ContainSingle(n => n.GetProperty("mask").GetString() == "NotPermitted", "the other site is not the coordinator's");

            var participants = (await GetAsync(crc, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(own.GetProperty("ref").GetString()!)}/children", ct)).EnumerateArray()
                .Select(c => c.GetProperty("node")).ToList();
            participants.Select(n => n.GetProperty("title").GetString()).Should().BeEquivalentTo("SUBJ-101-001", "SUBJ-101-002");

            using var stranger = first.CreateSignedClient(new GatewayIdentity("stranger@dev-tenant.test", [], [], TenantId: code));
            (await GetAsync(stranger, "/api/v1/kernel/search?q=CADENCE-101", ct)).GetArrayLength().Should().Be(0, "a person with no grant sees nothing");

            using var subjectParticipant = first.CreateSignedClient(new GatewayIdentity(SubjectParticipantId, ["Subject"], [], TenantId: code));
            (await GetAsync(subjectParticipant, "/api/v1/kernel/search?q=CADENCE-101", ct)).GetArrayLength().Should().Be(0, "a participant account holds no staff kernel grant (K027)");
        }

        await using (var connection = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await connection.OpenAsync(ct);
            auditAfterFirstStart = (long)(await ScalarAsync(connection, AuditCount, code))!;
            var grantees = ((string)(await ScalarAsync(
                connection, "SELECT string_agg(g.grantee_id, ',') FROM kernel.grants g JOIN kernel.tenants t ON t.id = g.tenant_id WHERE t.code = @code", code))!)
                .Split(',');
            grantees.Should().BeEquivalentTo(
                StaffRealmUserIds().Append(Operator).Concat(ModuleConsumers),
                "the seeder grants itself, the Execution module's consumers (until #5566) and each staff realm user, by the fixed subject id in docker/cadence-realm.json");
        }

        using (var second = Host(code, demoData: true))
        {
            using var crc = second.CreateSignedClient(new GatewayIdentity(CrcSubject, [], [], TenantId: code));
            (await GetAsync(crc, "/api/v1/kernel/search?q=CADENCE-101", ct)).GetArrayLength().Should().Be(1);
        }

        await using (var connection = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await connection.OpenAsync(ct);
            (await ScalarAsync(connection, AuditCount, code)).Should().Be(auditAfterFirstStart, "the second start replays every command");
        }
    }

    /// <summary>
    /// Demo data seeds an approved and effective protocol version for <c>CADENCE-101</c> with scheduled and unscheduled
    /// collection specifications, activates <c>SITE-101</c> and <c>SITE-102</c> so forms open without
    /// <c>K.Collection.NoGoverningVersion</c>, and exposes <c>POST /api/v1/kernel/enrollments/screen</c> under site grants.
    /// </summary>
    [Fact(DisplayName = "DemoData_SeedsEffectiveSchedule_OpensForms_AndScreensSyncedParticipantUnderSiteGrants")]
    public async Task DemoDataSeedsEffectiveScheduleOpensFormsAndScreensSyncedParticipantUnderSiteGrants()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string code = $"dev-{Guid.NewGuid():N}"[..20];
        var collectedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        using var host = Host(code, demoData: true);
        using var crc = host.CreateSignedClient(new GatewayIdentity(CrcSubject, [], [], TenantId: code));

        var studyRef = (await GetAsync(crc, "/api/v1/kernel/search?q=CADENCE-101", ct)).EnumerateArray()
            .Single(h => h.GetProperty("code").GetString() == "CADENCE-101").GetProperty("ref").GetString()!;

        var versions = (await GetAsync(crc, $"/api/v1/kernel/studies/{Uri.EscapeDataString(studyRef)}/protocol-versions", ct)).EnumerateArray()
            .Where(v => v.GetProperty("mask").ValueKind == JsonValueKind.Null && v.GetProperty("status").GetString() == "Approved")
            .ToList();
        var versionRef = versions.Should().ContainSingle().Subject.GetProperty("ref").GetString()!;

        var soa = await GetAsync(crc, $"/api/v1/kernel/protocol-versions/{Uri.EscapeDataString(versionRef)}/schedule-of-activities", ct);
        soa.GetProperty("epochs").GetArrayLength().Should().Be(3);
        soa.GetProperty("columns").GetArrayLength().Should().Be(5);
        var rows = soa.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(4);
        rows.Count(r => r.GetProperty("frequency").GetInt32() > 0).Should().Be(2);
        rows.Count(r => r.GetProperty("frequency").GetInt32() == 0).Should().Be(2);

        var ownSiteRef = (await GetAsync(crc, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(studyRef)}/children", ct)).EnumerateArray()
            .Select(c => c.GetProperty("node"))
            .Single(n => n.GetProperty("kind").GetString() == "study_site" && n.GetProperty("mask").ValueKind == JsonValueKind.Null)
            .GetProperty("ref").GetString()!;

        var enrollmentRef = (await GetAsync(crc, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(ownSiteRef)}/children", ct)).EnumerateArray()
            .Select(c => c.GetProperty("node"))
            .Single(n => n.GetProperty("kind").GetString() == "enrollment" && n.GetProperty("code").GetString() == "SUBJ-101-001")
            .GetProperty("ref").GetString()!;
        var enrollmentId = Cadence.Kernel.References.NodeRef.Parse(enrollmentRef).Id;

        using (var openScheduled = SendJsonRequest(HttpMethod.Post, "/api/v1/execution/forms", "Screening vital signs", new
        {
            enrollment_id = enrollmentId,
            collection_spec_id = "cs.vitals",
            planned_activity_id = "act.screening",
            collected_at = collectedAt,
        }))
        using (var scheduledResponse = await crc.SendAsync(openScheduled, ct))
        {
            string body = await scheduledResponse.Content.ReadAsStringAsync(ct);
            scheduledResponse.StatusCode.Should().Be(HttpStatusCode.Created, body);
        }

        using (var openUnscheduled = SendJsonRequest(HttpMethod.Post, "/api/v1/execution/forms", "Unscheduled adverse event log", new
        {
            enrollment_id = enrollmentId,
            collection_spec_id = "cs.adverse_event",
            planned_activity_id = (string?)null,
            collected_at = collectedAt.AddHours(1),
        }))
        using (var unscheduledResponse = await crc.SendAsync(openUnscheduled, ct))
        {
            string body = await unscheduledResponse.Content.ReadAsStringAsync(ct);
            unscheduledResponse.StatusCode.Should().Be(HttpStatusCode.Created, body);
        }

        using (var screenOwnSite = SendJsonRequest(HttpMethod.Post, "/api/v1/kernel/enrollments/screen", "Participant registered in Clinical Canvas", new
        {
            study_code = "CADENCE-101",
            site_code = "SITE-101",
            subject_code = "SUBJ-101-099",
            screened_at = collectedAt,
        }))
        using (var createdEnrollment = await crc.SendAsync(screenOwnSite, ct))
        {
            string body = await createdEnrollment.Content.ReadAsStringAsync(ct);
            createdEnrollment.StatusCode.Should().Be(HttpStatusCode.Created, body);
        }

        var syncedChildren = (await GetAsync(crc, $"/api/v1/kernel/nodes/{Uri.EscapeDataString(ownSiteRef)}/children", ct)).EnumerateArray()
            .Select(c => c.GetProperty("node"))
            .ToList();
        syncedChildren.Should().ContainSingle(n => n.GetProperty("kind").GetString() == "enrollment" && n.GetProperty("code").GetString() == "SUBJ-101-099");

        using var screenOtherSite = SendJsonRequest(HttpMethod.Post, "/api/v1/kernel/enrollments/screen", "Cross-site screening attempt", new
        {
            study_code = "CADENCE-101",
            site_code = "SITE-102",
            subject_code = "SUBJ-102-099",
            screened_at = collectedAt,
        });
        using var deniedEnrollment = await crc.SendAsync(screenOtherSite, ct);
        deniedEnrollment.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // dm.lead holds DEMO_DATA_MANAGER at AuthorityScope.Study (without tenant-wide attestation.sign) and applies + releases a signed lock.
        string unversionedStudyRef = studyRef.Split('@')[0];
        using var dm = host.CreateSignedClient(new GatewayIdentity(DataManagerSubject, [], [], TenantId: code));
        var applyLockReq = SendJsonRequest(HttpMethod.Post, "/api/v1/kernel/locks", "Demo interim review freeze", new
        {
            study_ref = unversionedStudyRef,
            scope_ref = enrollmentRef.Split('@')[0],
            kind = "Frozen",
            meaning = "Approved",
        });
        applyLockReq.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(
                DataManagerSubject,
                Cadence.Kernel.Api.KernelLockEndpoints.ApplyLockAction,
                Cadence.Kernel.Api.KernelLockEndpoints.ApplyLockSemanticAction));
        string lockRef;
        using (var appliedLock = await dm.SendAsync(applyLockReq, ct))
        {
            string body = await appliedLock.Content.ReadAsStringAsync(ct);
            appliedLock.StatusCode.Should().Be(HttpStatusCode.Created, body);
            lockRef = JsonDocument.Parse(body).RootElement.GetProperty("ref").GetString()!;
        }

        var releaseLockReq = SendJsonRequest(HttpMethod.Post, $"/api/v1/kernel/locks/{lockRef}/release", "Demo interim review complete", new
        {
            study_ref = unversionedStudyRef,
            meaning = "Responsibility",
        });
        releaseLockReq.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(
                DataManagerSubject,
                Cadence.Kernel.Api.KernelLockEndpoints.ReleaseLockAction(lockRef),
                Cadence.Kernel.Api.KernelLockEndpoints.ReleaseLockSemanticAction));
        using (var releasedLock = await dm.SendAsync(releaseLockReq, ct))
        {
            string body = await releasedLock.Content.ReadAsStringAsync(ct);
            releasedLock.StatusCode.Should().Be(HttpStatusCode.OK, body);
            JsonDocument.Parse(body).RootElement.GetProperty("released").GetBoolean().Should().BeTrue();
        }
    }

    /// <summary>
    /// Demo data registers tenant-scoped <c>Person</c> and <c>TenantMembership</c> records for each staff realm user so
    /// multiple demo tenants coexist under PostgreSQL Row-Level Security without colliding on <c>kernel.people</c>, and
    /// <c>GET /api/v1/kernel/entitlements</c> serves enrolled staff while refusing unenrolled callers and participants.
    /// </summary>
    [Fact(DisplayName = "DemoData_EnrollsStaffMemberships_ServesEntitlements_AndIsolatesAcrossDemoTenants")]
    public async Task DemoDataEnrollsStaffMembershipsServesEntitlementsAndIsolatesAcrossDemoTenants()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string firstCode = $"dev-{Guid.NewGuid():N}"[..20];
        string secondCode = $"dev-{Guid.NewGuid():N}"[..20];

        using var firstHost = Host(firstCode, demoData: true);
        using var secondHost = Host(secondCode, demoData: true);

        foreach (var (host, code) in new[] { (firstHost, firstCode), (secondHost, secondCode) })
        {
            using var crc = host.CreateSignedClient(new GatewayIdentity(CrcSubject, [], [], TenantId: code));
            var entitlements = await GetAsync(crc, "/api/v1/kernel/entitlements", ct);
            entitlements.GetProperty("tenant_ref").GetString().Should().StartWith("tenant:");

            using var stranger = host.CreateSignedClient(new GatewayIdentity("stranger@dev-tenant.test", [], [], TenantId: code));
            using var strangerEntitlements = await stranger.GetAsync(new Uri("/api/v1/kernel/entitlements", UriKind.Relative), ct);
            strangerEntitlements.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using var participant = host.CreateSignedClient(new GatewayIdentity(SubjectParticipantId, ["Subject"], [], TenantId: code));
            using var participantEntitlements = await participant.GetAsync(new Uri("/api/v1/kernel/entitlements", UriKind.Relative), ct);
            participantEntitlements.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    private static HttpRequestMessage SendJsonRequest(HttpMethod method, string path, string reason, object payload)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(payload),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        return request;
    }

    private const string CrcSubject = "1a4345cc-364c-4b8b-881e-93bbea528b0a";
    private const string DataManagerSubject = "47a9d88f-73d7-4aca-9fd6-14b8e6e9284f";
    private const string SubjectParticipantId = "7a8b9c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d";

    private const string AuditCount =
        "SELECT count(*) FROM kernel.audit_entries a JOIN kernel.tenants t ON t.id = a.tenant_id WHERE t.code = @code";

    /// <summary>The fixed subject ids of the staff users in <c>docker/cadence-realm.json</c> (participants hold no staff kernel grants per K027).</summary>
    private static IEnumerable<string> StaffRealmUserIds()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker", "cadence-realm.json")))
        {
            directory = directory.Parent;
        }

        string path = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("docker/cadence-realm.json not found."), "docker", "cadence-realm.json");
        using var realm = JsonDocument.Parse(File.ReadAllText(path));
        return [.. realm.RootElement.GetProperty("users").EnumerateArray()
            .Where(u => u.GetProperty("realmRoles").EnumerateArray().Any(r => r.GetString() != "Subject"))
            .Select(u => u.GetProperty("id").GetString()!)];
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, string code)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("actor", Operator);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private WebApplicationFactory<Cadence.Kernel.Host.Program> Host(string code, bool demoData = false) =>
        new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .UseSetting("Kernel:DevelopmentTenant:Code", code)
            .UseSetting("Kernel:DevelopmentTenant:DemoData", demoData ? "true" : "false"));
}
