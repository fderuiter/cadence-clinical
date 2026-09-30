using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// #4920 through the real composition root: source data verification needs a verification role and a reason for
/// change, and a query's responder and resolver are always the signed caller, whatever the request body names.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionCallerAuthorityTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string Reason = "Caller authority check";
    private const string Study = "STUDY-AUTHORITY";

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Sdv_BySiteCoordinator_Returns403_AndTheObservationStaysUnverified()
    {
        factory.SkipIfUnavailable();
        var coordinator = Caller("sdv-coordinator", "Site Coordinator");
        string observation = await CreateObservationAsync(coordinator);

        using var client = factory.CreateSignedClient(coordinator);
        var verify = await client.PostAsync($"/api/v1/execution/observations/{observation}/sdv", content: null, Ct);
        var flag = await client.PostAsJsonAsync($"/api/v1/execution/observations/{observation}/sdv/flag", new { reason = "Mismatch" }, Ct);

        verify.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        flag.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var read = await ReadAsync(client, $"/api/v1/execution/observations/{observation}");
        read.RootElement.GetProperty("is_sdv_verified").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Sdv_ByMonitor_RecordsTheMonitorAsVerifier()
    {
        factory.SkipIfUnavailable();
        var monitor = Caller("sdv-monitor", "CRA");
        string observation = await CreateObservationAsync(Caller("sdv-crc", "Site Coordinator"));

        using var client = factory.CreateSignedClient(monitor);
        var verify = await client.PostAsync($"/api/v1/execution/observations/{observation}/sdv", content: null, Ct);

        verify.StatusCode.Should().Be(HttpStatusCode.OK, await verify.Content.ReadAsStringAsync(Ct));
        using var body = JsonDocument.Parse(await verify.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("sdv_verified_by").GetString().Should().Be(monitor.UserId);
    }

    [Fact]
    public async Task Sdv_WithoutReasonForChange_Returns400()
    {
        factory.SkipIfUnavailable();
        string observation = await CreateObservationAsync(Caller("sdv-crc2", "Site Coordinator"));
        var monitor = Caller("sdv-monitor2", "CRA") with { ChangeReason = null };

        using var client = factory.CreateSignedClient(monitor);
        var verify = await client.PostAsync($"/api/v1/execution/observations/{observation}/sdv", content: null, Ct);

        verify.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await verify.Content.ReadAsStringAsync(Ct)).Should().Contain(GatewayHeaders.ChangeReason);
    }

    [Fact]
    public async Task QueryResponderAndResolver_AreTheSignedCaller_NotTheBodyFields()
    {
        factory.SkipIfUnavailable();
        var monitor = Caller("query-monitor", "CRA");
        var investigator = Caller("query-investigator", "Site Investigator");
        using var monitorClient = factory.CreateSignedClient(monitor);
        using var investigatorClient = factory.CreateSignedClient(investigator);

        var raised = await monitorClient.PostAsJsonAsync(
            "/api/v1/execution/queries/",
            new { study_id = Study, subject_id = $"SUBJ-{_run}", test_code = "SYSBP", explanation = "Value out of range" },
            Ct);
        raised.EnsureSuccessStatusCode();
        string query = (await ReadIdAsync(raised))!;

        var answered = await investigatorClient.PostAsJsonAsync(
            $"/api/v1/execution/queries/{query}/respond",
            new { response = "Confirmed with source", responder = "someone-else" },
            Ct);
        var closed = await monitorClient.PostAsJsonAsync(
            $"/api/v1/execution/queries/{query}/close",
            new { resolver = "someone-else" },
            Ct);

        answered.StatusCode.Should().Be(HttpStatusCode.OK, await answered.Content.ReadAsStringAsync(Ct));
        closed.StatusCode.Should().Be(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(Ct));
        using var body = JsonDocument.Parse(await closed.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("responder").GetString().Should().Be(investigator.UserId);
        body.RootElement.GetProperty("resolver").GetString().Should().Be(monitor.UserId);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GatewayIdentity Caller(string name, string role) =>
        TestSecurityContext.Identity($"{name}-{_run}", role) with { ChangeReason = Reason };

    private async Task<string> CreateObservationAsync(GatewayIdentity caller)
    {
        using var client = factory.CreateSignedClient(caller);
        var created = await client.PostAsJsonAsync(
            "/api/v1/execution/observations/",
            new { subject_id = $"SUBJ-{_run}", domain = "VS", test_code = "SYSBP", test_name = "Systolic BP", study_id = Study, value = 120, unit = "mmHg", observation_date = "2026-01-01T08:00:00Z" },
            Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return (await ReadIdAsync(created))!;
    }

    private static async Task<JsonDocument> ReadAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<string?> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("id").GetString();
    }
}
