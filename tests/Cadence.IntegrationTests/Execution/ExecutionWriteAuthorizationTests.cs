using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Signed callers acting on stored queries through the real composition root on PostgreSQL: callers without the
/// needed role get 403 (#4990 moved these from the unit tests, whose host ran on the EF Core in-memory provider). The pre-persistence checks (anonymous and
/// forged callers get 401) stay in <c>Cadence.UnitTests.Execution.Endpoints.ExecutionAuthenticationTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionWriteAuthorizationTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string QueriesPath = "/api/v1/execution/queries/";

    private static readonly GatewayIdentity Cra = TestSecurityContext.Identity("cra-01", "CRA") with { ChangeReason = "Authorization check" };
    private static readonly GatewayIdentity Investigator = TestSecurityContext.Identity("pi-01", "Site Investigator") with { ChangeReason = "Authorization check" };

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task CloseQuery_AsSignedCallerWithoutCraOrDmRole_Returns403()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string queryId = await CreateQueryAsync();
        using var investigator = factory.CreateSignedClient(Investigator);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{QueriesPath}{queryId}/close")
        {
            Content = JsonContent.Create(new { resolver = "pi-01" }),
        };
        request.Headers.Add("X-User-Role", "CRA");
        var response = await investigator.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task CloseQuery_AsSignedCra_Succeeds()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string queryId = await CreateQueryAsync();
        using var investigator = factory.CreateSignedClient(Investigator);
        var answered = await investigator.PostAsJsonAsync($"{QueriesPath}{queryId}/respond", new { response = "Confirmed with source" }, ct);
        answered.StatusCode.Should().Be(HttpStatusCode.OK);
        using var cra = factory.CreateSignedClient(Cra);

        var response = await cra.PostAsJsonAsync($"{QueriesPath}{queryId}/close", new { resolver = "cra-01" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    private async Task<string> CreateQueryAsync()
    {
        using var cra = factory.CreateSignedClient(Cra);
        var response = await cra.PostAsJsonAsync(
            QueriesPath,
            new { study_id = "STUDY-AUTH", subject_id = $"SUBJ-{Guid.NewGuid():N}", test_code = "SYSBP", explanation = "Value out of range" },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadIdAsync(response);
    }

    private static async Task<string> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("id").GetString()!;
    }
}
