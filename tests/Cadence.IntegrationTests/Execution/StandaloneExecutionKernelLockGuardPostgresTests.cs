using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Etmf.Workers;
using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Devices;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
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
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        string tenantCode = TestSecurityContext.TenantCode;
        TenantId tenant = await EnsureTenantAlphaAsync(ct);
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

    [Fact(DisplayName = "StandaloneEtmfAndExecution_BatchCompletenessWebhook_UsesSingleTenantConnection_SkipsUnchangedSites_AndEnforcesBatchKernelLocks")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task StandaloneEtmfAndExecutionBatchCompletenessWebhookUsesSingleTenantConnectionSkipsUnchangedSitesAndEnforcesBatchKernelLocks()
    {
        kernelFixture.SkipIfUnavailable();
        executionBaseFactory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        string tenantCode = TestSecurityContext.TenantCode;
        TenantId tenant = await EnsureTenantAlphaAsync(ct);

        var (_, studyId) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        string studyCode;
        string siteCodeA = $"SITE-BA-{Guid.NewGuid():N}"[..15].ToUpperInvariant();
        string siteCodeB = $"SITE-BB-{Guid.NewGuid():N}"[..15].ToUpperInvariant();
        StudySiteId studySiteIdB;
        await using (var kdb = kernelFixture.CreateContext(tenant))
        {
            var studyRow = await kdb.Studies.AsNoTracking().SingleAsync(s => s.Id == studyId, ct);
            studyCode = studyRow.StudyCode;
            var rawSiteA = new Site(SiteId.New(), tenant, studyRow.SponsorId, siteCodeA, "Batch Clinic A", "US", KernelPostgresFixture.ActorId, "Seeded");
            var rawSiteB = new Site(SiteId.New(), tenant, studyRow.SponsorId, siteCodeB, "Batch Clinic B", "US", KernelPostgresFixture.ActorId, "Seeded");
            var linkA = new StudySite(StudySiteId.New(), tenant, studyId, rawSiteA.Id, "Active", KernelPostgresFixture.ActorId, "Seeded");
            var linkB = new StudySite(StudySiteId.New(), tenant, studyId, rawSiteB.Id, "Active", KernelPostgresFixture.ActorId, "Seeded");
            await kernelFixture.SeedAsync(rawSiteA, rawSiteB, linkA, linkB);
            studySiteIdB = linkB.Id;
        }

        await IssueGrantAsync(tenant, DataManager, "attestation.sign", AuthorityScope.Tenant(), ct);
        await IssueGrantAsync(tenant, DataManager, "lock.apply", AuthorityScope.Study(studyId), ct);
        await IssueGrantAsync(tenant, DataManager, "lock.release", AuthorityScope.Study(studyId), ct);

        int kernelBatchLockCalls = 0;
        int executionBatchWebhookCalls = 0;

        using var kernelHost = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", kernelFixture.ConnectionString)
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        using var rawKernelClient = kernelHost.CreateClient();
        using var countingKernelClient = new HttpClient(new PathCountingDelegatingHandler(
            rawKernelClient,
            path =>
            {
                if (string.Equals(path, "/api/v1/kernel/locks/in-force/batch", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref kernelBatchLockCalls);
                }
            }))
        {
            BaseAddress = rawKernelClient.BaseAddress,
        };

        using var executionHost = executionBaseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IKernelLockGuard>();
                services.AddScoped<IKernelLockGuard>(sp => new HttpKernelLockGuard(
                    countingKernelClient,
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    sp.GetRequiredService<IOptionsMonitor<GatewayAuthenticationOptions>>(),
                    SystemClock.Instance));
            }));
        using var rawExecutionClient = executionHost.CreateClient();
        using var countingExecutionClient = new HttpClient(new PathCountingDelegatingHandler(
            rawExecutionClient,
            path =>
            {
                if (string.Equals(path, "/api/v1/execution/webhooks/etmf/batch", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref executionBatchWebhookCalls);
                }
            }))
        {
            BaseAddress = rawExecutionClient.BaseAddress,
        };

        var connectionCounter = new PostgresConnectionCountingInterceptor();
        var sqlCounter = new PostgresSqlCountingInterceptor();
        using var etmfHost = new WebApplicationFactory<Cadence.Apps.Etmf.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestStorage()
            .UseSetting("ConnectionStrings:cadence", executionBaseFactory.ConnectionString)
            .UseSetting("Database:MigrateOnStartup", "true")
            .UseSetting("EXECUTION_SERVICE_URL", "https://execution.internal")
            .ConfigureServices(services =>
            {
                var exportWorkerDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(EtmfExportWorker));
                if (exportWorkerDescriptor is not null)
                {
                    services.Remove(exportWorkerDescriptor);
                }

                services.RemoveAll<CurrentTenantAccessor>();
                services.AddSingleton<CurrentTenantAccessor>(sp =>
                {
                    var accessor = new CurrentTenantAccessor(
                        sp.GetRequiredService<ITenantCodeResolver>(),
                        sp.GetService<IHttpContextAccessor>());
                    accessor.UseNonHttpSeedingFallbackTenant(tenantCode);
                    return accessor;
                });

                services.AddSingleton(connectionCounter);
                services.AddSingleton(sqlCounter);
                services.ConfigureDbContext<EtmfDbContext>(opts => opts.AddInterceptors(connectionCounter, sqlCounter));

                services.RemoveAll<IEtmfCompletenessWebhookPublisher>();
                services.AddScoped<IEtmfCompletenessWebhookPublisher>(sp => new HttpEtmfCompletenessWebhookPublisher(
                    countingExecutionClient,
                    sp.GetRequiredService<IOptions<GatewayAuthenticationOptions>>(),
                    sp.GetRequiredService<IClock>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HttpEtmfCompletenessWebhookPublisher>>(),
                    sp.GetService<IHttpContextAccessor>()));
            }));

        using var etmfSponsor = etmfHost.CreateSignedClient(new GatewayIdentity(
            UserId: DataManager,
            Roles: ["sponsor_dm"],
            SiteIds: [],
            TenantId: tenantCode,
            StudyIds: [studyCode],
            ProductFamilies: ["Orbit"],
            ChangeReason: Reason));

        string centralNonStartupId = Guid.NewGuid().ToString();
        string centralProtocolId = Guid.NewGuid().ToString();
        var now = SystemClock.Instance.UtcNow;

        // Seed central non-startup doc, central startup protocol doc, and 2 site expectations + docs in PostgreSQL EtmfDbContext.
        using (var seedScope = etmfHost.Services.CreateScope())
        {
            using var _ = seedScope.ServiceProvider
                .GetRequiredService<Cadence.Database.Audit.IAuditActorScope>()
                .Begin("svc:test-seeder", "Test fixture seeding");
            var etmfDb = seedScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            etmfDb.TmfExpectedDocuments.AddRange(
                new TmfExpectedDocument
                {
                    StudyId = studyCode,
                    SiteId = null,
                    Milestone = "SITE_ACTIVATION",
                    Zone = 1,
                    Section = "01.01",
                    ArtifactType = "Clinical Trial Protocol",
                },
                new TmfExpectedDocument
                {
                    StudyId = studyCode,
                    SiteId = siteCodeA,
                    Milestone = "SITE_ACTIVATION",
                    Zone = 5,
                    Section = "05.02",
                    ArtifactType = "FDA Form 1572",
                },
                new TmfExpectedDocument
                {
                    StudyId = studyCode,
                    SiteId = siteCodeB,
                    Milestone = "SITE_ACTIVATION",
                    Zone = 5,
                    Section = "05.02",
                    ArtifactType = "FDA Form 1572",
                });

            var nonStartupDoc = new TmfDocument(centralNonStartupId)
            {
                StudyId = studyCode,
                SiteId = null,
                Filename = "dsmb-charter.pdf",
                Content = "DSMB Charter binary payload",
                Zone = 1,
                Section = "01.03",
                ArtifactCode = "01.03.01",
                ArtifactType = "DSMB Charter",
            };
            nonStartupDoc.TransitionTo(TmfDocumentLifecycleState.TechnicalQc, "seed-cra", "CRA", Reason, now, 1);

            var centralProtocolDoc = new TmfDocument(centralProtocolId)
            {
                StudyId = studyCode,
                SiteId = null,
                Filename = "protocol-v1.pdf",
                Content = "Clinical Trial Protocol payload",
                Zone = 1,
                Section = "01.01",
                ArtifactCode = "01.01.01",
                ArtifactType = "Clinical Trial Protocol",
            };
            centralProtocolDoc.TransitionTo(TmfDocumentLifecycleState.TechnicalQc, "seed-cra", "CRA", Reason, now, 1);

            var siteADoc = new TmfDocument(Guid.NewGuid().ToString())
            {
                StudyId = studyCode,
                SiteId = siteCodeA,
                Filename = "site-a-1572.pdf",
                Content = "Approved FDA Form 1572 payload",
                Zone = 5,
                Section = "05.02",
                ArtifactCode = "05.02.02",
                ArtifactType = "FDA Form 1572",
            };
            siteADoc.TransitionTo(TmfDocumentLifecycleState.TechnicalQc, "seed-cra", "CRA", Reason, now, 1);
            siteADoc.TransitionTo(TmfDocumentLifecycleState.Approved, "seed-dm", "Data Manager", Reason, now, 2);

            etmfDb.TmfDocuments.AddRange(nonStartupDoc, centralProtocolDoc, siteADoc);
            await etmfDb.SaveChangesAsync(ct);
        }

        // 1. Reviewing a central non-startup document triggers 0 outbound webhooks and 0 Kernel lock queries.
        using (var nonStartupRes = await etmfSponsor.PostAsJsonAsync(
            $"/api/v1/etmf/intelligence/qc-review/{centralNonStartupId}",
            new CraqcReviewRequest("ACCEPT", Reason),
            ct))
        {
            nonStartupRes.StatusCode.Should().Be(HttpStatusCode.OK, await nonStartupRes.Content.ReadAsStringAsync(ct));
        }

        Volatile.Read(ref executionBatchWebhookCalls).Should().Be(0);
        Volatile.Read(ref kernelBatchLockCalls).Should().Be(0);

        // 2. Directly verify single-connection tenant lease on PostgreSQL: 1 connection open (1 set_config), projected SELECTs without binary/JSON columns.
        connectionCounter.Reset();
        sqlCounter.Reset();
        using (var evalScope = etmfHost.Services.CreateScope())
        {
            var etmfDb = evalScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            var publisher = evalScope.ServiceProvider.GetRequiredService<IEtmfCompletenessWebhookPublisher>();
            await publisher.PublishSiteActivationCompletenessAsync(
                etmfDb,
                studyCode,
                siteId: null,
                tenantId: tenantCode,
                reasonForChange: Reason,
                reviewContext: new EtmfDocumentReviewContext(new EtmfArtifactTaxonomy("Clinical Trial Protocol", "01.01.01")),
                ct: ct);
        }

        connectionCounter.ConnectionOpenCount.Should().Be(1,
            "reusing one open NpgsqlConnection across projected expected-doc and study-doc queries must open and run StandaloneTenantConnectionInterceptor once");
        connectionCounter.LastObservedTenantSetting.Should().NotBeNullOrWhiteSpace();
        sqlCounter.SelectCommands.Should().HaveCount(2);
        sqlCounter.SelectCommands.Should().NotContain(sql =>
            sql.Contains("content", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("metadata_json", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("signature_manifestation", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("redaction_manifest_json", StringComparison.OrdinalIgnoreCase));
        Volatile.Read(ref executionBatchWebhookCalls).Should().Be(1);
        Volatile.Read(ref kernelBatchLockCalls).Should().Be(1);

        // Verify initial site caches in Execution PostgreSQL: site A missing Clinical Trial Protocol (false), site B missing Clinical Trial Protocol + FDA Form 1572 (false).
        using (var verifyScope1 = executionHost.Services.CreateScope())
        {
            var execDb = verifyScope1.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var caches1 = await execDb.SiteComplianceCaches.AsNoTracking()
                .Where(c => c.StudyCode == new StudyCode(studyCode))
                .ToDictionaryAsync(c => c.SiteCode.Value, ct);
            caches1.Should().HaveCount(2);
            caches1[siteCodeA].IsComplete.Should().BeFalse();
            caches1[siteCodeA].Version.Should().Be(1);
            caches1[siteCodeB].IsComplete.Should().BeFalse();
            caches1[siteCodeB].Version.Should().Be(1);
        }

        // 3. Re-posting the identical batch snapshot skips unchanged rows (preserving Version == 1 and avoiding duplicate periods/audits).
        using var etmfSvc = executionHost.CreateSignedClient(new GatewayIdentity(
            UserId: "svc:etmf",
            Roles: [PermissionMatrix.EtmfServiceRole],
            SiteIds: [siteCodeA, siteCodeB],
            TenantId: tenantCode,
            StudyIds: [studyCode],
            ProductFamilies: ["Orbit"],
            ChangeReason: Reason));
        using (var repeatRes = await etmfSvc.PostAsJsonAsync(
            "/api/v1/execution/webhooks/etmf/batch",
            new EtmfCompletenessBatchWebhookPayload(
                new StudyCode(studyCode),
                "SITE_ACTIVATION",
                [
                    new EtmfSiteCompletenessItem(new SiteCode(siteCodeA), false, ["Clinical Trial Protocol"]),
                    new EtmfSiteCompletenessItem(new SiteCode(siteCodeB), false, ["Clinical Trial Protocol", "FDA Form 1572"]),
                ]),
            ExecutionApiFactoryOptions(),
            ct))
        {
            repeatRes.StatusCode.Should().Be(HttpStatusCode.OK, await repeatRes.Content.ReadAsStringAsync(ct));
        }

        using (var verifyRepeatScope = executionHost.Services.CreateScope())
        {
            var execDb = verifyRepeatScope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var repeatCaches = await execDb.SiteComplianceCaches.AsNoTracking()
                .Where(c => c.StudyCode == new StudyCode(studyCode))
                .ToDictionaryAsync(c => c.SiteCode.Value, ct);
            repeatCaches[siteCodeA].Version.Should().Be(1);
            repeatCaches[siteCodeB].Version.Should().Be(1);

            int periodCount = await execDb.SiteActivationPeriods.AsNoTracking()
                .CountAsync(p => p.StudyCode == new StudyCode(studyCode), ct);
            periodCount.Should().Be(2);

            string[] cacheEntityIds = repeatCaches.Values.Select(c => c.Id.ToString()).ToArray();
            int auditCount = await execDb.AuditLogEntries.AsNoTracking()
                .CountAsync(a => a.Action == "ETMF_COMPLIANCE_UPDATE" && cacheEntityIds.Contains(a.EntityId), ct);
            auditCount.Should().Be(2);
        }

        // 4. Approve central Protocol via POST /api/v1/etmf/intelligence/qc-review/{doc_id} -> site A becomes complete (Version 2), site B missing list narrows to FDA Form 1572 (Version 2).
        using (var approveRes = await etmfSponsor.PostAsJsonAsync(
            $"/api/v1/etmf/intelligence/qc-review/{centralProtocolId}",
            new CraqcReviewRequest("ACCEPT", Reason),
            ct))
        {
            approveRes.StatusCode.Should().Be(HttpStatusCode.OK, await approveRes.Content.ReadAsStringAsync(ct));
        }

        using (var verifyScope2 = executionHost.Services.CreateScope())
        {
            var execDb = verifyScope2.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var caches2 = await execDb.SiteComplianceCaches.AsNoTracking()
                .Where(c => c.StudyCode == new StudyCode(studyCode))
                .ToDictionaryAsync(c => c.SiteCode.Value, ct);
            caches2[siteCodeA].IsComplete.Should().BeTrue();
            caches2[siteCodeA].Version.Should().Be(2);
            caches2[siteCodeB].IsComplete.Should().BeFalse();
            caches2[siteCodeB].MissingDocuments.Should().Equal("FDA Form 1572");
            caches2[siteCodeB].Version.Should().Be(2);
        }

        // 5. Freeze site B in Clinical Kernel -> batch webhook targeting [site A, site B] fails closed with 423 K.Observation.Locked via batch lock endpoint!
        using var kernelDm = kernelHost.CreateSignedClient(new GatewayIdentity(DataManager, [], [], TenantId: tenantCode));
        string studyRef = NodeRef.For(studyId).ToString();
        string siteBRef = NodeRef.For(studySiteIdB).ToString();
        string applySig = TestSecurityContext.CreateSigToken(
            DataManager,
            KernelLockEndpoints.ApplyLockAction,
            KernelLockEndpoints.ApplyLockSemanticAction);
        using (var applyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/locks")
        {
            Content = JsonContent.Create(new { study_ref = studyRef, scope_ref = siteBRef, kind = "Frozen", meaning = "Approved" }),
        })
        {
            applyReq.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
            applyReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Freeze site B before audit"));
            applyReq.Headers.Add(GatewayHeaders.SigToken, applySig);
            using var applyRes = await kernelDm.SendAsync(applyReq, ct);
            applyRes.StatusCode.Should().Be(HttpStatusCode.Created, await applyRes.Content.ReadAsStringAsync(ct));
        }

        using var lockedBatchRes = await etmfSvc.PostAsJsonAsync(
            "/api/v1/execution/webhooks/etmf/batch",
            new EtmfCompletenessBatchWebhookPayload(
                new StudyCode(studyCode),
                "SITE_ACTIVATION",
                [
                    new EtmfSiteCompletenessItem(new SiteCode(siteCodeA), true, []),
                    new EtmfSiteCompletenessItem(new SiteCode(siteCodeB), true, []),
                ]),
            ExecutionApiFactoryOptions(),
            ct);
        string lockedBatchBody = await lockedBatchRes.Content.ReadAsStringAsync(ct);
        lockedBatchRes.StatusCode.Should().Be(HttpStatusCode.Locked, lockedBatchBody);
        using var lockedBatchDoc = JsonDocument.Parse(lockedBatchBody);
        lockedBatchDoc.RootElement.GetProperty("code").GetString().Should().Be(ObservationErrors.Locked.Code);
    }

    private sealed class PathCountingDelegatingHandler(HttpClient innerClient, Action<string> onRequestPath) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequestPath(request.RequestUri?.AbsolutePath ?? string.Empty);
            using var clone = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
            return await innerClient.SendAsync(clone, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original, CancellationToken ct)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri);
            foreach (var header in original.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (original.Content is not null)
            {
                byte[] bytes = await original.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                var content = new ByteArrayContent(bytes);
                foreach (var header in original.Content.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                clone.Content = content;
            }

            return clone;
        }
    }

    private sealed class PostgresConnectionCountingInterceptor : DbConnectionInterceptor
    {
        private int _connectionOpenCount;
        private string? _lastObservedTenantSetting;

        public int ConnectionOpenCount => Volatile.Read(ref _connectionOpenCount);

        public string? LastObservedTenantSetting => Volatile.Read(ref _lastObservedTenantSetting);

        public void Reset()
        {
            Interlocked.Exchange(ref _connectionOpenCount, 0);
            Volatile.Write(ref _lastObservedTenantSetting, null);
        }

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            Interlocked.Increment(ref _connectionOpenCount);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT pg_catalog.current_setting('cadence.tenant_id', true)";
            Volatile.Write(ref _lastObservedTenantSetting, cmd.ExecuteScalar() as string);
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectionOpenCount);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT pg_catalog.current_setting('cadence.tenant_id', true)";
            object? val = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _lastObservedTenantSetting, val as string);
        }
    }

    private sealed class PostgresSqlCountingInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _selectCommands = new();

        public IReadOnlyCollection<string> SelectCommands => _selectCommands;

        public void Reset() => _selectCommands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Record(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(string sql)
        {
            if (sql.Contains("FROM", StringComparison.OrdinalIgnoreCase)
                && (sql.Contains("tmf_expected_documents", StringComparison.OrdinalIgnoreCase)
                    || sql.Contains("tmf_documents", StringComparison.OrdinalIgnoreCase)))
            {
                _selectCommands.Enqueue(sql);
            }
        }
    }

    private async Task<TenantId> EnsureTenantAlphaAsync(CancellationToken ct)
    {
        string tenantCode = TestSecurityContext.TenantCode;
        await using var adminDb = kernelFixture.CreateAdminContext();
        var existingTenantId = await adminDb.Tenants.AsNoTracking()
            .Where(t => t.Code == tenantCode)
            .Select(t => (TenantId?)t.Id)
            .SingleOrDefaultAsync(ct);
        if (existingTenantId is { } foundTenant)
        {
            return foundTenant;
        }

        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, tenantCode, "Cross-service lock guard tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);
        return tenant;
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
