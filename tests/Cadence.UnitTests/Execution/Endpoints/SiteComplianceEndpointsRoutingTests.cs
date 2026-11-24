using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application;
using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Compliance;
using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Core.Domain;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using FluentValidation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// `MIG-SUBJ-024` coverage for <see cref="SiteComplianceEndpoints.MapSiteComplianceEndpoints"/> --
/// one routing-shape test per route/filter behavior, mirroring <c>SubjectEndpointsRoutingTests</c>'
/// (`MIG-SUBJ-023`) approach exactly, per this ticket's own Verification &amp; Test Requirements. These
/// tests host <see cref="SiteComplianceEndpoints.MapSiteComplianceEndpoints"/> behind an independent
/// minimal host with a <see cref="FakeSiteComplianceApplicationService"/> test double, so a failure here
/// always points at endpoint/filter wiring, never at <c>SiteComplianceApplicationService</c>'s own business
/// logic (already covered by <c>SiteComplianceApplicationServiceTests</c>, MIG-SUBJ-020).
/// </summary>
public sealed class SiteComplianceEndpointsRoutingTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly SiteComplianceEndpointsTestFactory _factory = new();
    private readonly HttpClient _client;
    private readonly HttpClient _etmf;

    /// <summary>
    /// Site activation and status are called by sponsor operations; the eTMF webhook by the eTMF service's signed
    /// account (ADR-2516).
    /// </summary>
    public SiteComplianceEndpointsRoutingTests()
    {
        _client = _factory.CreateSignedClient(TestSecurityContext.Identity("sponsor-ops", "Data Manager") with
        {
            ChangeReason = "Site activation readiness check",
        });
        _etmf = _factory.CreateSignedClient(TestSecurityContext.Identity("svc:etmf", PermissionMatrix.EtmfServiceRole) with
        {
            ChangeReason = "Automated eTMF completeness webhook sync",
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _etmf.Dispose();
        _factory.Dispose();
    }

    // ================================================================================================
    // POST /api/v1/execution/webhooks/etmf (EtmfCompletenessWebhook)
    // ================================================================================================

    [Fact]
    public async Task EtmfWebhook_GivenValidPayload_Returns200WithExactSuccessEnvelope()
    {
        var payload = new EtmfCompletenessWebhookPayload("STUDY-001", "SITE-001", "SITE_ACTIVATION", true, null);

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/execution/webhooks/etmf", payload, _etmf);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.ApplyEtmfWebhookAsyncCalls.Should().Be(1);
        var body = await response.Content.ReadFromJsonAsync<OperationEnvelopeResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body.Should().Be(new OperationEnvelopeResponse("success", "Compliance cache updated successfully."));
    }

    [Fact]
    public async Task EtmfWebhook_GivenInvalidPayload_ShortCircuitsWith400AndNeverInvokesApplicationService()
    {
        // Neither study_id nor milestone supplied -- fails EtmfCompletenessWebhookPayloadValidator's two
        // NotEmpty() rules. ValidationFilter<T> must short-circuit before ApplyEtmfWebhookAsync is reached.
        var response = await SendRawJsonAsync(HttpMethod.Post, "/api/v1/execution/webhooks/etmf", "{}", _etmf);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Service.ApplyEtmfWebhookAsyncCalls.Should().Be(0);
    }

    [Fact]
    public async Task ApplyEtmfWebhookAsync_GivenPreCancelledToken_ThrowsOperationCanceledException()
    {
        var service = new FakeSiteComplianceApplicationService();
        var payload = new EtmfCompletenessWebhookPayload("STUDY-001", "SITE-001", "SITE_ACTIVATION", true, null);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SiteComplianceEndpoints.ApplyEtmfWebhookAsync(payload, service, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ================================================================================================
    // POST /api/v1/execution/sites/{siteId}/activate (ActivateSite)
    // ================================================================================================

    [Fact]
    public async Task ActivateSite_GivenValidRequest_Returns200WithExactSuccessEnvelopeNamingTheSite()
    {
        var request = new SiteActivationRequest("STUDY-001");

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/execution/sites/SITE-001/activate", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.ActivateSiteAsyncCalls.Should().Be(1);
        _factory.Service.LastActivateSiteId.Should().Be("SITE-001");
        var body = await response.Content.ReadFromJsonAsync<OperationEnvelopeResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body.Should().Be(new OperationEnvelopeResponse("success", "Site SITE-001 activated successfully."));
    }

    [Fact]
    public async Task ActivateSite_GivenNonCompliantSiteResult_Returns400WithExactBlockedActivationDetail()
    {
        // This ticket's AC #5, exercised at the routing layer: SubjectErrors.BlockedActivation's own literal
        // text is asserted directly against SubjectErrorsTests/DomainErrorHttpMappingTests elsewhere; this
        // test's job is only to prove the endpoint's .Match(...) wiring actually surfaces whatever
        // ISiteComplianceApplicationService.ActivateSiteAsync returns via ToProblemResult() unchanged. The
        // real, end-to-end (non-faked) version of this exact scenario is covered by
        // ExecutionProgramStartupTests.ActivateSite_EndToEnd_NonCompliantSite_Returns400WithExactBlockedActivationDetail.
        _factory.Service.ActivateSiteResult = SubjectErrors.BlockedActivation("SITE-002");
        var request = new SiteActivationRequest("STUDY-001");

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/execution/sites/SITE-002/activate", request);

        await AssertProblemDetailAsync(response, HttpStatusCode.BadRequest,
            "Site activation blocked: Site SITE-002 is not compliant in eTMF. Missing expected documents.");
    }

    [Fact]
    public async Task ActivateSiteAsync_GivenPreCancelledToken_ThrowsOperationCanceledException()
    {
        var service = new FakeSiteComplianceApplicationService();
        var request = new SiteActivationRequest("STUDY-001");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SiteComplianceEndpoints.ActivateSiteAsync("SITE-001", request, service, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ================================================================================================
    // GET /api/v1/execution/sites/{siteId}/compliance-status (GetSiteComplianceStatus)
    // ================================================================================================

    [Fact]
    public async Task GetComplianceStatus_GivenStudyIdAndMilestoneQueryParams_ThreadsBothThroughAsSnakeCaseKeys()
    {
        // Proves the [FromQuery(Name = "study_id"/"milestone")] binding actually resolves the snake_case
        // wire keys `01_spec.md` §1.2 documents -- SiteComplianceEndpoints' own remarks explain why the
        // design doc's un-annotated camelCase sample would NOT bind these query keys correctly.
        var response = await _client.GetAsync(
            "/api/v1/execution/sites/SITE-001/compliance-status?study_id=STUDY-001&milestone=SCREENING_VISIT",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.GetComplianceStatusAsyncCalls.Should().Be(1);
        _factory.Service.LastComplianceStatusSiteId.Should().Be("SITE-001");
        _factory.Service.LastComplianceStatusStudyId.Should().Be("STUDY-001");
        _factory.Service.LastComplianceStatusMilestone.Should().Be("SCREENING_VISIT");
    }

    [Fact]
    public async Task GetComplianceStatus_GivenNoMilestoneQueryParam_DefaultsToSiteActivation()
    {
        var response = await _client.GetAsync(
            "/api/v1/execution/sites/SITE-001/compliance-status?study_id=STUDY-001",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.LastComplianceStatusMilestone.Should().Be("SITE_ACTIVATION");
    }

    [Fact]
    public async Task GetComplianceStatus_GivenNoCacheRowResult_StillReturns200NeverAProblemResponse()
    {
        // AC #6: even a "nothing found" outcome from the application service is always Results.Ok(...) --
        // there is no failure branch on this route at all (unlike the other two routes in this file).
        _factory.Service.ComplianceStatusResult = new ComplianceStatusResponse(
            new StudyCode("STUDY-001"), new SiteCode("SITE-001"), "SITE_ACTIVATION", IsComplete: false, MissingDocuments: []);

        var response = await _client.GetAsync(
            "/api/v1/execution/sites/SITE-001/compliance-status?study_id=STUDY-001",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ComplianceStatusResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body!.IsComplete.Should().BeFalse();
        body.MissingDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSiteComplianceStatusAsync_GivenPreCancelledToken_ThrowsOperationCanceledException()
    {
        var service = new FakeSiteComplianceApplicationService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SiteComplianceEndpoints.GetSiteComplianceStatusAsync("SITE-001", "STUDY-001", null, service, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ================================================================================================
    // Test infrastructure
    // ================================================================================================

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        ExecutionJsonSerialization.Configure(options);
        return options;
    }

    private static async Task AssertProblemDetailAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedDetail)
    {
        response.StatusCode.Should().Be(expectedStatus);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);
    }

    private Task<HttpResponseMessage> SendJsonAsync<TBody>(HttpMethod method, string uri, TBody body, HttpClient? client = null)
        where TBody : notnull =>
        (client ?? _client).SendAsync(
            new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body, options: JsonOptions) },
            TestContext.Current.CancellationToken);

    private Task<HttpResponseMessage> SendRawJsonAsync(HttpMethod method, string uri, string rawJson, HttpClient? client = null) =>
        (client ?? _client).SendAsync(
            new HttpRequestMessage(method, uri) { Content = new StringContent(rawJson, Encoding.UTF8, "application/json") },
            TestContext.Current.CancellationToken);

    /// <summary>
    /// A fake <see cref="ISiteComplianceApplicationService"/> recording call counts/arguments and returning
    /// settable results per method, mirroring <c>SubjectEndpointsRoutingTests</c>' own
    /// <c>FakeSubjectApplicationService</c> convention exactly. <c>ct.ThrowIfCancellationRequested()</c> first
    /// in every method is what makes the <see cref="CancellationToken"/> boundary tests above meaningful.
    /// </summary>
    private sealed class FakeSiteComplianceApplicationService : ISiteComplianceApplicationService
    {
        public int ApplyEtmfWebhookAsyncCalls { get; private set; }

        public int ActivateSiteAsyncCalls { get; private set; }

        public int GetComplianceStatusAsyncCalls { get; private set; }

        public string? LastActivateSiteId { get; private set; }

        public string? LastComplianceStatusSiteId { get; private set; }

        public string? LastComplianceStatusStudyId { get; private set; }

        public string? LastComplianceStatusMilestone { get; private set; }

        public Result<bool>? ActivateSiteResult { get; set; }

        public ComplianceStatusResponse? ComplianceStatusResult { get; set; }

        public Task<Result<bool>> ApplyEtmfWebhookAsync(EtmfCompletenessWebhookPayload payload, CancellationToken ct)
        {
            // No settable override property: ISiteComplianceApplicationService.ApplyEtmfWebhookAsync's own
            // doc comment is explicit that it "always upserts" and has no documented failure path, so no
            // test in this file ever needs this fake to return anything other than success.
            ApplyEtmfWebhookAsyncCalls++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<bool>> ActivateSiteAsync(string siteId, SiteActivationRequest request, CancellationToken ct)
        {
            ActivateSiteAsyncCalls++;
            LastActivateSiteId = siteId;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(ActivateSiteResult ?? Result<bool>.Success(true));
        }

        public Task<ComplianceStatusResponse> GetComplianceStatusAsync(string siteId, string studyId, string milestone, CancellationToken ct)
        {
            GetComplianceStatusAsyncCalls++;
            LastComplianceStatusSiteId = siteId;
            LastComplianceStatusStudyId = studyId;
            LastComplianceStatusMilestone = milestone;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(ComplianceStatusResult ?? new ComplianceStatusResponse(
                new StudyCode(studyId), new SiteCode(siteId), milestone, IsComplete: true, MissingDocuments: []));
        }
    }

    /// <summary>
    /// A self-contained <see cref="WebApplicationFactory{TEntryPoint}"/> hosting ONLY
    /// <see cref="SiteComplianceEndpoints.MapSiteComplianceEndpoints"/> against
    /// <see cref="FakeSiteComplianceApplicationService"/>, independent of `Program.cs`'s own composition
    /// root -- matching <c>SubjectEndpointsRoutingTests.SubjectEndpointsTestFactory</c>'s exact approach
    /// (`MIG-SUBJ-023`), so these tests remain true routing/filter-wiring tests of this ticket's own surface.
    /// <see cref="Cadence.Apps.Execution.Program"/> is used purely as <see cref="WebApplicationFactory{TEntryPoint}"/>'s required
    /// marker/content-root-inference type. The REAL composition root (every DI registration this ticket adds
    /// to `Program.cs`) is instead exercised by <see cref="ExecutionProgramStartupTests"/> below, in this
    /// same file, per this ticket's own "dedicated startup smoke test" requirement.
    /// </summary>
    private sealed class SiteComplianceEndpointsTestFactory : WebApplicationFactory<Cadence.Apps.Execution.Program>
    {
        public FakeSiteComplianceApplicationService Service { get; } = new();

        protected override IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureWebHost(webHost => webHost
                    .UseTestServer()
                    .UseTestGatewaySecret()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddOpenApi();
                        services.AddCadenceSecurity();
                        services.ConfigureHttpJsonOptions(ApplyExecutionJsonConventions);
                        services.AddSingleton<ISiteComplianceApplicationService>(Service);
                        services.AddSingleton<IValidator<EtmfCompletenessWebhookPayload>, EtmfCompletenessWebhookPayloadValidator>();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseCadenceSecurity();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapSiteComplianceEndpoints();
                            endpoints.MapOpenApi().AllowAnonymous();
                        });
                    }));

        private static void ApplyExecutionJsonConventions(Microsoft.AspNetCore.Http.Json.JsonOptions options) =>
            ExecutionJsonSerialization.Configure(options.SerializerOptions);
    }
}

/// <summary>
/// The dedicated startup smoke test this ticket's own Verification &amp; Test Requirements calls for: "a
/// dedicated startup smoke test (<c>WebApplicationFactory&lt;Program&gt;().CreateClient()</c> succeeding
/// without throwing) as the primary verification that every DI registration in this ticket is complete and
/// consistent -- this smoke test becomes the foundation <c>ExecutionApiFactory</c> that MIG-SUBJ-025
/// formalizes for golden-master use." Unlike <see cref="SiteComplianceEndpointsRoutingTests"/> above (which
/// deliberately bypasses `Program.cs` with an independent host + fakes), every test in this class boots the
/// REAL, unmodified `Program.cs` composition root end to end -- the only override is
/// <see cref="ExecutionProgramTestFactory"/>'s swap of <see cref="ExecutionDbContext"/>'s underlying
/// provider from Npgsql to <c>Microsoft.EntityFrameworkCore.InMemory</c>, since no live PostgreSQL instance
/// is assumed to be reachable wherever `dotnet test` runs. A same-relational-family swap (Sqlite) was tried
/// first and rejected: EF Core refuses to build a context once two *relational* providers' services are
/// both present in one container (`Program.cs`'s own Npgsql registration is never actually removable via a
/// `DbContextOptions&lt;T&gt;` descriptor swap alone), so the non-relational InMemory provider is the one
/// that actually coexists safely -- see <see cref="ExecutionProgramTestFactory"/>'s own remarks for the full
/// explanation, and <c>ClinicalSubjectConfigurationTests</c>/<c>SiteComplianceCacheConfigurationTests</c> for
/// this same test project's own prior-established precedent that <c>SiteComplianceCache</c>
/// (the only entity this class's end-to-end assertions ever touch) is already proven compatible with it. Both
/// persistence interceptors (<see cref="ConcurrencyTokenIncrementInterceptor"/>/<see cref="GxPAuditInterceptor"/>)
/// are re-registered identically to `Program.cs`'s own registration, so this override changes only the
/// physical database, not the audit/concurrency behavior under test.
/// <para>
/// This override is also why every test here incidentally proves this ticket's AC #8 (error condition: "the
/// MassTransit outbox/consumer registration does not require a live Redis/broker connection to start the app
/// in a test host"): `Program.cs`'s own <c>UsingInMemory</c> bus transport is left completely untouched by
/// this factory, so if a live broker were ever required to start, every test in this class would fail or
/// hang outright -- confirmed empirically while implementing this ticket (`MassTransit.EntityFrameworkCoreIntegration.BusOutboxDeliveryService`
/// logs, but does not throw or block host startup on, its own background outbox-polling query failures
/// against a non-Postgres SQL dialect -- exactly the scenario this override creates). EF Core/MassTransit's
/// own diagnostic-level logging for those expected background polling failures is suppressed below purely to
/// keep test output readable; it has no bearing on any assertion in this class.
/// </para>
/// </summary>
public sealed class ExecutionProgramStartupTests : IDisposable
{
    private readonly ExecutionProgramTestFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void CreateClient_BootingTheRealProgramCompositionRoot_DoesNotThrow()
    {
        // The literal AC wording: "WebApplicationFactory<Program>().CreateClient() succeeding without
        // throwing". xunit fails this test automatically if CreateClient() (or the host startup it triggers)
        // throws, so no further assertion is needed beyond "this call returns".
        using var client = _factory.CreateSignedClient();

        client.Should().NotBeNull();
    }

    [Fact]
    public void AllDIRegistrationsAddedByThisTicket_ResolveSuccessfullyFromARealRequestScope()
    {
        // AC #7's own wording: "app.Services.GetRequiredService<T>() for every interface registered in this
        // ticket". Forces client/host creation first so this exercises the fully-built service provider.
        using var client = _factory.CreateSignedClient();
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IClock>().Should().NotBeNull();
        sp.GetRequiredService<IDemographicsEncryptionService>().Should().NotBeNull();
        sp.GetRequiredService<IDemographicsNormalizationService>().Should().NotBeNull();
        sp.GetRequiredService<IHttpContextAccessor>().Should().NotBeNull();
        sp.GetRequiredService<ExecutionDbContext>().Should().NotBeNull();
        sp.GetRequiredService<ISubjectRepository>().Should().NotBeNull();
        sp.GetRequiredService<IConsentRepository>().Should().NotBeNull();
        sp.GetRequiredService<ISiteComplianceRepository>().Should().NotBeNull();
        sp.GetRequiredService<IAuditLogWriter>().Should().NotBeNull();
        sp.GetRequiredService<ICurrentAuditContextAccessor>().Should().BeSameAs(sp.GetRequiredService<IAuditActorScope>(),
            "a consumer's declared service account must be what the audit interceptor reads in the same scope");
        sp.GetRequiredService<ISubjectApplicationService>().Should().NotBeNull();
        sp.GetRequiredService<ISiteComplianceApplicationService>().Should().NotBeNull();
        sp.GetRequiredService<IValidator<SubjectCreateRequest>>().Should().NotBeNull();
        sp.GetRequiredService<IValidator<SubjectConsentRequest>>().Should().NotBeNull();
        sp.GetRequiredService<IValidator<SubjectStateUpdateRequest>>().Should().NotBeNull();
        sp.GetRequiredService<IValidator<EtmfCompletenessWebhookPayload>>().Should().NotBeNull();
    }

    [Fact]
    public async Task Healthz_GivenTheFullyWiredCompositionRoot_StillReturnsTheUnchangedPreExistingResponse()
    {
        // AC #3: this ticket must not regress the pre-existing placeholder route.
        using var client = _factory.CreateSignedClient();

        var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("status").GetString().Should().Be("healthy");
        document.RootElement.GetProperty("service").GetString().Should().Be("Cadence.Apps.Execution");
        document.RootElement.GetProperty("engine").GetString().Should().Be("EDC");
    }

    [Fact]
    public async Task EtmfWebhook_EndToEnd_NewMilestone_Returns200WithExactSuccessEnvelope()
    {
        // AC #4, exercised through the REAL ISiteComplianceApplicationService/ISiteComplianceRepository/
        // ExecutionDbContext -- not a fake -- proving the eTMF webhook route's full DI graph actually
        // persists a new SiteComplianceCache row end to end. eTMF calls it as its signed service account.
        using var client = _factory.CreateSignedClient(TestSecurityContext.Identity("svc:etmf", PermissionMatrix.EtmfServiceRole) with
        {
            ChangeReason = "Automated eTMF completeness webhook sync",
        });
        var payload = new EtmfCompletenessWebhookPayload("STUDY-E2E", "SITE-E2E", "SITE_ACTIVATION", true, null);

        var response = await client.PostAsJsonAsync("/api/v1/execution/webhooks/etmf", payload,
            ExecutionProgramTestFactory.JsonOptions, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperationEnvelopeResponse>(
            ExecutionProgramTestFactory.JsonOptions, TestContext.Current.CancellationToken);
        body.Should().Be(new OperationEnvelopeResponse("success", "Compliance cache updated successfully."));
    }

    [Fact]
    public async Task ActivateSite_EndToEnd_NonCompliantSite_Returns400WithExactBlockedActivationDetail()
    {
        // AC #5, verbatim: "Given a non-compliant site, when POST .../sites/{siteId}/activate is called,
        // then the response is 400 with detail 'Site activation blocked: Site {site_id} is not compliant in
        // eTMF. Missing expected documents.'" -- exercised end to end: no SiteComplianceCache row exists yet
        // for this (study, site) pair, which is itself "not compliant" per
        // SiteComplianceApplicationService.ActivateSiteAsync's own `gate is not { IsComplete: true }` check.
        using var client = _factory.CreateSignedClient(TestSecurityContext.Identity("sponsor-ops", "Data Manager") with
        {
            ChangeReason = "Attempt activation of non-compliant site",
        });
        var request = new SiteActivationRequest("STUDY-E2E-BLOCKED");

        var response = await client.PostAsJsonAsync("/api/v1/execution/sites/SITE-E2E-BLOCKED/activate", request,
            ExecutionProgramTestFactory.JsonOptions, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("detail").GetString().Should().Be(
            "Site activation blocked: Site SITE-E2E-BLOCKED is not compliant in eTMF. Missing expected documents.");
    }

    [Fact]
    public async Task ComplianceStatus_EndToEnd_NoCacheRowExists_Returns200WithFalseAndEmptyDefaults()
    {
        // AC #6, verbatim: "Given any (study, site, milestone) combination with no cache row, when GET
        // .../compliance-status is called, then the response is 200 (never 404) with is_complete: false,
        // missing_documents: []" -- exercised end to end against a genuinely empty ExecutionDbContext table
        // (no prior webhook/activation call for this study/site pair in this test).
        using var client = _factory.CreateSignedClient(TestSecurityContext.Identity("sponsor-ops", "Data Manager"));

        var response = await client.GetAsync(
            "/api/v1/execution/sites/SITE-NEVER-SEEN/compliance-status?study_id=STUDY-NEVER-SEEN",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("is_complete").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("is_activated").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("missing_documents").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ActivateSite_EndToEnd_CompliantSite_PersistsActivatedStatusAcrossReloadsAndIsIdempotent()
    {
        using var etmf = _factory.CreateSignedClient(TestSecurityContext.Identity("svc:etmf", PermissionMatrix.EtmfServiceRole) with
        {
            ChangeReason = "Automated eTMF completeness webhook sync",
        });
        using var dm = _factory.CreateSignedClient(TestSecurityContext.Identity("sponsor-dm-9", "Data Manager") with
        {
            ChangeReason = "All startup artifacts verified complete"
        });

        var webhook = new EtmfCompletenessWebhookPayload("STUDY-ACT-1", "SITE-ACT-1", "SITE_ACTIVATION", true, Array.Empty<string>());
        (await etmf.PostAsJsonAsync("/api/v1/execution/webhooks/etmf", webhook,
            ExecutionProgramTestFactory.JsonOptions, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var activateReq = new SiteActivationRequest("STUDY-ACT-1");
        var firstAct = await dm.PostAsJsonAsync("/api/v1/execution/sites/SITE-ACT-1/activate", activateReq,
            ExecutionProgramTestFactory.JsonOptions, TestContext.Current.CancellationToken);
        firstAct.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondAct = await dm.PostAsJsonAsync("/api/v1/execution/sites/SITE-ACT-1/activate", activateReq,
            ExecutionProgramTestFactory.JsonOptions, TestContext.Current.CancellationToken);
        secondAct.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusRes = await dm.GetAsync(
            "/api/v1/execution/sites/SITE-ACT-1/compliance-status?study_id=STUDY-ACT-1",
            TestContext.Current.CancellationToken);
        statusRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using var statusDoc = JsonDocument.Parse(await statusRes.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        statusDoc.RootElement.GetProperty("is_complete").GetBoolean().Should().BeTrue();
        statusDoc.RootElement.GetProperty("is_activated").GetBoolean().Should().BeTrue();
        statusDoc.RootElement.GetProperty("activated_by").GetString().Should().Be("sponsor-dm-9");
        statusDoc.RootElement.GetProperty("activated_at").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Wraps the REAL <see cref="Cadence.Apps.Execution.Program"/> composition root, overriding only
    /// <see cref="ExecutionDbContext"/>'s underlying provider (Npgsql -&gt; a uniquely-named
    /// <c>Microsoft.EntityFrameworkCore.InMemory</c> database per factory instance) -- see this class's own
    /// remarks for why a dedicated internal service provider, not just a swapped <c>UseXxx(...)</c> call, is
    /// required to make this override actually work. Every other registration `Program.cs` makes
    /// (MassTransit's `UsingInMemory` transport included) is left completely untouched.
    /// </summary>
    private sealed class ExecutionProgramTestFactory : WebApplicationFactory<Cadence.Apps.Execution.Program>
    {
        public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        // Unique per factory instance (xunit constructs a fresh test-class instance, and therefore a fresh
        // factory, per test method) so no two tests in this class ever see each other's rows.
        private readonly string _databaseName = $"execution-program-startup-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestDemographicsKey().UseTestRandomizationKey();
            builder.UseTestPostgresConnectionString();

            builder.ConfigureServices(services =>
            {
                // `Program.cs`'s own registration configures ExecutionDbContext for Npgsql, which -- via
                // AddDbContext's own provider-registration side effects -- adds Npgsql's own EF provider
                // services directly to this same IServiceCollection, not merely to the removed
                // DbContextOptions<ExecutionDbContext> descriptor below. Verified empirically while
                // implementing this ticket: simply re-registering AddDbContext with a different `UseXxx(...)`
                // call here (Sqlite, then plain UseInMemoryDatabase) still throws "Only a single database
                // provider can be registered in a service provider" the first time a real query runs (not at
                // DI-resolution time -- EF only builds/validates its internal service provider lazily, on
                // first actual use of a DbSet<T>/Database property, which is why a "does this resolve"-only
                // test would not have caught this). EF Core's own escape hatch for exactly this scenario is
                // `UseInternalServiceProvider`: building the InMemory provider's services in a completely
                // separate, isolated ServiceProvider bypasses the ambient (Npgsql-containing) one entirely,
                // so the two providers' services never coexist in the same lookup.
                var inMemoryServices = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.RemoveAll<DbContextOptions<ExecutionDbContext>>();

                services.RemoveAll<IDbContextOptionsConfiguration<ExecutionDbContext>>();
                services.AddDbContext<ExecutionDbContext>((sp, options) =>
                {
                    options.UseInMemoryDatabase(_databaseName);
                    options.UseInternalServiceProvider(inMemoryServices);
                    options.AddInterceptors(
                        new ConcurrencyTokenIncrementInterceptor(),
                        new GxPAuditInterceptor(() => sp.GetRequiredService<ICurrentAuditContextAccessor>().UserId, sp.GetRequiredService<IClock>()));
                });

                // MassTransit's EF outbox lock-statement provider is configured for Postgres syntax
                // (`Program.cs`'s own `o.UsePostgres()` call, unmodified here -- see this class's own
                // remarks) and is therefore expected to fail its background polling query against this
                // InMemory-backed context specifically, since the InMemory provider cannot execute a raw SQL
                // query at all. Suppressing these two diagnostic categories keeps that expected, harmless
                // noise out of test output without touching any application-level log source.
                services.AddLogging(logging =>
                {
                    logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
                    logging.AddFilter("MassTransit", LogLevel.None);
                });
            });

            base.ConfigureWebHost(builder);
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions();
            ExecutionJsonSerialization.Configure(options);
            return options;
        }
    }
}
