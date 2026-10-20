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
/// #4920 through the real composition root: a query's responder and resolver are always the signed caller, whatever
/// the request body names.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionCallerAuthorityTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string Reason = "Caller authority check";
    private const string Study = "STUDY-AUTHORITY";

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

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

    private static async Task<string?> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("id").GetString();
    }
}
