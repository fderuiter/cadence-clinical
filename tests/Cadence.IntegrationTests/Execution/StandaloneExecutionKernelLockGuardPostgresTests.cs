using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Devices;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Real cross-service PostgreSQL integration test wiring standalone <see cref="Cadence.Apps.Execution.Program"/>'s
/// <see cref="HttpKernelLockGuard"/> directly over HTTP to <see cref="Cadence.Kernel.Host.Program"/>'s
/// <c>GET /api/v1/kernel/locks/in-force</c> endpoint without referencing internal kernel assemblies (<c>K-XS-2</c>).
/// Verifies that applying a signed <c>Frozen</c> or <c>Locked</c> assertion in the Clinical Kernel immediately causes
/// standalone Execution mutations to fail closed with <c>423 K.Observation.Locked</c> while stateless endpoints remain
/// <c>200 OK</c>, and releasing the lock restores writes (#5376, #5466).
/// </summary>
[Trait("Category", "Integration")]
public sealed class StandaloneExecutionKernelLockGuardPostgresTests(
    ExecutionFormModulePostgresFixture kernelFixture,
    ExecutionPostgresApiFactory executionBaseFactory)
    : IClassFixture<ExecutionFormModulePostgresFixture>,
      IClassFixture<ExecutionPostgresApiFactory>,
      IDisposable
{
    private const string DataManager = "dm@cross-lock.test";
    private const string Coordinator = "crc@cross-lock.test";
    private const string Reason = "Cross-service synchronous kernel lock guard verification on PostgreSQL.";

    private readonly KernelCollectionScenario _scenario = new(kernelFixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _kernelHost;
    private HttpClient? _kernelInternalClient;
    private WebApplicationFactory<Cadence.Apps.Execution.Program>? _executionHost;

    public void Dispose()
    {
        _executionHost?.Dispose();
        _kernelInternalClient?.Dispose();
        _kernelHost?.Dispose();
    }

    [Fact(DisplayName = "StandaloneExecution_EnforcesLiveKernelLocksOverSignedHttp_AndRestoresWritesAfterRelease")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task StandaloneExecutionEnforcesLiveKernelLocksOverSignedHttpAndRestoresWritesAfterRelease()
    {
        kernelFixture.SkipIfUnavailable();
        executionBaseFactory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        // 1. Seed or reuse tenant-alpha (pre-registered in ExecutionPostgresApiFactory), plus a run-unique study, active site, and grants in the Clinical Kernel.
        const string tenantCode = "tenant-alpha";
        TenantId tenant;
        await using (var tdb = kernelFixture.CreateContext(TenantId.New()))
        {
            var existingTenantId = await tdb.Tenants.AsNoTracking()
                .Where(t => t.Code == tenantCode)
                .Select(t => (TenantId?)t.Id)
                .SingleOrDefaultAsync(ct);
            if (existingTenantId is { } foundTenant)
            {
                tenant = foundTenant;
            }
            else
            {
                tenant = TenantId.New();
                await _scenario.Protocol.OkAsync(
                    new ProvisionTenant(tenant, tenantCode, "Cross-service lock guard tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
                    ct);
            }
        }
        var (_, studyId) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        string studyCode;
        string siteCode = $"SITE-X-{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        StudySiteId studySiteId;
        await using (var kdb = kernelFixture.CreateContext(tenant))
        {
            var studyRow = await kdb.Studies.AsNoTracking().SingleAsync(s => s.Id == studyId, ct);
            studyCode = studyRow.StudyCode;
            var rawSite = new Site(SiteId.New(), tenant, studyRow.SponsorId, siteCode, "Cross-Lock Clinic", "US", KernelPostgresFixture.ActorId, "Seeded");
            var link = new StudySite(StudySiteId.New(), tenant, studyId, rawSite.Id, "Active", KernelPostgresFixture.ActorId, "Seeded");
            await kernelFixture.SeedAsync(rawSite, link);
            studySiteId = link.Id;
        }

        await IssueGrantAsync(tenant, DataManager, "attestation.sign", AuthorityScope.Tenant(), ct);
        await IssueGrantAsync(tenant, DataManager, "lock.apply", AuthorityScope.Study(studyId), ct);
        await IssueGrantAsync(tenant, DataManager, "lock.release", AuthorityScope.Study(studyId), ct);
        await IssueGrantAsync(tenant, Coordinator, "observation.record", AuthorityScope.StudySite(studySiteId), ct);

        _kernelHost = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", kernelFixture.ConnectionString)
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        _kernelInternalClient = _kernelHost.CreateClient();

        _executionHost = executionBaseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKernelLockGuard>();
                services.AddScoped<IKernelLockGuard>(sp => new HttpKernelLockGuard(
                    _kernelInternalClient,
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    sp.GetRequiredService<IOptionsMonitor<GatewayAuthenticationOptions>>(),
                    SystemClock.Instance));
            }));

        using var kernelDm = _kernelHost.CreateSignedClient(new GatewayIdentity(DataManager, [], [], TenantId: tenantCode));
        using var execCrc = _executionHost.CreateSignedClient(new GatewayIdentity(
            UserId: Coordinator,
            Roles: ["CRC"],
            SiteIds: [siteCode],
            TenantId: tenantCode,
            StudyIds: [studyCode],
            ChangeReason: Reason));
        using var execDm = _executionHost.CreateSignedClient(new GatewayIdentity(
            UserId: DataManager,
            Roles: ["DATA_MANAGER", PermissionMatrix.EtmfServiceRole],
            SiteIds: [siteCode],
            TenantId: tenantCode,
            StudyIds: [studyCode],
            ChangeReason: Reason));

        // 2. Before any lock is applied, standalone Execution mutations succeed.
        using (var unlockedSubjectRes = await execCrc.PostAsJsonAsync(
            "/api/v1/execution/subjects",
            new SubjectCreateRequest("SUBJ-PRE-LOCK", studyCode, siteCode, null),
            ExecutionApiFactoryOptions(),
            ct))
        {
            unlockedSubjectRes.StatusCode.Should().Be(HttpStatusCode.Created, await unlockedSubjectRes.Content.ReadAsStringAsync(ct));
        }

        // 3. Apply a signed Frozen assertion on studySiteId in the Clinical Kernel.
        string studyRef = NodeRef.For(studyId).ToString();
        string siteRef = NodeRef.For(studySiteId).ToString();
        string applySig = TestSecurityContext.CreateSigToken(
            DataManager,
            KernelLockEndpoints.ApplyLockAction,
            KernelLockEndpoints.ApplyLockSemanticAction);

        string lockRef;
        using (var applyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/locks")
        {
            Content = JsonContent.Create(new { study_ref = studyRef, scope_ref = siteRef, kind = "Frozen", meaning = "Approved" }),
        })
        {
            applyReq.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
            applyReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Freeze site before interim review"));
            applyReq.Headers.Add(GatewayHeaders.SigToken, applySig);
            using var applyRes = await kernelDm.SendAsync(applyReq, ct);
            string applyBody = await applyRes.Content.ReadAsStringAsync(ct);
            applyRes.StatusCode.Should().Be(HttpStatusCode.Created, applyBody);
            using var applyDoc = JsonDocument.Parse(applyBody);
            lockRef = applyDoc.RootElement.GetProperty("ref").GetString()!;
        }

        // 4. Standalone Execution mutating routes targeting that site now fail closed with 423 K.Observation.Locked!
        using (var lockedSubjectRes = await execCrc.PostAsJsonAsync(
            "/api/v1/execution/subjects",
            new SubjectCreateRequest("SUBJ-WHILE-LOCKED", studyCode, siteCode, null),
            ExecutionApiFactoryOptions(),
            ct))
        {
            string body = await lockedSubjectRes.Content.ReadAsStringAsync(ct);
            lockedSubjectRes.StatusCode.Should().Be(HttpStatusCode.Locked, body);
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("code").GetString().Should().Be(ObservationErrors.Locked.Code);
        }

        using (var lockedRangeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/execution/lab-ranges")
        {
            Content = JsonContent.Create(
                new LabReferenceRangeCreateRequest(studyCode, "ALT", "Alanine Aminotransferase", SiteId: siteCode, Unit: "U/L", LowBound: 7, HighBound: 56),
                options: ExecutionApiFactoryOptions()),
        })
        {
            lockedRangeReq.Headers.Add(GatewayHeaders.ChangeReason, Reason);
            using var lockedRangeRes = await execDm.SendAsync(lockedRangeReq, ct);
            lockedRangeRes.StatusCode.Should().Be(HttpStatusCode.Locked, await lockedRangeRes.Content.ReadAsStringAsync(ct));
        }

        using (var lockedWebhookRes = await execDm.PostAsJsonAsync(
            "/api/v1/execution/webhooks/etmf",
            new EtmfCompletenessWebhookPayload(studyCode, siteCode, "SITE_ACTIVATION", true, null),
            ExecutionApiFactoryOptions(),
            ct))
        {
            lockedWebhookRes.StatusCode.Should().Be(HttpStatusCode.Locked, await lockedWebhookRes.Content.ReadAsStringAsync(ct));
        }

        // 5. Stateless standalone Execution routes remain 200 OK while the site is frozen!
        using (var udiRes = await execCrc.PostAsJsonAsync(
            "/api/v1/execution/devices/parse-udi",
            new UdiParseRequest("(01)00850006781111(10)LOT123(21)SN001"),
            ExecutionApiFactoryOptions(),
            ct))
        {
            udiRes.StatusCode.Should().Be(HttpStatusCode.OK, await udiRes.Content.ReadAsStringAsync(ct));
        }

        using (var unitRes = await execDm.PostAsJsonAsync(
            "/api/v1/execution/labs/convert-unit",
            new UnitConversionRequest("GLUC", 5.5, "mmol/L", "mg/dL"),
            ExecutionApiFactoryOptions(),
            ct))
        {
            unitRes.StatusCode.Should().Be(HttpStatusCode.OK, await unitRes.Content.ReadAsStringAsync(ct));
        }

        // 6. Release the Frozen assertion in the Clinical Kernel -> standalone Execution mutations succeed again!
        string releaseAction = KernelLockEndpoints.ReleaseLockAction(lockRef.Split('@')[0]);
        string releaseSig = TestSecurityContext.CreateSigToken(
            DataManager,
            releaseAction,
            KernelLockEndpoints.ReleaseLockSemanticAction);
        using (var releaseReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/kernel/locks/{lockRef}/release")
        {
            Content = JsonContent.Create(new { study_ref = studyRef, meaning = "Responsibility" }),
        })
        {
            releaseReq.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
            releaseReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Release site freeze after review"));
            releaseReq.Headers.Add(GatewayHeaders.SigToken, releaseSig);
            using var releaseRes = await kernelDm.SendAsync(releaseReq, ct);
            releaseRes.StatusCode.Should().Be(HttpStatusCode.OK, await releaseRes.Content.ReadAsStringAsync(ct));
        }

        using var postReleaseSubjectRes = await execCrc.PostAsJsonAsync(
            "/api/v1/execution/subjects",
            new SubjectCreateRequest("SUBJ-POST-RELEASE", studyCode, siteCode, null),
            ExecutionApiFactoryOptions(),
            ct);
        postReleaseSubjectRes.StatusCode.Should().Be(HttpStatusCode.Created, await postReleaseSubjectRes.Content.ReadAsStringAsync(ct));
    }

    private Task IssueGrantAsync(TenantId tenant, string userId, string capability, AuthorityScope scope, CancellationToken ct) =>
        _scenario.Protocol.OkAsync(
            new IssueGrant(
                tenant,
                GrantId.New(),
                Actor.Person(userId),
                capability,
                null,
                scope,
                kernelFixture.Clock.Advance(TimeSpan.FromSeconds(1)),
                null,
                false,
                KernelProtocolScenario.Key(),
                "Test grant"),
            ct);

    private static JsonSerializerOptions ExecutionApiFactoryOptions()
    {
        var options = new JsonSerializerOptions();
        Cadence.Apps.Execution.Application.Serialization.ExecutionJsonSerialization.Configure(options);
        return options;
    }
}
