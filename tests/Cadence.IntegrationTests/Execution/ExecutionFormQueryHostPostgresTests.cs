using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Execution's query endpoints through the real kernel host on PostgreSQL (#5461): a gateway-signed monitor raises a query
/// on the value a coordinator entered, the coordinator answers and corrects it, and the monitor sees that the value changed
/// and closes the query. Each role is held to its grants, so a coordinator cannot close a query and a stranger sees none.
/// Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormQueryHostPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@execution-queries.test";
    private const string Monitor = "cra@execution-queries.test";
    private const string Stranger = "stranger@execution-queries.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, AllowOutOfOrderMetadataProperties = true };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// The monitor raises a query on revision 1 of the systolic value; the coordinator answers and then corrects the value
    /// to revision 2, so the query shows the value changed; the monitor closes it. The enrollment's list shows the same
    /// query, and a query on the stale revision is refused with its stable code.
    /// </summary>
    [Fact(DisplayName = "HostQuery_RaiseAnswerCorrectClose_ShowsTheValueChanged")]
    public async Task HostQueryRaiseAnswerCorrectCloseShowsTheValueChanged()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, enrollment, form, observation) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);

        var raised = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Source data verification", new
        {
            observation_id = observation,
            revision = 1,
            text = "Please confirm against the source",
        }, HttpStatusCode.Created, ct);
        raised.Should().Match<FormQueryResponse>(q =>
            q.Status == "open" && q.ItemId == Systolic && q.Revision == 1 && q.CurrentRevision == 1 && !q.ValueChanged && q.RaisedBy == Monitor);

        await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/answer", "Answering the query", new
        {
            text = "Transcription error; corrected to 128",
        }, HttpStatusCode.OK, ct);
        await SendAsync<JsonElement>(crc, HttpMethod.Put, $"/{form:D}/values", SaveReason, new
        {
            values = new[] { new { item_id = Systolic, value = new { type = "quantity", value = 128m, unit = "mm[Hg]" }, expected_revision = 1 } },
        }, HttpStatusCode.OK, ct);

        var listed = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        listed.Should().ContainSingle().Which.Should().Match<FormQueryResponse>(q =>
            q.Status == "answered" && q.Revision == 1 && q.CurrentRevision == 2 && q.ValueChanged);

        using (var stale = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries", "Source data verification", new
        {
            observation_id = observation,
            revision = 1,
            text = "Please confirm again",
        }), ct))
        {
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(stale, ct)).Should().Be("M.Execution.Query.RevisionStale");
        }

        var closed = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/close", "The correction settles it", null, HttpStatusCode.OK, ct);
        closed.Status.Should().Be("closed");
        closed.Messages.Select(m => m.Kind).Should().Equal("raised", "answered", "closed");
        closed.Messages[^1].Text.Should().Be("The correction settles it");

        var forEnrollment = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        forEnrollment.Should().ContainSingle().Which.Should().Match<FormQueryResponse>(q => q.QueryId == raised.QueryId && q.Status == "closed" && q.ValueChanged);
    }

    /// <summary>
    /// Each role keeps to its grants: the coordinator may answer but neither raise nor close (403), and a caller with no
    /// grant at the study site can neither list the form's queries nor the enrollment's (403) nor answer one (403).
    /// </summary>
    [Fact(DisplayName = "HostQuery_RolesFailClosed")]
    public async Task HostQueryRolesFailClosed()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, enrollment, form, observation) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);
        using var stranger = Client(code, Stranger);
        var raised = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Source data verification", new
        {
            observation_id = observation,
            revision = 1,
            text = "Please confirm against the source",
        }, HttpStatusCode.Created, ct);

        await ExpectDeniedAsync(crc, HttpMethod.Post, $"/{form:D}/queries", new { observation_id = observation, revision = 1, text = "Raised by the site" }, ct);
        await ExpectDeniedAsync(crc, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/close", null, ct);
        await ExpectDeniedAsync(stranger, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/answer", new { text = "Not mine to answer" }, ct);
        foreach (var path in new[] { $"/{form:D}/queries", $"/queries?enrollment_id={enrollment.Value:D}" })
        {
            using var hidden = await stranger.SendAsync(Request(HttpMethod.Get, path, null, null), ct);
            hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }

        var unchanged = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        unchanged.Should().ContainSingle().Which.Status.Should().Be("open");
    }

    private static async Task ExpectDeniedAsync(HttpClient client, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var denied = await client.SendAsync(Request(method, path, "Attempted step", body), ct);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        (await ProblemCodeAsync(denied, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code, path);
    }

    /// <summary>
    /// A tenant whose study has the vital signs form in force, one screened participant with a form whose systolic value is
    /// entered at revision 1, the coordinator granted to enter values and answer queries at the participant's study site,
    /// and the monitor granted to raise and manage them there.
    /// </summary>
    private async Task<(string Code, EnrollmentId Enrollment, Guid Form, Guid Observation)> SeedAsync(CancellationToken ct)
    {
        var code = $"T-QRY-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Execution query host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var version = await _scenario.EffectiveVersionAsync(tenant, study, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);
        foreach (var (person, capabilities) in new[]
        {
            (Crc, new[] { "collection.record", "execution.query.answer" }),
            (Monitor, new[] { "execution.query.raise", "execution.query.manage" }),
        })
        {
            foreach (var capability in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), Actor.Person(person), capability, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                        KernelProtocolScenario.Key(), "Appointed"),
                    ct);
            }
        }

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        var value = (await _scenario.Protocol.OkAsync(Save(new Form(subject, version, opened.CollectionInstanceId), Value(Systolic, Mmhg(120))), ct)).Values.Single();
        return (code, enrollment, opened.CollectionInstanceId.Value, value.ObservationId.Value);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)

            // Every error a query endpoint returns must be one its command declares (ADR-2600 §4.5).
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri($"{FormEndpoints.RoutePrefix}{path}", UriKind.Relative));
        request.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
