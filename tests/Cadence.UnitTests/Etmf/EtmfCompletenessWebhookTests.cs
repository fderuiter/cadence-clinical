using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Execution;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Etmf;

/// <summary>
/// Verifies that staging and approving required site artifacts in eTMF posts a signed completeness webhook
/// as <see cref="PermissionMatrix.EtmfServiceRole"/> to Execution's <c>POST /api/v1/execution/webhooks/etmf</c>,
/// updating <c>SITE_ACTIVATION</c> completeness for unseeded sites such as <c>SITE-102</c>, propagating
/// signed caller tenant scope (ADR-2548), and unblocking sponsor site activation, subject enrollment, and randomization.
/// </summary>
public sealed class EtmfCompletenessWebhookTests : IDisposable
{
    private const string StudyId = "CADENCE-101";
    private const string SiteId = "SITE-102";
    private const string Reason = "Sponsor clinical QC approval for site startup essential documents";

    private readonly ConcurrentQueue<string?> _observedWebhookTenants = new();
    private readonly ConcurrentQueue<(HttpMethod Method, string Path)> _observedWebhookRequests = new();
    private readonly ExecutionApiFactory _executionFactory = new();
    private readonly WebApplicationFactory<Cadence.Apps.Etmf.Program> _etmfFactory;

    public EtmfCompletenessWebhookTests()
    {
        var etmfDbName = $"etmf-webhook-{Guid.NewGuid():N}";
        _etmfFactory = new WebApplicationFactory<Cadence.Apps.Etmf.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseTestStorage();

            host.ConfigureTestServices(services =>
            {
                var inMemoryServices = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.RemoveAll<DbContextOptions<EtmfDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<EtmfDbContext>>();
                services.AddDbContext<EtmfDbContext>(options =>
                {
                    options.UseInMemoryDatabase(etmfDbName);
                    options.UseInternalServiceProvider(inMemoryServices);
                });

                services.AddHttpClient<IEtmfCompletenessWebhookPublisher, HttpEtmfCompletenessWebhookPublisher>(client =>
                {
                    client.BaseAddress = new Uri("http://localhost");
                }).ConfigurePrimaryHttpMessageHandler(() => new RecordingForwardingHandler(
                    _executionFactory.Server.CreateHandler(),
                    _observedWebhookTenants,
                    _observedWebhookRequests));

                services.AddLogging(logging =>
                {
                    logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
                    logging.AddFilter("MassTransit", LogLevel.None);
                });
            });
        });
    }

    public void Dispose()
    {
        _etmfFactory.Dispose();
        _executionFactory.Dispose();
    }

    [Fact]
    public async Task SponsorApprovingSiteArtifactsInEtmf_UpdatesSite102CompletenessOverSignedWebhook_AndUnlocksActivationEnrollmentAndRandomization()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed ExecutionDbSeeder baseline (SITE-101 has SITE_ACTIVATION; SITE-102 only has CONDUCT).
        using (var warmClient = _executionFactory.CreateSignedClient())
        using (var scope = _executionFactory.Services.CreateSeedingScope())
        {
            var execDb = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            await ExecutionDbSeeder.SeedAsync(execDb, ct);
        }

        var sponsorIdentity = new GatewayIdentity(
            UserId: "sponsor-dm-1",
            Roles: ["Data Manager"],
            SiteIds: [SiteId],
            TenantId: TestSecurityContext.TenantCode,
            ChangeReason: Reason,
            StudyIds: [StudyId]);

        using var executionSponsorClient = _executionFactory.CreateSignedClient(sponsorIdentity);
        using var etmfSponsorClient = _etmfFactory.CreateSignedClient(sponsorIdentity);

        // 1. Initially SITE-102 is NOT complete for SITE_ACTIVATION and activation returns 400 Bad Request.
        var initialStatusResp = await executionSponsorClient.GetAsync(
            $"/api/v1/execution/sites/{SiteId}/compliance-status?study_id={StudyId}", ct);
        initialStatusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialStatus = await initialStatusResp.Content.ReadFromJsonAsync<ComplianceStatusResponse>(
            ExecutionApiFactory.JsonOptions, ct);
        initialStatus.Should().NotBeNull();
        initialStatus!.IsComplete.Should().BeFalse();

        var blockedActivateResp = await executionSponsorClient.PostAsJsonAsync(
            $"/api/v1/execution/sites/{SiteId}/activate",
            new SiteActivationRequest(StudyId),
            ExecutionApiFactory.JsonOptions,
            ct);
        blockedActivateResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Stage two startup documents for SITE-102 in eTMF -> webhook reports missing unapproved artifacts.
        var stage1572Resp = await etmfSponsorClient.PostAsJsonAsync(
            "/api/v1/etmf/intelligence/stage-qc",
            new StageDocumentQCRequest(
                Content: "FDA Form 1572 signed by Principal Investigator at SITE-102",
                Filename: "FDA_Form_1572_SITE_102.pdf",
                StudyId: StudyId,
                SiteId: SiteId,
                ReasonForChange: Reason),
            ct);
        stage1572Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var staged1572 = await stage1572Resp.Content.ReadFromJsonAsync<CraqcStagingItem>(ct);
        staged1572.Should().NotBeNull();

        var stageIrbResp = await etmfSponsorClient.PostAsJsonAsync(
            "/api/v1/etmf/intelligence/stage-qc",
            new StageDocumentQCRequest(
                Content: "IRB approval notification letter for SITE-102",
                Filename: "IRB_Approval_SITE_102.pdf",
                StudyId: StudyId,
                SiteId: SiteId,
                ReasonForChange: Reason),
            ct);
        stageIrbResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var stagedIrb = await stageIrbResp.Content.ReadFromJsonAsync<CraqcStagingItem>(ct);
        stagedIrb.Should().NotBeNull();

        var pendingStatus = await (await executionSponsorClient.GetAsync(
                $"/api/v1/execution/sites/{SiteId}/compliance-status?study_id={StudyId}", ct))
            .Content.ReadFromJsonAsync<ComplianceStatusResponse>(ExecutionApiFactory.JsonOptions, ct);
        pendingStatus!.IsComplete.Should().BeFalse();
        pendingStatus.MissingDocuments.Should().Contain(["FDA Form 1572", "IRB/IEC Approval Notification"]);

        // 3. Sponsor DM reviews and approves first document -> still 1 missing artifact (IRB/IEC Approval Notification).
        var review1572Resp = await etmfSponsorClient.PostAsJsonAsync(
            $"/api/v1/etmf/intelligence/qc-review/{staged1572!.DocumentId}",
            new CraqcReviewRequest("ACCEPT", Reason),
            ct);
        review1572Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var partialStatus = await (await executionSponsorClient.GetAsync(
                $"/api/v1/execution/sites/{SiteId}/compliance-status?study_id={StudyId}", ct))
            .Content.ReadFromJsonAsync<ComplianceStatusResponse>(ExecutionApiFactory.JsonOptions, ct);
        partialStatus!.IsComplete.Should().BeFalse();
        partialStatus.MissingDocuments.Should().Equal("IRB/IEC Approval Notification");

        // 4. Sponsor DM reviews and approves second document -> SITE_ACTIVATION becomes IsComplete = true!
        var reviewIrbResp = await etmfSponsorClient.PostAsJsonAsync(
            $"/api/v1/etmf/intelligence/qc-review/{stagedIrb!.DocumentId}",
            new CraqcReviewRequest("ACCEPT", Reason),
            ct);
        reviewIrbResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeStatus = await (await executionSponsorClient.GetAsync(
                $"/api/v1/execution/sites/{SiteId}/compliance-status?study_id={StudyId}", ct))
            .Content.ReadFromJsonAsync<ComplianceStatusResponse>(ExecutionApiFactory.JsonOptions, ct);
        completeStatus!.IsComplete.Should().BeTrue();
        completeStatus.MissingDocuments.Should().BeEmpty();

        // 5. Sponsor activates SITE-102 -> 200 OK.
        var activateResp = await executionSponsorClient.PostAsJsonAsync(
            $"/api/v1/execution/sites/{SiteId}/activate",
            new SiteActivationRequest(StudyId),
            ExecutionApiFactory.JsonOptions,
            ct);
        activateResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Verify that updating SITE_ACTIVATION completeness unblocks SubjectApplicationService enrollment
        // and RandomizationApplicationService randomization for SITE-102.
        await AssertEnrollmentAndRandomizationUnblockedForSite102Async(completeStatus, ct);
    }

    [Fact]
    public async Task SponsorApprovingSiteArtifactsInEtmf_PropagatesSignedCallerTenantScope_IsolatingOtherTenants()
    {
        var ct = TestContext.Current.CancellationToken;
        const string tenantAlpha = "tenant-alpha";
        const string tenantBeta = "tenant-beta";

        var alphaSponsor = new GatewayIdentity(
            UserId: "sponsor-dm-alpha",
            Roles: ["Data Manager"],
            SiteIds: [SiteId],
            TenantId: tenantAlpha,
            ChangeReason: Reason,
            StudyIds: [StudyId]);

        var betaSponsor = new GatewayIdentity(
            UserId: "sponsor-dm-beta",
            Roles: ["Data Manager"],
            SiteIds: [SiteId],
            TenantId: tenantBeta,
            ChangeReason: Reason,
            StudyIds: [StudyId]);

        using var etmfAlphaClient = _etmfFactory.CreateSignedClient(alphaSponsor);
        using var execAlphaClient = _executionFactory.CreateSignedClient(alphaSponsor);
        using var execBetaClient = _executionFactory.CreateSignedClient(betaSponsor);

        var stageResp = await etmfAlphaClient.PostAsJsonAsync(
            "/api/v1/etmf/intelligence/stage-qc",
            new StageDocumentQCRequest(
                Content: "FDA Form 1572 signed by Principal Investigator at SITE-102",
                Filename: "FDA_Form_1572_SITE_102_Alpha.pdf",
                StudyId: StudyId,
                SiteId: SiteId,
                ReasonForChange: Reason),
            ct);
        stageResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var staged = await stageResp.Content.ReadFromJsonAsync<CraqcStagingItem>(ct);
        staged.Should().NotBeNull();

        var reviewResp = await etmfAlphaClient.PostAsJsonAsync(
            $"/api/v1/etmf/intelligence/qc-review/{staged!.DocumentId}",
            new CraqcReviewRequest("ACCEPT", Reason),
            ct);
        reviewResp.StatusCode.Should().Be(HttpStatusCode.OK);

        _observedWebhookTenants.Should().NotBeEmpty();
        _observedWebhookTenants.Should().OnlyContain(t => t == tenantAlpha);

        var alphaStatus = await (await execAlphaClient.GetAsync(
                $"/api/v1/execution/sites/{SiteId}/compliance-status?study_id={StudyId}", ct))
            .Content.ReadFromJsonAsync<ComplianceStatusResponse>(ExecutionApiFactory.JsonOptions, ct);
        alphaStatus!.IsComplete.Should().BeTrue();

        var betaStatus = await (await execBetaClient.GetAsync(
                $"/api/v1/execution/sites/{SiteId}/compliance-status?study_id={StudyId}", ct))
            .Content.ReadFromJsonAsync<ComplianceStatusResponse>(ExecutionApiFactory.JsonOptions, ct);
        betaStatus!.IsComplete.Should().BeFalse("signed webhook carries caller tenant scope and must not mutate another tenant's site compliance cache");
    }

    [Fact]
    public async Task ReviewingCentralNonStartupDocument_Returns200_SendsZeroWebhooks_AndLeavesSiteComplianceCacheUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        const string studyCode = "CADENCE-CENTRAL-SKIP";
        string[] studySites = ["SITE-201", "SITE-202", "SITE-203"];

        // Seed study-level SITE_ACTIVATION expectation ("Clinical Trial Protocol") and site-specific expectations.
        using (var warm = _etmfFactory.CreateSignedClient())
        using (var scope = _etmfFactory.Services.CreateScope())
        {
            var etmfDb = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            etmfDb.TmfExpectedDocuments.Add(new TmfExpectedDocument
            {
                StudyId = studyCode,
                SiteId = null,
                Milestone = "SITE_ACTIVATION",
                Zone = 1,
                Section = "01.01",
                ArtifactType = "Clinical Trial Protocol",
            });
            foreach (string site in studySites)
            {
                etmfDb.TmfExpectedDocuments.Add(new TmfExpectedDocument
                {
                    StudyId = studyCode,
                    SiteId = site,
                    Milestone = "SITE_ACTIVATION",
                    Zone = 5,
                    Section = "05.02",
                    ArtifactType = "FDA Form 1572",
                });
            }

            // Stage a central non-startup document ("Data Management Plan" / "10.01.01") in TECHNICAL_QC.
            etmfDb.TmfDocuments.Add(CreateTestDocument(
                id: "central-non-startup-doc",
                studyCode: studyCode,
                siteId: null,
                filename: "Data_Management_Plan_v1.pdf",
                content: "Central non-startup DMP document",
                status: TmfDocumentLifecycleState.TechnicalQc,
                zone: 10,
                section: "10.01",
                artifactCode: "10.01.01",
                artifactType: "Data Management Plan"));
            await etmfDb.SaveChangesAsync(ct);
        }

        var sponsorIdentity = new GatewayIdentity(
            UserId: "sponsor-dm-central",
            Roles: ["Data Manager"],
            SiteIds: studySites,
            TenantId: TestSecurityContext.TenantCode,
            ChangeReason: Reason,
            StudyIds: [studyCode]);

        using var etmfClient = _etmfFactory.CreateSignedClient(sponsorIdentity);
        _observedWebhookRequests.Clear();

        var reviewResp = await etmfClient.PostAsJsonAsync(
            "/api/v1/etmf/intelligence/qc-review/central-non-startup-doc",
            new CraqcReviewRequest("ACCEPT", Reason),
            ct);
        reviewResp.StatusCode.Should().Be(HttpStatusCode.OK);

        _observedWebhookRequests.Should().BeEmpty("reviewing a central document outside SITE_ACTIVATION expectations must send zero webhooks");

        using var execScope = _executionFactory.Services.CreateSeedingScope();
        var execDb = execScope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var touched = await execDb.SiteComplianceCaches
            .AsNoTracking()
            .Where(c => c.StudyCode == new StudyCode(studyCode))
            .ToListAsync(ct);
        touched.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviewingCentralStartupDocument_AndOverride_SendsSingleBatchWebhook_AndPreservesUnchangedSiteVersionsAndAuditRows()
    {
        var ct = TestContext.Current.CancellationToken;
        const string studyCode = "CADENCE-CENTRAL-BATCH";
        string[] studySites = ["SITE-301", "SITE-302", "SITE-303"];

        // Seed Execution baseline: SITE-301 already has exact incomplete state ["Clinical Trial Protocol"] (Version=1),
        // SITE-302 has stale state ["Clinical Trial Protocol", "FDA Form 1572"] (Version=1), and SITE-303 has no row yet.
        var sponsorIdentity = new GatewayIdentity(
            UserId: "sponsor-dm-batch",
            Roles: ["Data Manager"],
            SiteIds: studySites,
            TenantId: TestSecurityContext.TenantCode,
            ChangeReason: Reason,
            StudyIds: [studyCode]);

        var etmfSvcIdentity = new GatewayIdentity(
            UserId: "svc:etmf",
            Roles: [PermissionMatrix.EtmfServiceRole],
            SiteIds: studySites,
            TenantId: TestSecurityContext.TenantCode,
            ChangeReason: Reason,
            StudyIds: [studyCode]);

        using var execEtmfClient = _executionFactory.CreateSignedClient(etmfSvcIdentity);
        using var execSponsorClient = _executionFactory.CreateSignedClient(sponsorIdentity);
        using var etmfSponsorClient = _etmfFactory.CreateSignedClient(sponsorIdentity);

        (await execEtmfClient.PostAsJsonAsync(
            "/api/v1/execution/webhooks/etmf/batch",
            new EtmfCompletenessBatchWebhookPayload(
                new StudyCode(studyCode),
                "SITE_ACTIVATION",
                [
                    new EtmfSiteCompletenessItem(new SiteCode("SITE-301"), IsComplete: false, MissingArtifacts: ["Clinical Trial Protocol"]),
                    new EtmfSiteCompletenessItem(new SiteCode("SITE-302"), IsComplete: false, MissingArtifacts: ["Clinical Trial Protocol", "FDA Form 1572"]),
                ]),
            ExecutionApiFactory.JsonOptions,
            ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Seed eTMF: study-level expected "Clinical Trial Protocol", site-level expected "FDA Form 1572" for SITE-301/302/303,
        // approved 1572 for SITE-301 and SITE-302 (not SITE-303), plus two central docs in TECHNICAL_QC.
        using (var scope = _etmfFactory.Services.CreateScope())
        {
            var etmfDb = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            etmfDb.TmfExpectedDocuments.Add(new TmfExpectedDocument
            {
                StudyId = studyCode,
                SiteId = null,
                Milestone = "SITE_ACTIVATION",
                Zone = 1,
                Section = "01.01",
                ArtifactType = "Clinical Trial Protocol",
            });

            foreach (string site in studySites)
            {
                etmfDb.TmfExpectedDocuments.Add(new TmfExpectedDocument
                {
                    StudyId = studyCode,
                    SiteId = site,
                    Milestone = "SITE_ACTIVATION",
                    Zone = 5,
                    Section = "05.02",
                    ArtifactType = "FDA Form 1572",
                });
            }

            foreach (string readySite in new[] { "SITE-301", "SITE-302" })
            {
                etmfDb.TmfDocuments.Add(CreateTestDocument(
                    id: $"1572-{readySite}",
                    studyCode: studyCode,
                    siteId: readySite,
                    filename: $"1572_{readySite}.pdf",
                    content: "Approved FDA Form 1572",
                    status: TmfDocumentLifecycleState.Approved,
                    zone: 5,
                    section: "05.02",
                    artifactCode: "05.02.02",
                    artifactType: "FDA Form 1572"));
            }

            // Doc 1: staged as Clinical Trial Protocol, will be OVERRIDDEN away to 10.01.01 (pre-override taxonomy matches startup!).
            etmfDb.TmfDocuments.Add(CreateTestDocument(
                id: "central-override-away-doc",
                studyCode: studyCode,
                siteId: null,
                filename: "Misclassified_Protocol.pdf",
                content: "Misclassified protocol document",
                status: TmfDocumentLifecycleState.TechnicalQc,
                zone: 1,
                section: "01.01",
                artifactCode: "01.01.01",
                artifactType: "Clinical Trial Protocol"));

            // Doc 2: staged as misclassified 10.01.01, will be OVERRIDDEN to 01.01.01 (Clinical Trial Protocol).
            etmfDb.TmfDocuments.Add(CreateTestDocument(
                id: "central-protocol-doc",
                studyCode: studyCode,
                siteId: null,
                filename: "Master_Protocol_v1.pdf",
                content: "Master clinical trial protocol",
                status: TmfDocumentLifecycleState.TechnicalQc,
                zone: 10,
                section: "10.01",
                artifactCode: "10.01.01",
                artifactType: "Data Management Plan"));

            await etmfDb.SaveChangesAsync(ct);
        }

        _observedWebhookRequests.Clear();

        // Step 1: Override Doc 1 away from Clinical Trial Protocol -> pre-override taxonomy still triggers 1 batch sync:
        // SITE-301 is UNCHANGED (still missing ["Clinical Trial Protocol"]), SITE-302 updates from 2 missing -> 1 missing, SITE-303 is created!
        var overrideAwayResp = await etmfSponsorClient.PostAsJsonAsync(
            "/api/v1/etmf/intelligence/qc-review/central-override-away-doc",
            new CraqcReviewRequest("OVERRIDE", Reason, OverrideArtifactCode: "10.01.01"),
            ct);
        overrideAwayResp.StatusCode.Should().Be(HttpStatusCode.OK);

        _observedWebhookRequests.Should().ContainSingle()
            .Which.Should().Be((HttpMethod.Post, "/api/v1/execution/webhooks/etmf/batch"));

        using (var execScope = _executionFactory.Services.CreateSeedingScope())
        {
            var execDb = execScope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var caches = await execDb.SiteComplianceCaches.AsNoTracking()
                .Where(c => c.StudyCode == new StudyCode(studyCode))
                .ToListAsync(ct);
            caches.Should().HaveCount(3);

            var c301 = caches.Single(c => c.SiteCode == new SiteCode("SITE-301"));
            var c302 = caches.Single(c => c.SiteCode == new SiteCode("SITE-302"));
            var c303 = caches.Single(c => c.SiteCode == new SiteCode("SITE-303"));

            c301.Version.Should().Be(1, "SITE-301 state did not change so Version must remain 1");
            c302.Version.Should().Be(2, "SITE-302 missing documents changed so Version incremented to 2");
            c303.Version.Should().Be(1, "SITE-303 was created at Version 1");

            var periods301 = await execDb.SiteActivationPeriods.AsNoTracking()
                .CountAsync(p => p.SiteComplianceCacheId == c301.Id, ct);
            periods301.Should().Be(1, "unchanged SITE-301 must not append duplicate SiteActivationPeriod rows");

            var audits301 = await execDb.AuditLogEntries.AsNoTracking()
                .CountAsync(a => a.Action == "ETMF_COMPLIANCE_UPDATE" && a.EntityId == c301.Id.ToString(), ct);
            audits301.Should().Be(1, "unchanged SITE-301 must not append duplicate ETMF_COMPLIANCE_UPDATE audit entries");
        }

        _observedWebhookRequests.Clear();

        // Step 2: Override Doc 2 TO 01.01.01 (Clinical Trial Protocol) -> 1 batch sync flips SITE-301 and SITE-302 to IsComplete = true!
        var approveProtocolResp = await etmfSponsorClient.PostAsJsonAsync(
            "/api/v1/etmf/intelligence/qc-review/central-protocol-doc",
            new CraqcReviewRequest("OVERRIDE", Reason, OverrideArtifactCode: "01.01.01"),
            ct);
        approveProtocolResp.StatusCode.Should().Be(HttpStatusCode.OK);

        _observedWebhookRequests.Should().ContainSingle()
            .Which.Should().Be((HttpMethod.Post, "/api/v1/execution/webhooks/etmf/batch"));

        var s301 = await (await execSponsorClient.GetAsync(
                $"/api/v1/execution/sites/SITE-301/compliance-status?study_id={studyCode}", ct))
            .Content.ReadFromJsonAsync<ComplianceStatusResponse>(ExecutionApiFactory.JsonOptions, ct);
        s301!.IsComplete.Should().BeTrue();

        var s302 = await (await execSponsorClient.GetAsync(
                $"/api/v1/execution/sites/SITE-302/compliance-status?study_id={studyCode}", ct))
            .Content.ReadFromJsonAsync<ComplianceStatusResponse>(ExecutionApiFactory.JsonOptions, ct);
        s302!.IsComplete.Should().BeTrue();

        var s303 = await (await execSponsorClient.GetAsync(
                $"/api/v1/execution/sites/SITE-303/compliance-status?study_id={studyCode}", ct))
            .Content.ReadFromJsonAsync<ComplianceStatusResponse>(ExecutionApiFactory.JsonOptions, ct);
        s303!.IsComplete.Should().BeFalse();
        s303.MissingDocuments.Should().Equal("FDA Form 1572");
    }

    [Fact]
    public async Task MultiSiteCentralStartupEvaluation_UsesProjectedSqlSingleConnectionScopeAndConstantBatchScaling_Across10And150Sites()
    {
        var ct = TestContext.Current.CancellationToken;

        var smallMetrics = await RunMultiSiteRelationalScalingScenarioAsync(siteCount: 10, ct);
        var largeMetrics = await RunMultiSiteRelationalScalingScenarioAsync(siteCount: 150, ct);

        // Constant O(1) SQL queries, connection opens, Execution batch webhooks, and Kernel batch lock checks across 10 -> 150 sites.
        smallMetrics.EtmfSelectCount.Should().Be(2);
        largeMetrics.EtmfSelectCount.Should().Be(2);

        smallMetrics.EtmfConnectionOpenCount.Should().Be(1);
        largeMetrics.EtmfConnectionOpenCount.Should().Be(1);

        smallMetrics.ExecutionWebhookRequests.Should().Be(1);
        largeMetrics.ExecutionWebhookRequests.Should().Be(1);

        smallMetrics.KernelLockBatchRequests.Should().Be(1);
        largeMetrics.KernelLockBatchRequests.Should().Be(1);

        foreach (string sql in smallMetrics.ExecutedSql.Concat(largeMetrics.ExecutedSql))
        {
            sql.Should().NotContainEquivalentOf("content", "projected completeness queries must not load binary document content");
            sql.Should().NotContainEquivalentOf("metadata_json", "projected completeness queries must not load JSON metadata blobs");
            sql.Should().NotContainEquivalentOf("signature_manifestation", "projected completeness queries must not load signature blobs");
            sql.Should().NotContainEquivalentOf("redaction_manifest_json", "projected completeness queries must not load redaction blobs");
        }
    }

    private static async Task<(int EtmfSelectCount, int EtmfConnectionOpenCount, int ExecutionWebhookRequests, int KernelLockBatchRequests, IReadOnlyList<string> ExecutedSql)>
        RunMultiSiteRelationalScalingScenarioAsync(int siteCount, CancellationToken ct)
    {
        string studyCode = $"STUDY-SCALE-{siteCount}";
        var sqlRecorder = new SqlAndConnectionRecorder();

        // Keep an underlying SQLite memory database alive via a shared named in-memory URI so OpenConnectionAsync/CloseConnectionAsync
        // exercises real relational connection open/close lifecycle per evaluation.
        string connString = $"Data Source=etmf_scale_{siteCount}_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keepAliveConnection = new SqliteConnection(connString);
        await keepAliveConnection.OpenAsync(ct);

        var dbOptions = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseSqlite(connString)
            .AddInterceptors(sqlRecorder)
            .Options;

        await using (var seedDb = new EtmfDbContext(dbOptions))
        {
            await seedDb.Database.EnsureCreatedAsync(ct);
            seedDb.TmfExpectedDocuments.Add(new TmfExpectedDocument
            {
                StudyId = studyCode,
                SiteId = null,
                Milestone = "SITE_ACTIVATION",
                Zone = 1,
                Section = "01.01",
                ArtifactType = "Clinical Trial Protocol",
                MetadataJson = "{\"large_expected_meta\":true}",
            });

            string largePayload = new('X', 16_384);

            seedDb.TmfDocuments.Add(CreateTestDocument(
                id: $"central-proto-{siteCount}",
                studyCode: studyCode,
                siteId: null,
                filename: "Protocol.pdf",
                content: largePayload,
                status: TmfDocumentLifecycleState.Approved,
                zone: 1,
                section: "01.01",
                artifactCode: "01.01.01",
                artifactType: "Clinical Trial Protocol",
                metadataJson: "{\"extracted\":\"protocol\"}",
                signatureManifestation: "{\"sig\":\"valid\"}",
                redactionManifestJson: "[{\"box\":1}]"));

            for (int i = 1; i <= siteCount; i++)
            {
                string site = $"SITE-{i:D4}";
                seedDb.TmfExpectedDocuments.Add(new TmfExpectedDocument
                {
                    StudyId = studyCode,
                    SiteId = site,
                    Milestone = "SITE_ACTIVATION",
                    Zone = 5,
                    Section = "05.02",
                    ArtifactType = "FDA Form 1572",
                });
                seedDb.TmfDocuments.Add(CreateTestDocument(
                    id: $"doc-{siteCount}-{i:D4}",
                    studyCode: studyCode,
                    siteId: site,
                    filename: $"1572_{site}.pdf",
                    content: largePayload,
                    status: i % 2 == 0 ? TmfDocumentLifecycleState.Approved : TmfDocumentLifecycleState.TechnicalQc,
                    zone: 5,
                    section: "05.02",
                    artifactCode: "05.02.02",
                    artifactType: "FDA Form 1572",
                    metadataJson: "{\"site_doc\":true}",
                    signatureManifestation: "{\"sig\":\"pi\"}",
                    redactionManifestJson: "[]"));
            }

            await seedDb.SaveChangesAsync(ct);
        }

        int kernelLockBatchRequests = 0;
        using var kernelHandler = new CountingStubHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == "/api/v1/kernel/locks/in-force/batch")
            {
                Interlocked.Increment(ref kernelLockBatchRequests);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"study_code":"{{studyCode}}","locked":false}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        using var execFactory = new ExecutionApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IKernelLockGuard>();
                services.AddScoped<IKernelLockGuard>(sp => new HttpKernelLockGuard(
                    new HttpClient(kernelHandler, disposeHandler: false) { BaseAddress = new Uri("https://kernel.internal") },
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    sp.GetRequiredService<IOptionsMonitor<GatewayAuthenticationOptions>>(),
                    sp.GetRequiredService<IClock>()));
            });
        });

        var observedTenants = new ConcurrentQueue<string?>();
        var observedWebhookRequests = new ConcurrentQueue<(HttpMethod Method, string Path)>();
        using var execForwarder = new RecordingForwardingHandler(
            execFactory.Server.CreateHandler(),
            observedTenants,
            observedWebhookRequests);
        using var webhookHttpClient = new HttpClient(execForwarder) { BaseAddress = new Uri("http://localhost") };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GATEWAY_SECRET"] = TestSecurityContext.GatewaySecret,
            })
            .Build();
        var execClock = execFactory.Services.GetRequiredService<IClock>();
        var publisher = new HttpEtmfCompletenessWebhookPublisher(
            webhookHttpClient,
            new StaticGatewayOptions(TestSecurityContext.GatewaySecret),
            config,
            execClock,
            NullLogger<HttpEtmfCompletenessWebhookPublisher>.Instance);

        sqlRecorder.Reset();

        await using (var evalDb = new EtmfDbContext(dbOptions))
        {
            await publisher.PublishSiteActivationCompletenessAsync(
                evalDb,
                studyCode,
                siteId: null,
                tenantId: TestSecurityContext.TenantCode,
                reasonForChange: Reason,
                reviewContext: new EtmfDocumentReviewContext(
                    Current: new EtmfArtifactTaxonomy("Clinical Trial Protocol", "01.01.01")),
                ct: ct);
        }

        using (var verifyScope = execFactory.Services.CreateSeedingScope())
        {
            var execDb = verifyScope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            int persistedCount = await execDb.SiteComplianceCaches.AsNoTracking()
                .CountAsync(c => c.StudyCode == new StudyCode(studyCode), ct);
            persistedCount.Should().Be(siteCount);
        }

        return (
            sqlRecorder.SelectCommands.Count,
            sqlRecorder.ConnectionOpenCount,
            observedWebhookRequests.Count,
            kernelLockBatchRequests,
            sqlRecorder.SelectCommands.ToList());
    }

    private static TmfDocument CreateTestDocument(
        string id,
        string studyCode,
        string? siteId,
        string filename,
        string content,
        string status,
        int zone,
        string section,
        string artifactCode,
        string artifactType,
        string? metadataJson = null,
        string? signatureManifestation = null,
        string? redactionManifestJson = null)
    {
        var stamp = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        var doc = new TmfDocument(id)
        {
            StudyId = studyCode,
            SiteId = siteId,
            Filename = filename,
            Content = content,
            Zone = zone,
            Section = section,
            ArtifactCode = artifactCode,
            ArtifactType = artifactType,
            MetadataJson = metadataJson,
            SignatureManifestation = signatureManifestation,
            RedactionManifestJson = redactionManifestJson,
        };

        if (!string.Equals(status, TmfDocumentLifecycleState.Draft, StringComparison.OrdinalIgnoreCase))
        {
            doc.TransitionTo(TmfDocumentLifecycleState.TechnicalQc, "seed-cra", "CRA", Reason, stamp, 1);
        }

        if (string.Equals(status, TmfDocumentLifecycleState.Approved, StringComparison.OrdinalIgnoreCase))
        {
            doc.TransitionTo(TmfDocumentLifecycleState.Approved, "seed-dm", "Data Manager", Reason, stamp, 2);
        }

        return doc;
    }

    private async Task AssertEnrollmentAndRandomizationUnblockedForSite102Async(
        ComplianceStatusResponse syncedStatus,
        CancellationToken ct)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var study = new StudyCode(StudyId);
        var site = new SiteCode(SiteId);
        var subjectCode = new SubjectCode("SUBJ-102-001");

        var subjects = new InMemorySubjectRepository();
        var consents = new InMemoryConsentRepository();
        var compliance = new InMemorySiteComplianceRepository();
        var encryption = new FakeDemographicsEncryptionService();
        var auditLog = new RecordingAuditLogWriter();
        var auditContext = new FakeCurrentAuditContextAccessor { UserId = "crc-site-102", ChangeReason = Reason };
        var randomizationRepo = new InMemoryRandomizationRepository();

        using var scope = _executionFactory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IAllocationProtector>();

        var subjectService = new SubjectApplicationService(
            subjects, consents, compliance, encryption, auditLog, auditContext, screeningEligibility: null, clock);
        var randomizationService = new RandomizationApplicationService(
            randomizationRepo, subjects, consents, compliance, protector, auditLog, auditContext, clock);

        var subject = ClinicalSubject.Create(
            subjectCode, study, site, enrollmentIndex: 0, encryptedDemographics: null, stratFactors: null);
        await subjects.AddAsync(subject, ct);

        var consent = SubjectConsent.Create(
            subjectCode,
            new ProtocolVersionRef(study, "v1.0", 1),
            icfSigned: true,
            icfSignedDate: clock.UtcNow,
            requiresReconsent: false,
            isPaperOverride: false);
        await consents.AddAsync(consent, ct);

        var scheme = RandomizationScheme.Draft(
            study,
            version: 1,
            RandomizationMethod.Minimization,
            new Dictionary<string, int> { ["PLACEBO"] = 1, ["ACTIVE"] = 1 },
            new Dictionary<string, IReadOnlyList<string>> { ["sex"] = ["F", "M"] },
            blockSizes: null,
            slotsPerStratum: 0,
            preferredArmProbability: 0.8,
            requiresKit: false,
            draftedBy: "stat-a",
            reasonForChange: Reason,
            clock: clock).Value!;
        scheme.Approve("stat-b", Reason, clock).IsSuccess.Should().BeTrue();
        await randomizationRepo.AddSchemeAsync(scheme, ct);

        // Before applying the synced eTMF completeness row, enrollment is blocked for SITE-102.
        var blockedEnroll = await subjectService.AdvanceStateAsync(
            subjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null),
            Reason,
            ct);
        blockedEnroll.IsFailure.Should().BeTrue();
        blockedEnroll.Error!.Code.Should().Be("BLOCKED_ENROLLMENT");

        // Apply the synced SITE_ACTIVATION state delivered by the eTMF webhook.
        await compliance.AddAsync(
            SiteComplianceCache.Create(
                syncedStatus.StudyId,
                syncedStatus.SiteId!.Value,
                syncedStatus.Milestone,
                syncedStatus.IsComplete,
                syncedStatus.MissingDocuments),
            ct);

        // Enrollment in SubjectApplicationService now succeeds for SITE-102.
        var enrolled = await subjectService.AdvanceStateAsync(
            subjectCode.Value,
            new SubjectStateUpdateRequest(Status: "ENROLLED", State: null),
            Reason,
            ct);
        enrolled.IsSuccess.Should().BeTrue();
        enrolled.Value!.Status.Should().Be("ENROLLED");

        // Randomization in RandomizationApplicationService now succeeds for SITE-102.
        var randomized = await randomizationService.RandomizeAsync(
            subjectCode.Value,
            new RandomizeSubjectRequest(new Dictionary<string, string> { ["sex"] = "F" }),
            Reason,
            ct);
        randomized.IsSuccess.Should().BeTrue();
        randomized.Value!.Status.Should().Be("RANDOMIZED");
    }

    private sealed class RecordingForwardingHandler(
        HttpMessageHandler inner,
        ConcurrentQueue<string?> observedTenants,
        ConcurrentQueue<(HttpMethod Method, string Path)>? observedRequests = null) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues(GatewayHeaders.TenantId, out var values);
            observedTenants.Enqueue(values?.FirstOrDefault());
            observedRequests?.Enqueue((request.Method, request.RequestUri?.AbsolutePath ?? string.Empty));
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class CountingStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class StaticGatewayOptions(string secret) : IOptionsMonitor<GatewayAuthenticationOptions>
    {
        public GatewayAuthenticationOptions CurrentValue { get; } = new() { GatewaySecret = secret };

        public GatewayAuthenticationOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<GatewayAuthenticationOptions, string?> listener) => null;
    }

    private sealed class SqlAndConnectionRecorder : DbCommandInterceptor, IDbConnectionInterceptor
    {
        public ConcurrentQueue<string> SelectCommands { get; } = new();

        public int ConnectionOpenCount;

        public void Reset()
        {
            SelectCommands.Clear();
            Interlocked.Exchange(ref ConnectionOpenCount, 0);
        }

        public void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
            Interlocked.Increment(ref ConnectionOpenCount);

        public Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ConnectionOpenCount);
            return Task.CompletedTask;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            RecordIfSelect(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            RecordIfSelect(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void RecordIfSelect(string sql)
        {
            if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                SelectCommands.Enqueue(sql);
            }
        }
    }

    private sealed class InMemoryRandomizationRepository : IRandomizationRepository
    {
        private readonly List<RandomizationScheme> _schemes = [];
        private readonly List<RandomizationListSlot> _slots = [];
        private readonly List<RandomizationAllocation> _allocations = [];

        public Task LockStudyAsync(StudyCode studyCode, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LockSchemeAsync(Guid schemeId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<RandomizationScheme>> ListSchemesAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RandomizationScheme>>(
                _schemes.Where(s => s.StudyCode == studyCode).OrderByDescending(s => s.Version).ToList());

        public Task<RandomizationScheme?> GetSchemeAsync(Guid schemeId, CancellationToken cancellationToken) =>
            Task.FromResult(_schemes.FirstOrDefault(s => s.Id == schemeId));

        public Task<RandomizationScheme?> GetApprovedSchemeAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
            Task.FromResult(_schemes.FirstOrDefault(s => s.StudyCode == studyCode && s.Status == RandomizationSchemeStatus.Approved));

        public Task<int> GetLatestVersionAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
            Task.FromResult(_schemes.Where(s => s.StudyCode == studyCode).Select(s => s.Version).DefaultIfEmpty(0).Max());

        public Task AddSchemeAsync(RandomizationScheme scheme, CancellationToken cancellationToken)
        {
            _schemes.Add(scheme);
            return Task.CompletedTask;
        }

        public Task AddListSlotsAsync(IReadOnlyCollection<RandomizationListSlot> slots, CancellationToken cancellationToken)
        {
            _slots.AddRange(slots);
            return Task.CompletedTask;
        }

        public Task<RandomizationListSlot?> GetNextFreeSlotAsync(Guid schemeId, string stratumKey, CancellationToken cancellationToken) =>
            Task.FromResult(_slots
                .Where(s => s.SchemeId == schemeId && s.StratumKey == stratumKey && s.AllocationId is null)
                .OrderBy(s => s.Sequence)
                .FirstOrDefault());

        public Task<RandomizationAllocation?> GetAllocationForSubjectAsync(SubjectId subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(_allocations.FirstOrDefault(a => a.SubjectId == subjectId));

        public Task<IReadOnlyList<RandomizationAllocation>> ListAllocationsAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RandomizationAllocation>>(_allocations.Where(a => a.StudyCode == studyCode).ToList());

        public Task AddAllocationAsync(RandomizationAllocation allocation, CancellationToken cancellationToken)
        {
            _allocations.Add(allocation);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IpKitInventoryItem>> ListAssignableKitsAsync(
            StudyCode studyCode, SiteCode siteCode, DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IpKitInventoryItem>>([]);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> unitOfWork, CancellationToken cancellationToken) =>
            unitOfWork(cancellationToken);
    }
}
