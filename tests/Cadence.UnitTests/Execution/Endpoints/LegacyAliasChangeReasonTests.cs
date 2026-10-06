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
/// The gateway proxies Execution's undocumented legacy aliases (<c>/api/v1/forms</c>, <c>/api/v1/queries</c>), so each
/// alias that changes data refuses a request without <c>X-Change-Reason</c>, as its documented route does. Without the
/// header the audit trail would record the fallback "system_operation" as the reason for the change (#5060).
/// </summary>
public sealed class LegacyAliasChangeReasonTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private const string Id = "00000000-0000-0000-0000-000000000001";

    public static TheoryData<string, string, string> ChangingRoutes => new()
    {
        { "PUT", $"/api/v1/forms/{Id}", """{"payload":"{}"}""" },
        { "PUT", $"/api/v1/execution/forms/{Id}", """{"payload":"{}"}""" },
        { "POST", $"/api/v1/forms/{Id}/unlock", """{"reason":"Late lab result"}""" },
        { "POST", $"/api/v1/execution/forms/{Id}/unlock", """{"reason":"Late lab result"}""" },
        { "POST", $"/api/v1/queries/{Id}/reopen", """{"reason":"Needs re-check"}""" },
        { "POST", $"/api/v1/execution/queries/{Id}/reopen", """{"reason":"Needs re-check"}""" },
        { "POST", $"/api/v1/queries/{Id}/cancel", """{"reason":"Raised in error"}""" },
        { "POST", $"/api/v1/execution/queries/{Id}/cancel", """{"reason":"Raised in error"}""" },
        { "POST", $"/api/v1/queries/{Id}/respond", """{"response":"Confirmed with source"}""" },
        { "POST", $"/api/v1/execution/queries/{Id}/respond", """{"response":"Confirmed with source"}""" },
        { "POST", $"/api/v1/execution/queries/{Id}/answer", """{"response":"Confirmed with source"}""" },
        { "POST", $"/api/v1/queries/{Id}/close", """{"resolver":"cra-01"}""" },
        { "POST", $"/api/v1/execution/queries/{Id}/close", """{"resolver":"cra-01"}""" },
    };

    /// <summary>A change through an alias or its documented route without a reason is refused before anything is read or written.</summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    [MemberData(nameof(ChangingRoutes))]
    public async Task ChangingRoute_WithoutAChangeReason_Returns400(string method, string path, string body)
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("pi-01", "Site Investigator"));
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
