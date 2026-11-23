using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
                    _observedWebhookTenants));

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
        ConcurrentQueue<string?> observedTenants) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues(GatewayHeaders.TenantId, out var values);
            observedTenants.Enqueue(values?.FirstOrDefault());
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class InMemoryRandomizationRepository : IRandomizationRepository
    {
        private readonly List<RandomizationScheme> _schemes = [];
        private readonly List<RandomizationListSlot> _slots = [];
        private readonly List<RandomizationAllocation> _allocations = [];

        public Task LockStudyAsync(StudyCode studyCode, CancellationToken cancellationToken) => Task.CompletedTask;

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
