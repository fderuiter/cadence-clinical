using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
public sealed class KernelDevelopmentTenantPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Operator = "svc:kernel-development-bootstrap";

    // The Execution module's kernel event consumers (ADR-2622, #5461), granted by the demo data until #5566 provisions
    // module service accounts.
    private static readonly string[] ModuleConsumers = ["svc:execution-forms", "svc:execution-queries"];

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
        }

        await using (var connection = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await connection.OpenAsync(ct);
            auditAfterFirstStart = (long)(await ScalarAsync(connection, AuditCount, code))!;
            var grantees = ((string)(await ScalarAsync(
                connection, "SELECT string_agg(g.grantee_id, ',') FROM kernel.grants g JOIN kernel.tenants t ON t.id = g.tenant_id WHERE t.code = @code", code))!)
                .Split(',');
            grantees.Should().BeEquivalentTo(
                RealmUserIds().Append(Operator).Concat(ModuleConsumers),
                "the seeder grants itself, the Execution module's consumers (until #5566) and each realm user, by the fixed subject id in docker/cadence-realm.json");
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

    private const string CrcSubject = "1a4345cc-364c-4b8b-881e-93bbea528b0a";

    private const string AuditCount =
        "SELECT count(*) FROM kernel.audit_entries a JOIN kernel.tenants t ON t.id = a.tenant_id WHERE t.code = @code";

    /// <summary>The fixed subject ids of the users in <c>docker/cadence-realm.json</c>.</summary>
    private static IEnumerable<string> RealmUserIds()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker", "cadence-realm.json")))
        {
            directory = directory.Parent;
        }

        string path = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("docker/cadence-realm.json not found."), "docker", "cadence-realm.json");
        using var realm = JsonDocument.Parse(File.ReadAllText(path));
        return [.. realm.RootElement.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("id").GetString()!)];
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
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .UseSetting("Kernel:DevelopmentTenant:Code", code)
            .UseSetting("Kernel:DevelopmentTenant:DemoData", demoData ? "true" : "false"));
}
