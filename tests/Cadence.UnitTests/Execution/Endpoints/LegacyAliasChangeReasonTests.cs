using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// The gateway proxies Execution's undocumented legacy aliases (<c>/api/v1/queries</c>), so each
/// alias that changes data refuses a request without <c>X-Change-Reason</c>, as its documented route does. Without the
/// header the audit trail would record the fallback "system_operation" as the reason for the change (#5060).
/// </summary>
public sealed class LegacyAliasChangeReasonTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private const string Id = "00000000-0000-0000-0000-000000000001";

    private const string Investigator = "Site Investigator";
    private const string Cra = "CRA";

    /// <summary>Each changing route, with the role of a caller the route admits (ADR-2516), so the request reaches the reason check.</summary>
    public static TheoryData<string, string, string, string> ChangingRoutes => new()
    {
        { "POST", $"/api/v1/queries/{Id}/reopen", """{"reason":"Needs re-check"}""", Cra },
        { "POST", $"/api/v1/execution/queries/{Id}/reopen", """{"reason":"Needs re-check"}""", Cra },
        { "POST", $"/api/v1/queries/{Id}/cancel", """{"reason":"Raised in error"}""", Cra },
        { "POST", $"/api/v1/execution/queries/{Id}/cancel", """{"reason":"Raised in error"}""", Cra },
        { "POST", $"/api/v1/queries/{Id}/respond", """{"response":"Confirmed with source"}""", Investigator },
        { "POST", $"/api/v1/execution/queries/{Id}/respond", """{"response":"Confirmed with source"}""", Investigator },
        { "POST", $"/api/v1/execution/queries/{Id}/answer", """{"response":"Confirmed with source"}""", Investigator },
        { "POST", $"/api/v1/queries/{Id}/close", """{"resolver":"cra-01"}""", Cra },
        { "POST", $"/api/v1/execution/queries/{Id}/close", """{"resolver":"cra-01"}""", Cra },
    };

    /// <summary>A change through an alias or its documented route without a reason is refused before anything is read or written.</summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    [MemberData(nameof(ChangingRoutes))]
    public async Task ChangingRoute_WithoutAChangeReason_Returns400(string method, string path, string body, string role)
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("caller-01", role));
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("detail").GetString().Should().Be("X-Change-Reason header is required.");
    }
}
