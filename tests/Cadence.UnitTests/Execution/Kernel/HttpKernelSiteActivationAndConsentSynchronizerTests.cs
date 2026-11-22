using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Consumers;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Core.Domain;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using MassTransit;
using MassTransit.Testing;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Kernel;

/// <summary>
/// Unit tests for <see cref="HttpKernelSiteActivationSynchronizer"/>, <see cref="HttpKernelConsentSynchronizer"/>,
/// and their integration into <see cref="SiteComplianceApplicationService"/>, <see cref="SubjectApplicationService"/>,
/// and <see cref="EConsentCompletedConsumer"/> (<c>PRD-SSU-003</c>, <c>PRD-SUB-007</c>).
/// </summary>
public sealed class HttpKernelSiteActivationAndConsentSynchronizerTests
{
    private static readonly string GatewaySigningKey = new('b', 64);
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public async Task SiteActivationSynchronizer_SignsGatewayHeadersAndPostsStudyAndSiteCodes()
    {
        var clock = new FakeClock(FixedNow);
        var tenantId = Guid.NewGuid().ToString();
        HttpRequestMessage? captured = null;
        string? capturedBody = null;

        using var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            captured = req;
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"study_site_id":"01977300-0000-7000-8000-000000000001","status":"Active"}""", Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var context = BuildHttpContext(tenantId, "sponsor.dm", ["data_manager"], ["SITE-101"], ["CADENCE-101"]);
        var sync = new HttpKernelSiteActivationSynchronizer(
            client,
            new HttpContextAccessor { HttpContext = context },
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var result = await sync.SynchronizeSiteActivationAsync(
            "CADENCE-101", "SITE-101", "Activate site after greenlight certification", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.RequestUri!.PathAndQuery.Should().Be("/api/v1/kernel/study-sites/activate");
        captured.Headers.GetValues("Idempotency-Key").Should().ContainSingle("execution-site-activate:CADENCE-101:SITE-101");
        captured.Headers.GetValues(GatewayHeaders.TenantId).Should().ContainSingle(tenantId);
        captured.Headers.Contains(GatewayHeaders.Signature).Should().BeTrue();

        using var payload = JsonDocument.Parse(capturedBody!);
        payload.RootElement.GetProperty("study_code").GetString().Should().Be("CADENCE-101");
        payload.RootElement.GetProperty("site_code").GetString().Should().Be("SITE-101");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-007")]
    public async Task ConsentSynchronizer_WithExplicitTenantCode_SignsServiceAccountHeadersAndPostsSignedConsent()
    {
        var clock = new FakeClock(FixedNow);
        const string tenantCode = "tenant-demo";
        HttpRequestMessage? captured = null;
        string? capturedBody = null;

        using var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            captured = req;
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"consent_status_id":"01977300-0000-7000-8000-000000000002","resolved_transitions":1}""", Encoding.UTF8, "application/json"),
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://kernel.internal") };

        var sync = new HttpKernelConsentSynchronizer(
            client,
            new HttpContextAccessor(),
            new StaticOptionsMonitor<GatewayAuthenticationOptions>(new GatewayAuthenticationOptions { GatewaySecret = GatewaySigningKey }),
            clock);

        var result = await sync.SynchronizeConsentAsync(
            "CADENCE-101",
            "SITE-101",
            "SUBJ-101-001",
            versionIndex: 2,
            versionTag: "2.0",
            signedAt: FixedNow.AddMinutes(-5),
            isPaperOverride: false,
            changeReason: "eConsent completed",
            TestContext.Current.CancellationToken,
            explicitTenantCode: tenantCode);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.RequestUri!.PathAndQuery.Should().Be("/api/v1/kernel/consents/sync");
        captured.Headers.GetValues(GatewayHeaders.TenantId).Should().ContainSingle(tenantCode);
        captured.Headers.Contains(GatewayHeaders.Signature).Should().BeTrue();

        using var payload = JsonDocument.Parse(capturedBody!);
        payload.RootElement.GetProperty("study_code").GetString().Should().Be("CADENCE-101");
        payload.RootElement.GetProperty("site_code").GetString().Should().Be("SITE-101");
        payload.RootElement.GetProperty("subject_code").GetString().Should().Be("SUBJ-101-001");
        payload.RootElement.GetProperty("version_index").GetInt32().Should().Be(2);
        payload.RootElement.GetProperty("version_tag").GetString().Should().Be("2.0");
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public async Task SiteComplianceService_ValidatesGreenlightTokenAndInvokesKernelSiteActivationSynchronizer()
    {
        var clock = new FakeClock(FixedNow);
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase($"site-act-sync-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ExecutionDbContext(options);
        db.SiteComplianceCaches.Add(SiteComplianceCache.Create(
            new StudyCode("CADENCE-101"), new SiteCode("SITE-101"), "SITE_ACTIVATION", isComplete: true, []));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var validator = new GreenlightTokenValidator(GatewaySigningKey, clock);
        var recordingSync = new RecordingSiteActivationSynchronizer();
        var service = new SiteComplianceApplicationService(
            new SiteComplianceRepository(db),
            new EfAuditLogWriter(db),
            new FakeCurrentAuditContextAccessor("sponsor-dm", "Activate site"),
            clock,
            validator,
            recordingSync);

        var invalid = await service.ActivateSiteAsync(
            "SITE-101",
            new SiteActivationRequest("CADENCE-101", GreenlightToken: "bad.sig"),
            TestContext.Current.CancellationToken);
        invalid.IsFailure.Should().BeTrue();
        invalid.Error!.Code.Should().Be("INVALID_GREENLIGHT_TOKEN");
        recordingSync.Calls.Should().BeEmpty();

        long nowUnix = FixedNow.ToUnixTimeSeconds();
        var validToken = GreenlightTokenValidator.GenerateToken(
            "SITE-101", "CADENCE-101", "US", "ctms-dir", GatewaySigningKey, nowUnix, nowUnix + 86400);
        var valid = await service.ActivateSiteAsync(
            "SITE-101",
            new SiteActivationRequest("CADENCE-101", validToken),
            TestContext.Current.CancellationToken);
        valid.IsSuccess.Should().BeTrue();
        recordingSync.Calls.Should().ContainSingle()
            .Which.Should().Be(("CADENCE-101", "SITE-101", "Activate site"));
    }

    [Fact]
    [GxPRequirement("PRD-SUB-007")]
    public async Task SubjectServiceAndEConsentConsumer_SynchronizeSignedConsentToKernel()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(FixedNow);
        var subjects = new InMemorySubjectRepository();
        var consents = new InMemoryConsentRepository();
        var compliance = new InMemorySiteComplianceRepository();
        var encryption = new FakeDemographicsEncryptionService();
        var auditLog = new RecordingAuditLogWriter();
        var auditContext = new FakeCurrentAuditContextAccessor { UserId = "crc-1", ChangeReason = "Paper ICF signed" };
        var consentSync = new RecordingConsentSynchronizer();

        var subject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-101"), new StudyCode("CADENCE-101"), new SiteCode("SITE-101"), 0, null, null);
        await subjects.AddAsync(subject, ct);

        var service = new SubjectApplicationService(
            subjects,
            consents,
            compliance,
            encryption,
            auditLog,
            auditContext,
            screeningEligibility: null,
            clock,
            kernelEnrollmentSynchronizer: null,
            kernelConsentSynchronizer: consentSync);

        // Unsigned non-override consent does not synchronize to Kernel.
        var draft = await service.RecordConsentAsync(
            "SUBJ-101",
            new SubjectConsentRequest(
                new ProtocolVersionPayload("CADENCE-101", "1.0", 1),
                IcfSigned: false,
                IcfSignedDate: null,
                RequiresReconsent: true,
                IsPaperOverride: false),
            ct);
        draft.IsSuccess.Should().BeTrue();
        consentSync.Calls.Should().BeEmpty();

        // Signed re-consent synchronizes to Kernel.
        var signed = await service.RecordConsentAsync(
            "SUBJ-101",
            new SubjectConsentRequest(
                new ProtocolVersionPayload("CADENCE-101", "2.0", 2),
                IcfSigned: true,
                IcfSignedDate: FixedNow,
                RequiresReconsent: false,
                IsPaperOverride: false),
            ct);
        signed.IsSuccess.Should().BeTrue();
        consentSync.Calls.Should().ContainSingle()
            .Which.VersionIndex.Should().Be(2);

        // MassTransit EConsentCompletedConsumer also synchronizes signed eConsent with TenantCode.
        var services = new ServiceCollection();
        services.AddSingleton<Cadence.Apps.Execution.Domain.Ports.ISubjectRepository>(subjects);
        services.AddSingleton<Cadence.Apps.Execution.Domain.Ports.IConsentRepository>(consents);
        services.AddSingleton<IAuditLogWriter>(auditLog);
        services.AddScoped<IAuditActorScope>(_ => new AmbientAuditContextAccessor(new HttpContextAccessor()));
        services.AddSingleton<IKernelConsentSynchronizer>(consentSync);
        services.AddMassTransitTestHarness(x => x.AddConsumer<EConsentCompletedConsumer>());

        await using var provider = services.BuildServiceProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(
                new EConsentCompletedEvent("SUBJ-102", "CADENCE-101", "SITE-101", "3.0", 3, FixedNow, "tenant-demo"),
                ct);
            (await harness.Consumed.Any<EConsentCompletedEvent>(ct)).Should().BeTrue();

            consentSync.Calls.Should().HaveCount(2);
            consentSync.Calls[1].SubjectCode.Should().Be("SUBJ-102");
            consentSync.Calls[1].VersionIndex.Should().Be(3);
            consentSync.Calls[1].ExplicitTenantCode.Should().Be("tenant-demo");
        }
        finally
        {
            await harness.Stop(ct);
        }
    }

    private static DefaultHttpContext BuildHttpContext(
        string tenantId,
        string userId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> sites,
        IReadOnlyList<string> studies)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(CadenceClaimTypes.Subject, userId),
                new Claim(CadenceClaimTypes.TenantId, tenantId),
            ], "CadenceGateway")),
        };
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userId,
            Roles = roles,
            AssignedSites = sites,
            RawRoles = roles,
            AssignedStudies = studies,
        };
        return context;
    }

    private sealed class RecordingSiteActivationSynchronizer : IKernelSiteActivationSynchronizer
    {
        public List<(string StudyCode, string SiteCode, string ChangeReason)> Calls { get; } = [];

        public Task<Result<bool>> SynchronizeSiteActivationAsync(
            string studyCode, string siteCode, string changeReason, CancellationToken cancellationToken)
        {
            Calls.Add((studyCode, siteCode, changeReason));
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    private sealed class RecordingConsentSynchronizer : IKernelConsentSynchronizer
    {
        public List<(string StudyCode, string? SiteCode, string SubjectCode, int VersionIndex, string? VersionTag, string? ExplicitTenantCode)> Calls { get; } = [];

        public Task<Result<bool>> SynchronizeConsentAsync(
            string studyCode,
            string? siteCode,
            string subjectCode,
            int versionIndex,
            string? versionTag,
            DateTimeOffset signedAt,
            bool isPaperOverride,
            string changeReason,
            CancellationToken cancellationToken,
            string? explicitTenantCode = null)
        {
            Calls.Add((studyCode, siteCode, subjectCode, versionIndex, versionTag, explicitTenantCode));
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
