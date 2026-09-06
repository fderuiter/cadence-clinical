using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Serialization;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Core.Domain;
using Cadence.Testing;

using FluentAssertions;

using FluentValidation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// `MIG-SUBJ-023` coverage for <see cref="SubjectEndpoints.MapSubjectEndpoints"/> — one routing-shape test
/// per route (status code, filter short-circuit behavior, OpenAPI exclusion for the alias route), per
/// `03_tickets.md`'s own Verification &amp; Test Requirements. Full behavioral correctness of
/// <see cref="ISubjectApplicationService"/> itself is delegated to <c>MIG-SUBJ-026</c>'s golden-master
/// suite and already covered by <c>SubjectApplicationService*Tests</c> — these tests exercise a
/// <see cref="FakeSubjectApplicationService"/> test double instead, so a failure here always points at the
/// endpoint/filter wiring, never at application-service business logic.
/// <para>
/// Most tests here host <see cref="SubjectEndpoints.MapSubjectEndpoints"/> behind a real, in-memory
/// <see cref="TestServer"/> via <see cref="SubjectEndpointsTestFactory"/> (a <see cref="WebApplicationFactory{TEntryPoint}"/>
/// per the ticket's own instruction) so the actual route templates, endpoint filter pipeline, and JSON wire
/// shape are all genuinely exercised — not merely the handler method bodies in isolation. Two narrow
/// exceptions call <see cref="SubjectEndpoints"/>'s handler methods directly instead (see their own remarks):
/// the <c>CancellationToken</c> boundary check (AC: "cancelling the request mid-flight, simulated... via a
/// pre-cancelled token" — a genuine HTTP-level abort race is exactly what that wording tells us to avoid),
/// and the blinded-role-claim-to-<c>isBlindedCaller</c> wiring check (a caller-principal concern entirely
/// internal to the handler body, independent of route/filter registration).
/// </para>
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectEndpointsRoutingTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly SubjectEndpointsTestFactory _factory = new();
    private readonly HttpClient _client;

    public SubjectEndpointsRoutingTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ================================================================================================
    // POST /api/v1/execution/subjects (CreateSubject)
    // ================================================================================================

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateSubject_GivenValidRequest_Returns201WithLocationHeaderPointingAtCanonicalRoute()
    {
        _factory.Service.CreateResult = Result<SubjectResponse>.Success(
            new SubjectResponse(SubjectId.New(), new SubjectCode("SUBJ-777"), new StudyCode("STUDY-001"),
                null, null, "SCREENING", "1.0.0", null));
        var request = new SubjectCreateRequest("SUBJ-777", "STUDY-001", null, null, null);

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/execution/subjects", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be("/api/v1/execution/subjects/SUBJ-777");
        _factory.Service.CreateAsyncCalls.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateSubject_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService()
    {
        // Empty subject_id fails SubjectCreateRequestValidator's NotEmpty() rule -- ValidationFilter<T> must
        // short-circuit before CreateAsync is ever reached.
        var response = await SendRawJsonAsync(HttpMethod.Post, "/api/v1/execution/subjects",
            """{"subject_id": "", "study_id": "STUDY-001"}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Service.CreateAsyncCalls.Should().Be(0);
    }

    // ================================================================================================
    // POST /{subjectId}/consent (RecordSubjectConsent) + POST /{subjectId}/consents (undocumented alias)
    // ================================================================================================

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task RecordSubjectConsent_GivenValidRequest_Returns200WithConsentResponseBody()
    {
        var request = new SubjectConsentRequest(new ProtocolVersionPayload("STUDY-001", "2.0", 2), IcfSigned: true, IcfSignedDate: null);

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/execution/subjects/SUBJ-001/consent", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.RecordConsentAsyncCalls.Should().Be(1);
        var body = await response.Content.ReadFromJsonAsync<SubjectConsentResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.IcfSigned.Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task RecordSubjectConsent_GivenSubjectNotFound_Returns404WithStandardizedDetail()
    {
        _factory.Service.RecordConsentResult = SubjectErrors.NotFound("SUBJ-404");
        var request = new SubjectConsentRequest(new ProtocolVersionPayload("STUDY-001", "2.0", 2), IcfSigned: true, IcfSignedDate: null);

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/execution/subjects/SUBJ-404/consent", request);

        await AssertProblemDetailAsync(response, HttpStatusCode.NotFound, "Subject not found");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task RecordSubjectConsent_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService()
    {
        // Missing protocol_version entirely fails SubjectConsentRequestValidator's NotNull() rule.
        var response = await SendRawJsonAsync(HttpMethod.Post, "/api/v1/execution/subjects/SUBJ-001/consent",
            """{"icf_signed": true}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Service.RecordConsentAsyncCalls.Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task RecordSubjectConsentAlias_GivenValidRequest_BehavesIdenticallyToTheCanonicalConsentRoute()
    {
        var request = new SubjectConsentRequest(new ProtocolVersionPayload("STUDY-001", "2.0", 2), IcfSigned: true, IcfSignedDate: null);

        var response = await SendJsonAsync(HttpMethod.Post, "/api/v1/execution/subjects/SUBJ-001/consents", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the plural alias must behave identically to /consent");
        _factory.Service.RecordConsentAsyncCalls.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task RecordSubjectConsentAlias_GivenInvalidRequest_AlsoShortCircuitsWith400_MatchingTheCanonicalRoute()
    {
        // The alias applies the same ValidationFilter<SubjectConsentRequest> as /consent (see
        // SubjectEndpoints.MapSubjectEndpoints's own remarks on why this is necessary for true behavioral
        // parity, unlike the design doc's un-filtered illustrative alias sample).
        var response = await SendRawJsonAsync(HttpMethod.Post, "/api/v1/execution/subjects/SUBJ-001/consents",
            """{"icf_signed": true}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Service.RecordConsentAsyncCalls.Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task RecordSubjectConsentAlias_IsExcludedFromGeneratedOpenApiDocument_WhileCanonicalRouteIsIncluded()
    {
        var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/v1/execution/subjects/{subjectId}/consent", out _).Should().BeTrue(
            "the canonical route must still be documented");
        paths.TryGetProperty("/api/v1/execution/subjects/{subjectId}/consents", out _).Should().BeFalse(
            "the plural alias is a legacy compatibility quirk, not a documented contract");
    }

    // ================================================================================================
    // PATCH /{subjectId}/state (AdvanceSubjectState)
    // ================================================================================================

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task UpdateSubjectState_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService()
    {
        var request = new SubjectStateUpdateRequest("ENROLLED", null);

        var response = await SendJsonAsync(HttpMethod.Patch, "/api/v1/execution/subjects/SUBJ-001/state", request, changeReason: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Service.AdvanceStateAsyncCalls.Should().Be(0, "ChangeReasonHeaderFilter must short-circuit before the handler runs");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task UpdateSubjectState_GivenWhitespaceOnlyChangeReasonHeader_Returns400AndNeverInvokesApplicationService()
    {
        var request = new SubjectStateUpdateRequest("ENROLLED", null);

        var response = await SendJsonAsync(HttpMethod.Patch, "/api/v1/execution/subjects/SUBJ-001/state", request, changeReason: "   ");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Service.AdvanceStateAsyncCalls.Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task UpdateSubjectState_GivenValidRequestAndHeader_Returns200AndThreadsTheHeaderValueThrough()
    {
        var request = new SubjectStateUpdateRequest("ENROLLED", null);

        var response = await SendJsonAsync(HttpMethod.Patch, "/api/v1/execution/subjects/SUBJ-001/state", request,
            changeReason: "Protocol Amendment 3 requires re-baseline.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.AdvanceStateAsyncCalls.Should().Be(1);
        _factory.Service.LastChangeReasonSeenByAdvanceState.Should().Be("Protocol Amendment 3 requires re-baseline.");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task UpdateSubjectState_GivenInvalidBody_ShortCircuitsWith400AndNeverInvokesApplicationService()
    {
        // Neither `status` nor `state` supplied -- fails SubjectStateUpdateRequestValidator's Must() rule.
        // The header is present and valid, isolating this as a ValidationFilter<T> short-circuit rather than
        // ChangeReasonHeaderFilter's.
        var response = await SendRawJsonAsync(HttpMethod.Patch, "/api/v1/execution/subjects/SUBJ-001/state", "{}",
            changeReason: "Valid reason.");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Service.AdvanceStateAsyncCalls.Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task UpdateSubjectState_GivenSubjectNotFound_Returns404WithStandardizedDetail()
    {
        _factory.Service.AdvanceStateResult = SubjectErrors.NotFound("SUBJ-404");
        var request = new SubjectStateUpdateRequest("ENROLLED", null);

        var response = await SendJsonAsync(HttpMethod.Patch, "/api/v1/execution/subjects/SUBJ-404/state", request,
            changeReason: "Valid reason.");

        await AssertProblemDetailAsync(response, HttpStatusCode.NotFound, "Subject not found");
    }

    // ================================================================================================
    // GET /{subjectId} (GetSubjectDetail)
    // ================================================================================================

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetSubjectDetail_GivenValidRequest_Returns200WithDetailBody()
    {
        var response = await _client.GetAsync("/api/v1/execution/subjects/SUBJ-001", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.GetDetailAsyncCalls.Should().Be(1);
        _factory.Service.LastIsBlindedCaller.Should().BeFalse("no role claim was presented, so the default caller is unblinded");
        var body = await response.Content.ReadFromJsonAsync<SubjectDetailResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body!.TreatmentGroup.Should().Be("ARM-A");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetSubjectDetail_GivenSubjectNotFound_Returns404WithStandardizedDetail()
    {
        _factory.Service.GetDetailResult = SubjectErrors.NotFound("SUBJ-404");

        var response = await _client.GetAsync("/api/v1/execution/subjects/SUBJ-404", TestContext.Current.CancellationToken);

        await AssertProblemDetailAsync(response, HttpStatusCode.NotFound, "Subject not found");
    }

    /// <summary>
    /// AC: "Given `GET /{subjectId}` for a blinded-role caller... the response body has
    /// `treatment_group: null`... this ticket only verifies the wiring, not the redaction logic itself."
    /// Calls <see cref="SubjectEndpoints.GetSubjectDetailAsync"/> directly with a constructed
    /// <see cref="ClaimsPrincipal"/> carrying the (temporary, cross-cutting-gap-pending)
    /// <c>"Blinded"</c> role claim <see cref="SubjectEndpoints"/> looks for, then genuinely executes the
    /// returned <see cref="IResult"/> and inspects the real serialized JSON body — proving the actual wire
    /// shape, not just the constructed C# response object — without needing a full authentication-handler
    /// registration in <see cref="SubjectEndpointsTestFactory"/> purely to inject one test principal.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetSubjectDetailAsync_GivenCallerWithBlindedRoleClaim_ResolvesIsBlindedCallerTrue_AndSerializesTreatmentGroupAsNull()
    {
        var service = new FakeSubjectApplicationService();
        using var responseBody = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Blinded") }, "TestAuth")),
            RequestServices = BuildJsonOptionsServiceProvider(),
        };
        httpContext.Response.Body = responseBody;

        var result = await SubjectEndpoints.GetSubjectDetailAsync("SUBJ-001", httpContext, service, TestContext.Current.CancellationToken);
        await result.ExecuteAsync(httpContext);

        service.LastIsBlindedCaller.Should().BeTrue();
        responseBody.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(responseBody, cancellationToken: TestContext.Current.CancellationToken);
        document.RootElement.GetProperty("treatment_group").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetSubjectDetailAsync_GivenCallerWithoutBlindedRoleClaim_ResolvesIsBlindedCallerFalse_AndDoesNotRedact()
    {
        // The meaningful half of the matrix above -- proves the wiring actually branches on the claim,
        // rather than isBlindedCaller trivially always being true or false regardless of the caller.
        var service = new FakeSubjectApplicationService();
        using var responseBody = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Investigator") }, "TestAuth")),
            RequestServices = BuildJsonOptionsServiceProvider(),
        };
        httpContext.Response.Body = responseBody;

        var result = await SubjectEndpoints.GetSubjectDetailAsync("SUBJ-001", httpContext, service, TestContext.Current.CancellationToken);
        await result.ExecuteAsync(httpContext);

        service.LastIsBlindedCaller.Should().BeFalse();
        responseBody.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(responseBody, cancellationToken: TestContext.Current.CancellationToken);
        document.RootElement.GetProperty("treatment_group").GetString().Should().Be("ARM-A");
    }

    // ================================================================================================
    // PUT /{subjectId}/demographics (UpdateSubjectDemographics)
    // ================================================================================================

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task UpdateSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService()
    {
        var request = new SubjectDemographicsUpdateRequest(null, null);

        var response = await SendJsonAsync(HttpMethod.Put, "/api/v1/execution/subjects/SUBJ-001/demographics", request, changeReason: null); // pragma: allowlist secret

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Service.UpdateDemographicsAsyncCalls.Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task UpdateSubjectDemographics_GivenValidRequestAndHeader_Returns200()
    {
        var request = new SubjectDemographicsUpdateRequest(new DemographicsPayload("Jane Doe", "1990-01-01", "F", null), null);

        var response = await SendJsonAsync(HttpMethod.Put, "/api/v1/execution/subjects/SUBJ-001/demographics", request, // pragma: allowlist secret
            changeReason: "Correcting transcription error.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.UpdateDemographicsAsyncCalls.Should().Be(1);
        _factory.Service.LastChangeReasonSeenByUpdateDemographics.Should().Be("Correcting transcription error.");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task UpdateSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail()
    {
        _factory.Service.UpdateDemographicsResult = SubjectErrors.NotFound("SUBJ-404");
        var request = new SubjectDemographicsUpdateRequest(null, null);

        var response = await SendJsonAsync(HttpMethod.Put, "/api/v1/execution/subjects/SUBJ-404/demographics", request, // pragma: allowlist secret
            changeReason: "Valid reason.");

        await AssertProblemDetailAsync(response, HttpStatusCode.NotFound, "Subject not found");
    }

    // ================================================================================================
    // DELETE /{subjectId}/demographics (DeleteSubjectDemographics)
    // ================================================================================================

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task DeleteSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService()
    {
        // Closes MIG-SUBJ-022's noted "!"-safety gap: this route did not exist before this ticket, so this
        // is the first time ChangeReasonHeaderFilter is actually applied to it.
        var response = await SendAsync(HttpMethod.Delete, "/api/v1/execution/subjects/SUBJ-001/demographics", changeReason: null); // pragma: allowlist secret

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Service.DeleteDemographicsAsyncCalls.Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task DeleteSubjectDemographics_GivenValidRequestAndHeader_Returns200()
    {
        var response = await SendAsync(HttpMethod.Delete, "/api/v1/execution/subjects/SUBJ-001/demographics", // pragma: allowlist secret
            changeReason: "Subject withdrew consent.");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Service.DeleteDemographicsAsyncCalls.Should().Be(1);
        _factory.Service.LastChangeReasonSeenByDeleteDemographics.Should().Be("Subject withdrew consent.");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task DeleteSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail()
    {
        _factory.Service.DeleteDemographicsResult = SubjectErrors.NotFound("SUBJ-404");

        var response = await SendAsync(HttpMethod.Delete, "/api/v1/execution/subjects/SUBJ-404/demographics", // pragma: allowlist secret
            changeReason: "Valid reason.");

        await AssertProblemDetailAsync(response, HttpStatusCode.NotFound, "Subject not found");
    }

    // ================================================================================================
    // Boundary check: CancellationToken is honored end-to-end (AC: "cancelling the request mid-flight
    // (simulated in a routing test via a pre-cancelled token)"). Calling each handler method directly with an
    // already-cancelled token and a FakeSubjectApplicationService that itself honors it (ct.ThrowIfCancellationRequested())
    // is the deterministic simulation the AC's own wording calls for -- a genuine HTTP-level abort race would
    // be nondeterministic and is exactly what "simulated" tells us to avoid.
    // ================================================================================================

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateSubjectAsync_GivenPreCancelledToken_ThrowsOperationCanceledExceptionRatherThanCompleting()
    {
        var service = new FakeSubjectApplicationService();
        var request = new SubjectCreateRequest("SUBJ-001", "STUDY-001", null, null, null);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SubjectEndpoints.CreateSubjectAsync(request, service, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task RecordSubjectConsentAsync_GivenPreCancelledToken_ThrowsOperationCanceledException()
    {
        var service = new FakeSubjectApplicationService();
        var request = new SubjectConsentRequest(new ProtocolVersionPayload("STUDY-001", "2.0", 2), IcfSigned: true, IcfSignedDate: null);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SubjectEndpoints.RecordSubjectConsentAsync("SUBJ-001", request, service, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AdvanceSubjectStateAsync_GivenPreCancelledToken_ThrowsOperationCanceledException()
    {
        var service = new FakeSubjectApplicationService();
        var request = new SubjectStateUpdateRequest("ENROLLED", null);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Change-Reason"] = "Valid reason.";
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SubjectEndpoints.AdvanceSubjectStateAsync("SUBJ-001", request, httpContext, service, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetSubjectDetailAsync_GivenPreCancelledToken_ThrowsOperationCanceledException()
    {
        var service = new FakeSubjectApplicationService();
        var httpContext = new DefaultHttpContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SubjectEndpoints.GetSubjectDetailAsync("SUBJ-001", httpContext, service, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task UpdateSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException()
    {
        var service = new FakeSubjectApplicationService();
        var request = new SubjectDemographicsUpdateRequest(null, null);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Change-Reason"] = "Valid reason.";
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SubjectEndpoints.UpdateSubjectDemographicsAsync("SUBJ-001", request, httpContext, service, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task DeleteSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException()
    {
        var service = new FakeSubjectApplicationService();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Change-Reason"] = "Valid reason.";
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => SubjectEndpoints.DeleteSubjectDemographicsAsync("SUBJ-001", httpContext, service, cts.Token);

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

    private static IServiceProvider BuildJsonOptionsServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpJsonOptions(ApplyExecutionJsonConventions);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// This ticket's own tests were the first in this repo to exercise a real end-to-end HTTP
    /// request/response cycle against this service's JSON options, which surfaced a bug in already-merged
    /// `MIG-SUBJ-008` code: <see cref="ExecutionJsonSerialization.Configure"/> used to set
    /// <c>PropertyNamingPolicy</c> via <c>??=</c>, a no-op against
    /// <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions.SerializerOptions"/> specifically (constructed
    /// from <see cref="JsonSerializerDefaults.Web"/>, whose <c>PropertyNamingPolicy</c> is already
    /// <see cref="JsonNamingPolicy.CamelCase"/>, non-null, by the time a <c>ConfigureHttpJsonOptions</c>
    /// callback runs) — flagged in this ticket's own final report and fixed at its source immediately after,
    /// so this helper now just calls <see cref="ExecutionJsonSerialization.Configure"/> directly like every
    /// other caller.
    /// </summary>
    private static void ApplyExecutionJsonConventions(Microsoft.AspNetCore.Http.Json.JsonOptions options) =>
        ExecutionJsonSerialization.Configure(options.SerializerOptions);

    private static async Task AssertProblemDetailAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedDetail)
    {
        response.StatusCode.Should().Be(expectedStatus);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string? changeReason) =>
        SendCoreAsync(new HttpRequestMessage(method, uri), changeReason);

    private Task<HttpResponseMessage> SendJsonAsync<TBody>(HttpMethod method, string uri, TBody body, string? changeReason = null)
        where TBody : notnull
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body, options: JsonOptions) };
        return SendCoreAsync(request, changeReason);
    }

    private Task<HttpResponseMessage> SendRawJsonAsync(HttpMethod method, string uri, string rawJson, string? changeReason = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = new StringContent(rawJson, Encoding.UTF8, "application/json") };
        return SendCoreAsync(request, changeReason);
    }

    private Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, string? changeReason)
    {
        if (changeReason is not null)
        {
            request.Headers.Add("X-Change-Reason", changeReason);
        }

        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A fake <see cref="ISubjectApplicationService"/> recording call counts/arguments and returning a
    /// settable <see cref="Result{TValue}"/> per method (defaulting to a canned success value), so each test
    /// can drive exactly the outcome (success/<see cref="SubjectErrors.NotFound"/>) it needs without pulling
    /// in real repositories/EF Core. Calling <c>ct.ThrowIfCancellationRequested()</c> first in every method is
    /// what makes the <c>CancellationToken</c> boundary tests above meaningful: if
    /// <see cref="SubjectEndpoints"/> ever stopped threading <c>ct</c> through to the application-service
    /// call, those tests would start failing (no exception thrown) rather than silently passing.
    /// </summary>
    private sealed class FakeSubjectApplicationService : ISubjectApplicationService
    {
        public int CreateAsyncCalls { get; private set; }

        public int RecordConsentAsyncCalls { get; private set; }

        public int AdvanceStateAsyncCalls { get; private set; }

        public int UpdateDemographicsAsyncCalls { get; private set; }

        public int DeleteDemographicsAsyncCalls { get; private set; }

        public int GetDetailAsyncCalls { get; private set; }

        public bool? LastIsBlindedCaller { get; private set; }

        public string? LastChangeReasonSeenByAdvanceState { get; private set; }

        public string? LastChangeReasonSeenByUpdateDemographics { get; private set; }

        public string? LastChangeReasonSeenByDeleteDemographics { get; private set; }

        public Result<SubjectResponse>? CreateResult { get; set; }

        public Result<SubjectConsentResponse>? RecordConsentResult { get; set; }

        public Result<SubjectResponse>? AdvanceStateResult { get; set; }

        public Result<SubjectResponse>? UpdateDemographicsResult { get; set; }

        public Result<SubjectResponse>? DeleteDemographicsResult { get; set; }

        public Result<SubjectDetailResponse>? GetDetailResult { get; set; }

        public Task<Result<SubjectResponse>> CreateAsync(SubjectCreateRequest request, CancellationToken ct)
        {
            CreateAsyncCalls++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CreateResult ?? Result<SubjectResponse>.Success(DefaultSubjectResponse()));
        }

        public Task<Result<SubjectConsentResponse>> RecordConsentAsync(string subjectIdOrCode, SubjectConsentRequest request, CancellationToken ct)
        {
            RecordConsentAsyncCalls++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(RecordConsentResult ?? Result<SubjectConsentResponse>.Success(DefaultConsentResponse()));
        }

        public Task<Result<SubjectResponse>> AdvanceStateAsync(
            string subjectIdOrCode, SubjectStateUpdateRequest request, string changeReason, CancellationToken ct)
        {
            AdvanceStateAsyncCalls++;
            LastChangeReasonSeenByAdvanceState = changeReason;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(AdvanceStateResult ?? Result<SubjectResponse>.Success(DefaultSubjectResponse()));
        }

        public Task<Result<SubjectResponse>> UpdateDemographicsAsync(
            string subjectIdOrCode, SubjectDemographicsUpdateRequest request, string changeReason, CancellationToken ct)
        {
            UpdateDemographicsAsyncCalls++;
            LastChangeReasonSeenByUpdateDemographics = changeReason;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(UpdateDemographicsResult ?? Result<SubjectResponse>.Success(DefaultSubjectResponse()));
        }

        public Task<Result<SubjectResponse>> DeleteDemographicsAsync(string subjectIdOrCode, string changeReason, CancellationToken ct)
        {
            DeleteDemographicsAsyncCalls++;
            LastChangeReasonSeenByDeleteDemographics = changeReason;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(DeleteDemographicsResult ?? Result<SubjectResponse>.Success(DefaultSubjectResponse()));
        }

        public Task<Result<SubjectDetailResponse>> GetDetailAsync(string subjectId, bool isBlindedCaller, CancellationToken ct)
        {
            GetDetailAsyncCalls++;
            LastIsBlindedCaller = isBlindedCaller;
            ct.ThrowIfCancellationRequested();
            if (GetDetailResult is { } overridden)
            {
                return Task.FromResult(overridden);
            }

            // Mirrors (in miniature, for this fake only) SubjectApplicationService.GetDetailAsync's own RTSM
            // redaction rule -- just enough to prove the endpoint threads isBlindedCaller correctly end to
            // end (AC's own wiring-only concern), not a re-implementation of that method's real logic.
            var body = new SubjectDetailResponse(new SubjectCode(subjectId), new StudyCode("STUDY-001"), "ENROLLED",
                new SiteCode("SITE-001"), isBlindedCaller ? null : "ARM-A", RandomizationSeed: null, InvestigationalProductId: null);
            return Task.FromResult(Result<SubjectDetailResponse>.Success(body));
        }

        public Task<Result<SubjectResponse>> EvaluateAndTransitionScreeningAsync(string subjectId, CancellationToken ct) =>
            throw new NotSupportedException(
                "Not exercised by SubjectEndpoints -- no route registered by this ticket maps to this method.");

        private static SubjectResponse DefaultSubjectResponse() =>
            new(SubjectId.New(), new SubjectCode("SUBJ-001"), new StudyCode("STUDY-001"), null, null, "SCREENING", "1.0.0", null);

        private static SubjectConsentResponse DefaultConsentResponse() =>
            new(SubjectConsentId.New(), new SubjectCode("SUBJ-001"), new StudyCode("STUDY-001"), "2.0", 2, true, null, false, false, 1);
    }

    /// <summary>
    /// A self-contained <see cref="WebApplicationFactory{TEntryPoint}"/> hosting ONLY
    /// <see cref="SubjectEndpoints.MapSubjectEndpoints"/> against <see cref="FakeSubjectApplicationService"/>,
    /// independent of `apps/execution/src/Program.cs`'s own composition root.
    /// <para>
    /// `Program.cs` does not call <c>MapSubjectEndpoints()</c> or register <see cref="ISubjectApplicationService"/>/
    /// its validators yet: per `03_tickets.md`, that "final wiring" is explicitly `MIG-SUBJ-024`'s Target
    /// Files entry, not this ticket's. `Program.cs`'s own remarks additionally note that
    /// <c>AddEntityFrameworkOutbox&lt;ExecutionDbContext&gt;()</c> cannot start cleanly today without
    /// <c>ExecutionDbContext</c> registered (also `MIG-SUBJ-024`'s job) -- so booting the real, current
    /// <see cref="Program"/> unmodified is neither possible nor within this ticket's scope.
    /// </para>
    /// <para>
    /// <see cref="Program"/> is used purely as <see cref="WebApplicationFactory{TEntryPoint}"/>'s required
    /// marker/content-root-inference type, per the ticket's own instruction to test via
    /// <c>WebApplicationFactory&lt;Program&gt;</c>. <see cref="CreateHostBuilder"/> is overridden to build an
    /// entirely independent minimal host that maps exactly (and only) <c>MapSubjectEndpoints()</c> -- the one
    /// extension method this ticket actually owns -- so these tests remain true routing/filter-wiring tests
    /// of `MIG-SUBJ-023`'s own surface, not an accidental end-to-end test of `MIG-SUBJ-024`'s not-yet-written
    /// registrations.
    /// </para>
    /// </summary>
    private sealed class SubjectEndpointsTestFactory : WebApplicationFactory<Program>
    {
        public FakeSubjectApplicationService Service { get; } = new();

        protected override IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureWebHost(webHost => webHost
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddOpenApi();
                        services.ConfigureHttpJsonOptions(ApplyExecutionJsonConventions);
                        services.AddSingleton<ISubjectApplicationService>(Service);
                        services.AddSingleton<IValidator<SubjectCreateRequest>, SubjectCreateRequestValidator>();
                        services.AddSingleton<IValidator<SubjectConsentRequest>, SubjectConsentRequestValidator>();
                        services.AddSingleton<IValidator<SubjectStateUpdateRequest>, SubjectStateUpdateRequestValidator>();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapSubjectEndpoints();
                            endpoints.MapOpenApi();
                        });
                    }));
    }
}
