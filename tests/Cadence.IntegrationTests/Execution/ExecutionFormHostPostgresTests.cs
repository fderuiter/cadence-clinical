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
using Cadence.Kernel.Evidence;
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
/// The eCRF form endpoints through the real kernel host on PostgreSQL (ADR-2601, ADR-2622): a gateway-signed coordinator
/// opens a form, saves pages of values and reads the form back in layout order with its completeness; a stale page is
/// refused whole, and a caller without a grant can neither read nor save. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormHostPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@execution-forms.test";
    private const string Stranger = "stranger@execution-forms.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, AllowOutOfOrderMetadataProperties = true };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// The coordinator opens the week 12 vital signs form, which reads back empty and incomplete with its items in the
    /// specification's order; a page with the systolic value saves at revision 1 and leaves the diastolic value missing;
    /// a page edited from a stale revision is refused whole with its stable code; the next page completes the form.
    /// </summary>
    [Fact(DisplayName = "HostForm_OpensSavesPagesAndDerivesCompleteness_AndRefusesAStalePage")]
    public async Task HostFormOpensSavesPagesAndDerivesCompletenessAndRefusesAStalePage()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, enrollment, collectedAt) = await SeedAsync(ct);
        using var crc = Client(code, Crc);

        var opened = await SendAsync<FormStateResponse>(crc, HttpMethod.Post, string.Empty, "Week 12 visit performed", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = collectedAt,
        }, HttpStatusCode.Created, ct);
        opened.Version.Should().Be(1);

        var empty = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"/{opened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        empty.Items.Select(i => (i.ItemId, i.Required, i.Revision)).Should().Equal((Systolic, true, 0), (Diastolic, true, 0), (Comment, false, 0));
        empty.Complete.Should().BeFalse();
        empty.MissingRequired.Should().Equal(Systolic, Diastolic);

        var saved = await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{opened.FormId:D}/values", SaveReason,
            Page((Systolic, Quantity(128), 0)), HttpStatusCode.OK, ct);
        saved.Values.Should().ContainSingle().Which.Should().Match<FormSavedValueResponse>(v => v.ItemId == Systolic && v.Revision == 1 && v.Changed);

        var partial = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"/{opened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        partial.Items[0].Value.Should().Be(new QuantityValue(128m, "mm[Hg]"));
        partial.Items[0].RecordedBy.Should().Be(Crc);
        partial.MissingRequired.Should().Equal(Diastolic);

        using (var stale = await crc.SendAsync(Request(HttpMethod.Put, $"/{opened.FormId:D}/values", SaveReason, Page((Systolic, Quantity(130), 0), (Diastolic, Quantity(80), 0))), ct))
        {
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(stale, ct)).Should().Be(CollectionErrors.RevisionStale.Code);
        }

        await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{opened.FormId:D}/values", SaveReason,
            Page((Diastolic, Quantity(82), 0)), HttpStatusCode.OK, ct);
        var complete = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"/{opened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        complete.Complete.Should().BeTrue();
        complete.MissingRequired.Should().BeEmpty();
        complete.Items.Select(i => (i.ItemId, i.Revision)).Should().Equal((Systolic, 1), (Diastolic, 1), (Comment, 0));
        complete.Version.Should().Be(3);
    }

    /// <summary>A caller with no grant at the form's study site can neither read it (403) nor save to it (403, audited as denied).</summary>
    [Fact(DisplayName = "HostForm_WithoutAGrant_IsNotVisibleAndNotSaved")]
    public async Task HostFormWithoutAGrantIsNotVisibleAndNotSaved()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, enrollment, collectedAt) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var stranger = Client(code, Stranger);
        var opened = await SendAsync<FormStateResponse>(crc, HttpMethod.Post, string.Empty, "Week 12 visit performed", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = collectedAt,
        }, HttpStatusCode.Created, ct);

        using (var hidden = await stranger.SendAsync(Request(HttpMethod.Get, $"/{opened.FormId:D}", null, null), ct))
        {
            hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var denied = await stranger.SendAsync(Request(HttpMethod.Put, $"/{opened.FormId:D}/values", SaveReason, Page((Systolic, Quantity(128), 0))), ct))
        {
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ProblemCodeAsync(denied, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        var read = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"/{opened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        read.Items.Should().OnlyContain(i => i.Value == null);
    }

    private static object Page(params (string Item, object Value, int ExpectedRevision)[] values) =>
        new { values = values.Select(v => new { item_id = v.Item, value = v.Value, expected_revision = v.ExpectedRevision }).ToArray() };

    // A typed value as a client sends it (ADR-2623).
    private static object Quantity(decimal value) => new { type = "quantity", value, unit = "mm[Hg]" };

    /// <summary>
    /// A tenant whose study has the vital signs form in force, one screened participant, and the coordinator granted to
    /// open and save forms at the participant's study site.
    /// </summary>
    private async Task<(string Code, EnrollmentId Enrollment, DateTimeOffset CollectedAt)> SeedAsync(CancellationToken ct)
    {
        var code = $"T-FORM-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Execution form host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        await _scenario.EffectiveVersionAsync(tenant, study, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);
        foreach (var capability in new[] { "collection.open", "collection.record" })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), capability, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Appointed"),
                ct);
        }

        return (code, enrollment, _scenario.Tick());
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)

            // Every error a form endpoint returns must be one its command declares (ADR-2600 §4.5).
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
