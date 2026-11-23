using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Database.Tenancy;
using Cadence.Kernel;
using Cadence.Kernel.Host.Onboarding;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Database;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// End-to-end PostgreSQL verification across the real Clinical Kernel host and Execution host sharing a single
/// <c>NOBYPASSRLS</c> PostgreSQL database:
/// <list type="bullet">
/// <item>Self-service trial founders receive <c>Trial Coordinator</c>, register <c>SiteInstitution</c> sites, link them
/// to <c>CADENCE-101</c>, and screen <c>SUBJ-001</c> via <c>POST /api/v1/execution/subjects</c> with live
/// <see cref="HttpKernelEnrollmentSynchronizer"/> synchronization.</item>
/// <item>Two separate trial tenants both create <c>CADENCE-101</c> and enroll <c>SUBJ-001</c> at enrollment index 0
/// without PostgreSQL <c>23505</c> unique-constraint collisions and with strict RLS isolation.</item>
/// <item>Non-trial <c>Sponsor Admin</c> (<c>sponsor_dm</c>) callers and <c>Trial Coordinator</c> callers outside
/// verified <c>trial-&lt;12hex&gt;</c> tenants receive <c>403 Forbidden</c> on <c>POST /api/v1/execution/subjects</c>.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class TrialSubjectWorkflowPostgresTests : IAsyncLifetime
{
    private static readonly string VaultMasterKey =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private PostgresTestServer? _server;
    private string? _serverConnectionString;
    private string? _databaseName;
    private string? _appRole;

    private string? AdminConnectionString { get; set; }

    private string? AppConnectionString { get; set; }

    public async ValueTask InitializeAsync()
    {
        _server = await PostgresTestServer.StartAsync().ConfigureAwait(false);
        if (_server is null)
        {
            return;
        }

        _serverConnectionString = _server.SuperuserConnectionString;
        string suffix = Guid.NewGuid().ToString("N");
        _databaseName = $"cadence_trial_exec_it_{suffix}";
        _appRole = $"cadence_trial_exec_app_{suffix}";
        string password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

        await ExecuteSqlAsync(
            _serverConnectionString,
            $"CREATE ROLE \"{_appRole}\" LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{password}'")
            .ConfigureAwait(false);
        await ExecuteSqlAsync(
            _serverConnectionString,
            $"CREATE DATABASE \"{_databaseName}\" OWNER \"{_appRole}\"")
            .ConfigureAwait(false);

        AdminConnectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString)
        {
            Database = _databaseName,
        }.ConnectionString;

        AppConnectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString)
        {
            Database = _databaseName,
            Username = _appRole,
            Password = password,
        }.ConnectionString;

        await KernelDatabase.MigrateAsync(AdminConnectionString).ConfigureAwait(false);
        await ExecuteSqlAsync(AdminConnectionString, KernelDatabase.GrantAppRoleSql(_appRole)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_databaseName is not null && _serverConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteSqlAsync(_serverConnectionString, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)")
                .ConfigureAwait(false);
            await ExecuteSqlAsync(_serverConnectionString, $"DROP ROLE IF EXISTS \"{_appRole}\"")
                .ConfigureAwait(false);
        }
    }

    [Fact(DisplayName = "TwoTrialFounders_RegisterSitesLinkStudiesAndEnrollSubj001InCadence101_SynchronizeKernelScreeningAndIsolateTenants")]
    public async Task TwoTrialFoundersRegisterSitesLinkStudiesAndEnrollSubj001InCadence101SynchronizeKernelScreeningAndIsolateTenants()
    {
        SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(SystemClock.Instance.UtcNow);
        var keycloak = new RecordingKeycloakClient();

        using var kernelHost = CreateKernelHost(clock, keycloak);
        _ = kernelHost.Server; // Ensure TestServer is started so Execution can attach to its handler.
        using var executionHost = CreateExecutionHost(clock, kernelHost);

        string founderOneSub = Guid.CreateVersion7(clock.UtcNow).ToString();
        string founderTwoSub = Guid.CreateVersion7(clock.UtcNow.AddSeconds(1)).ToString();

        var founderOne = await BootstrapTrialFounderAsync(kernelHost, founderOneSub, "Meridian Cardio Trial", ct);
        var founderTwo = await BootstrapTrialFounderAsync(kernelHost, founderTwoSub, "Helios Vascular Trial", ct);

        founderOne.TenantCode.Should().NotBe(founderTwo.TenantCode);
        founderOne.Roles.Should().Equal("Sponsor Admin", "Sponsor Designer", "Trial Coordinator");
        founderTwo.Roles.Should().Equal("Sponsor Admin", "Sponsor Designer", "Trial Coordinator");

        using var founderOneKernel = kernelHost.CreateSignedClient(new GatewayIdentity(
            UserId: founderOneSub,
            Roles: founderOne.Roles,
            SiteIds: [],
            TenantId: founderOne.TenantCode,
            ChangeReason: "Founder one trial site setup"));
        using var founderTwoKernel = kernelHost.CreateSignedClient(new GatewayIdentity(
            UserId: founderTwoSub,
            Roles: founderTwo.Roles,
            SiteIds: [],
            TenantId: founderTwo.TenantCode,
            ChangeReason: "Founder two trial site setup"));

        // 1. Both isolated trial founders create study CADENCE-101.
        await CreateStudyAsync(founderOneKernel, "CADENCE-101", "Meridian Cardio Trial", "idem-f1-study", ct);
        await CreateStudyAsync(founderTwoKernel, "CADENCE-101", "Helios Vascular Trial", "idem-f2-study", ct);

        using var founderOneExec = executionHost.CreateSignedClient(new GatewayIdentity(
            UserId: founderOneSub,
            Roles: founderOne.Roles,
            SiteIds: [],
            TenantId: founderOne.TenantCode,
            ChangeReason: "Screen first participant at clinical site",
            StudyIds: ["*"]));
        using var founderTwoExec = executionHost.CreateSignedClient(new GatewayIdentity(
            UserId: founderTwoSub,
            Roles: founderTwo.Roles,
            SiteIds: [],
            TenantId: founderTwo.TenantCode,
            ChangeReason: "Screen first participant at clinical site",
            StudyIds: ["*"]));

        // 2. Before SITE-01 is registered and linked to CADENCE-101, kernel screening synchronization rejects with StudySiteUnknown.
        using (var prematureScreen = await founderOneExec.PostAsJsonAsync(
            "/api/v1/execution/subjects/",
            new { subject_id = "SUBJ-001", study_id = "CADENCE-101", site_id = "SITE-01" },
            ct))
        {
            string prematureBody = await prematureScreen.Content.ReadAsStringAsync(ct);
            prematureScreen.StatusCode.Should().Be(HttpStatusCode.NotFound, prematureBody);
            prematureBody.Should().Contain("K.Execution.StudySiteUnknown");
        }

        // 3. Both founders register SITE-01 under SiteInstitution organizations (even when institution_name matches sponsor name) and link to CADENCE-101.
        var siteOne = await RegisterSiteAsync(
            founderOneKernel,
            siteCode: "SITE-01",
            siteName: "Meridian Boston Clinical Center",
            country: "US",
            institutionName: "Meridian Cardio Trial",
            idempotencyKey: "idem-f1-site-01",
            ct);
        siteOne.GetProperty("code").GetString().Should().Be("SITE-01");
        siteOne.GetProperty("institution_code").GetString().Should().EndWith("-SITE");

        var siteOrgsOne = await GetJsonAsync(founderOneKernel, "/api/v1/kernel/organizations?kind=SiteInstitution", ct);
        siteOrgsOne.EnumerateArray().Select(o => o.GetProperty("kind").GetString()).Should().OnlyContain(k => k == "SiteInstitution");
        siteOrgsOne.EnumerateArray().Select(o => o.GetProperty("name").GetString()).Should().Contain("Meridian Cardio Trial");

        var linkedOne = await LinkSiteToStudyAsync(founderOneKernel, "CADENCE-101", "SITE-01", "idem-f1-link-01", ct);
        linkedOne.GetProperty("site_code").GetString().Should().Be("SITE-01");

        await RegisterSiteAsync(
            founderTwoKernel,
            siteCode: "SITE-01",
            siteName: "Helios Munich Clinical Center",
            country: "DE",
            institutionName: "Munich University Hospital",
            idempotencyKey: "idem-f2-site-01",
            ct);
        await LinkSiteToStudyAsync(founderTwoKernel, "CADENCE-101", "SITE-01", "idem-f2-link-01", ct);

        // 4. Both trial founders now enroll SUBJ-001 into CADENCE-101 at SITE-01:
        //    both receive 201 Created, both get sequential EnrollmentIndex = 0 in their own tenant without 23505 collisions,
        //    and both synchronize screening into kernel.enrollments without StudySiteUnknown.
        Guid subjectOneId;
        using (var createOne = await founderOneExec.PostAsJsonAsync(
            "/api/v1/execution/subjects/",
            new { subject_id = "SUBJ-001", study_id = "CADENCE-101", site_id = "SITE-01" },
            ct))
        {
            string bodyOne = await createOne.Content.ReadAsStringAsync(ct);
            createOne.StatusCode.Should().Be(HttpStatusCode.Created, bodyOne);
            using var docOne = JsonDocument.Parse(bodyOne);
            subjectOneId = docOne.RootElement.GetProperty("id").GetGuid();
            docOne.RootElement.GetProperty("subject_id").GetString().Should().Be("SUBJ-001");
            docOne.RootElement.GetProperty("study_id").GetString().Should().Be("CADENCE-101");
            docOne.RootElement.GetProperty("site_id").GetString().Should().Be("SITE-01");
            docOne.RootElement.GetProperty("status").GetString().Should().Be("SCREENING");
        }

        Guid subjectTwoId;
        using (var createTwo = await founderTwoExec.PostAsJsonAsync(
            "/api/v1/execution/subjects/",
            new { subject_id = "SUBJ-001", study_id = "CADENCE-101", site_id = "SITE-01" },
            ct))
        {
            string bodyTwo = await createTwo.Content.ReadAsStringAsync(ct);
            createTwo.StatusCode.Should().Be(HttpStatusCode.Created, bodyTwo);
            using var docTwo = JsonDocument.Parse(bodyTwo);
            subjectTwoId = docTwo.RootElement.GetProperty("id").GetGuid();
            docTwo.RootElement.GetProperty("subject_id").GetString().Should().Be("SUBJ-001");
            docTwo.RootElement.GetProperty("study_id").GetString().Should().Be("CADENCE-101");
            docTwo.RootElement.GetProperty("site_id").GetString().Should().Be("SITE-01");
            docTwo.RootElement.GetProperty("status").GetString().Should().Be("SCREENING");
        }

        subjectOneId.Should().NotBe(subjectTwoId);

        // 5. Tenant-scoped reads in Execution and underlying PostgreSQL tables confirm strict tenant isolation and 0-based sequential index.
        var listOne = await GetJsonAsync(founderOneExec, "/api/v1/execution/subjects/?study_id=CADENCE-101", ct);
        listOne.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("id").GetGuid(), i.GetProperty("enrollment_index").GetInt32()))
            .Should().Equal((subjectOneId, 0));

        var listTwo = await GetJsonAsync(founderTwoExec, "/api/v1/execution/subjects/?study_id=CADENCE-101", ct);
        listTwo.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("id").GetGuid(), i.GetProperty("enrollment_index").GetInt32()))
            .Should().Equal((subjectTwoId, 0));

        await using var adminConn = new NpgsqlConnection(AdminConnectionString);
        await adminConn.OpenAsync(ct);

        Guid tenantOneUuid = NodeRef.Parse(founderOne.TenantRef).Id;
        Guid tenantTwoUuid = NodeRef.Parse(founderTwo.TenantRef).Id;

        (await ScalarLongAsync(
            adminConn,
            "SELECT count(*) FROM edc.clinical_subjects WHERE tenant_id = @tid AND \"StudyCode\" = 'CADENCE-101' AND \"SubjectCode\" = 'SUBJ-001' AND \"EnrollmentIndex\" = 0",
            ("tid", tenantOneUuid))).Should().Be(1L);
        (await ScalarLongAsync(
            adminConn,
            "SELECT count(*) FROM edc.clinical_subjects WHERE tenant_id = @tid AND \"StudyCode\" = 'CADENCE-101' AND \"SubjectCode\" = 'SUBJ-001' AND \"EnrollmentIndex\" = 0",
            ("tid", tenantTwoUuid))).Should().Be(1L);

        (await ScalarLongAsync(
            adminConn,
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'edc' AND tablename = 'clinical_subjects' AND indexname IN ('IX_clinical_subjects_tenant_id_SubjectCode', 'IX_clinical_subjects_tenant_id_StudyCode_EnrollmentIndex')")).Should().Be(2L);

        (await ScalarLongAsync(
            adminConn,
            "SELECT count(*) FROM kernel.enrollments WHERE tenant_id = @tid AND subject_code = 'SUBJ-001'",
            ("tid", tenantOneUuid))).Should().Be(1L);
        (await ScalarLongAsync(
            adminConn,
            "SELECT count(*) FROM kernel.enrollments WHERE tenant_id = @tid AND subject_code = 'SUBJ-001'",
            ("tid", tenantTwoUuid))).Should().Be(1L);
    }

    [Fact(DisplayName = "NonTrialSponsorAdmin_AndNonTrialCoordinator_Receive403Forbidden_OnSubjectCreate")]
    public async Task NonTrialSponsorAdminAndNonTrialCoordinatorReceive403ForbiddenOnSubjectCreate()
    {
        SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(SystemClock.Instance.UtcNow);
        var keycloak = new RecordingKeycloakClient();

        // Insert a non-trial enterprise tenant row in kernel.tenants so tenant resolution succeeds and RBAC/SubjectAuthority is exercised.
        const string EnterpriseTenantCode = "enterprise-sponsor-01";
        Guid enterpriseTenantId = Guid.CreateVersion7(clock.UtcNow);
        await using (var adminConn = new NpgsqlConnection(AdminConnectionString))
        {
            await adminConn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO kernel.tenants (id, code, name, residency, is_active, version_index, created_at, created_by, reason_for_change)
                VALUES (@id, @code, 'Enterprise Sponsor Workspace', 'US', true, 1, @now, 'it-seed', 'Seed enterprise tenant for authority test')
                ON CONFLICT (code) DO NOTHING
                """,
                adminConn);
            cmd.Parameters.AddWithValue("id", enterpriseTenantId);
            cmd.Parameters.AddWithValue("code", EnterpriseTenantCode);
            cmd.Parameters.AddWithValue("now", clock.UtcNow);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        using var kernelHost = CreateKernelHost(clock, keycloak);
        _ = kernelHost.Server;
        using var executionHost = CreateExecutionHost(clock, kernelHost);

        // 1. Non-trial Sponsor Admin (sponsor_dm) still receives 403 Forbidden (separation of duties preserved).
        using var sponsorAdminClient = executionHost.CreateSignedClient(new GatewayIdentity(
            UserId: "enterprise-sponsor-admin",
            Roles: ["Sponsor Admin"],
            SiteIds: [],
            TenantId: EnterpriseTenantCode,
            ChangeReason: "Attempt participant write as Sponsor Admin",
            StudyIds: ["*"]));

        using var sponsorResponse = await sponsorAdminClient.PostAsJsonAsync(
            "/api/v1/execution/subjects/",
            new { subject_id = "SUBJ-403-A", study_id = "CADENCE-101", site_id = "SITE-01" },
            ct);
        sponsorResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, await sponsorResponse.Content.ReadAsStringAsync(ct));

        // 2. Even inside a verified trial-<12hex> tenant, a caller holding ONLY Sponsor Admin (without Trial Coordinator) still receives 403 Forbidden.
        string trialFounderSub = Guid.CreateVersion7(clock.UtcNow.AddSeconds(5)).ToString();
        var trialWorkspace = await BootstrapTrialFounderAsync(kernelHost, trialFounderSub, "Separated Duties Bio", ct);
        using var sponsorOnlyInTrialClient = executionHost.CreateSignedClient(new GatewayIdentity(
            UserId: "trial-sponsor-only",
            Roles: ["Sponsor Admin"],
            SiteIds: [],
            TenantId: trialWorkspace.TenantCode,
            ChangeReason: "Attempt participant write without Trial Coordinator",
            StudyIds: ["*"]));

        using var sponsorOnlyTrialResponse = await sponsorOnlyInTrialClient.PostAsJsonAsync(
            "/api/v1/execution/subjects/",
            new { subject_id = "SUBJ-403-B", study_id = "CADENCE-101", site_id = "SITE-01" },
            ct);
        sponsorOnlyTrialResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, await sponsorOnlyTrialResponse.Content.ReadAsStringAsync(ct));

        // 3. A caller holding Trial Coordinator outside a verified trial-<12hex> tenant receives 403 Forbidden from SubjectAuthority.
        using var nonTrialCoordinatorClient = executionHost.CreateSignedClient(new GatewayIdentity(
            UserId: "enterprise-trial-coord",
            Roles: ["Trial Coordinator"],
            SiteIds: [],
            TenantId: EnterpriseTenantCode,
            ChangeReason: "Attempt Trial Coordinator write in non-trial tenant",
            StudyIds: ["*"]));

        using var nonTrialCoordResponse = await nonTrialCoordinatorClient.PostAsJsonAsync(
            "/api/v1/execution/subjects/",
            new { subject_id = "SUBJ-403-C", study_id = "CADENCE-101", site_id = "SITE-01" },
            ct);
        string nonTrialCoordBody = await nonTrialCoordResponse.Content.ReadAsStringAsync(ct);
        nonTrialCoordResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, nonTrialCoordBody);
        nonTrialCoordBody.Should().Contain("UNAUTHORIZED_SUBJECT_ROLE");
    }

    private void SkipIfUnavailable() =>
        Assert.SkipWhen(AppConnectionString is null, PostgresTestServer.SkipReason);

    private WebApplicationFactory<Cadence.Kernel.Host.Program> CreateKernelHost(
        FakeClock clock,
        IKeycloakOnboardingClient keycloakClient) =>
        new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", AppConnectionString)
            .UseSetting("Kernel:VaultMasterKey", VaultMasterKey)
            .UseSetting("Kernel:VaultRegion", "US")
            .ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.RemoveAll<IKeycloakOnboardingClient>();
                services.AddSingleton(keycloakClient);
            }));

    private WebApplicationFactory<Cadence.Apps.Execution.Program> CreateExecutionHost(
        FakeClock clock,
        WebApplicationFactory<Cadence.Kernel.Host.Program> kernelHost) =>
        new WebApplicationFactory<Cadence.Apps.Execution.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestDemographicsKey().UseTestRandomizationKey();
            builder.UseSetting("ConnectionStrings:cadence", AppConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting(HttpKernelEnrollmentSynchronizer.ConfigurationKey, "http://localhost");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
                services.RemoveAll<IKernelEnrollmentSynchronizer>();
                services.AddHttpClient<IKernelEnrollmentSynchronizer, HttpKernelEnrollmentSynchronizer>(client =>
                {
                    client.BaseAddress = kernelHost.Server.BaseAddress;
                }).ConfigurePrimaryHttpMessageHandler(() => kernelHost.Server.CreateHandler());

                services.RemoveAll<CurrentTenantAccessor>();
                services.AddSingleton<CurrentTenantAccessor>(sp =>
                {
                    var accessor = new CurrentTenantAccessor(
                        sp.GetRequiredService<ITenantCodeResolver>(),
                        sp.GetService<IHttpContextAccessor>());
                    return accessor;
                });
            });
        });

    private static async Task<(string TenantCode, string TenantRef, string[] Roles)> BootstrapTrialFounderAsync(
        WebApplicationFactory<Cadence.Kernel.Host.Program> kernelHost,
        string subjectId,
        string organizationName,
        CancellationToken ct)
    {
        using var unprovisioned = kernelHost.CreateSignedClient(new GatewayIdentity(subjectId, [], [], TenantId: null));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/onboarding/trial")
        {
            Content = JsonContent.Create(new { organization_name = organizationName, residency = "US", tier = "Individual" }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"idem-trial-founder:{subjectId}");
        request.Headers.TryAddWithoutValidation(
            GatewayHeaders.ChangeReason,
            ChangeReasonHeader.Encode($"Bootstrap self-service trial workspace for {organizationName}"));
        using var response = await unprovisioned.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var doc = JsonDocument.Parse(body);
        string tenantCode = doc.RootElement.GetProperty("tenant_code").GetString()!;
        string tenantRef = doc.RootElement.GetProperty("tenant_ref").GetString()!;
        string[] roles = doc.RootElement.GetProperty("assigned_roles").EnumerateArray().Select(e => e.GetString()!).ToArray();
        return (tenantCode, tenantRef, roles);
    }

    private static async Task CreateStudyAsync(
        HttpClient client,
        string studyCode,
        string sponsorName,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/studies")
        {
            Content = JsonContent.Create(new
            {
                study_code = studyCode,
                title = $"{sponsorName} Phase 2 Study",
                phase = "Phase 2",
                sponsor_name = sponsorName,
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        using var response = await client.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    }

    private static async Task<JsonElement> RegisterSiteAsync(
        HttpClient client,
        string siteCode,
        string siteName,
        string country,
        string institutionName,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/sites")
        {
            Content = JsonContent.Create(new
            {
                site_code = siteCode,
                name = siteName,
                country,
                institution_name = institutionName,
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        using var response = await client.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> LinkSiteToStudyAsync(
        HttpClient client,
        string studyCode,
        string siteCode,
        string idempotencyKey,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/kernel/studies/{studyCode}/sites")
        {
            Content = JsonContent.Create(new { site_code = siteCode }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        using var response = await client.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<long> ScalarLongAsync(
        NpgsqlConnection conn,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private sealed class RecordingKeycloakClient : IKeycloakOnboardingClient
    {
        public ConcurrentQueue<(string SubjectId, string TenantCode, string[] Roles)> Calls { get; } = new();

        public Task AssignTrialWorkspaceAsync(
            string subjectId,
            string tenantCode,
            IReadOnlyList<string> realmRoles,
            CancellationToken cancellationToken)
        {
            Calls.Enqueue((subjectId, tenantCode, [.. realmRoles]));
            return Task.CompletedTask;
        }
    }
}
