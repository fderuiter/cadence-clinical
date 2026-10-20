using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Mcp;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// The MCP server for outside agents (ADR-2633 §4.8): signed-in callers only, the read profile only, and the same
/// visibility and envelope as in-app agents.
/// </summary>
public sealed class IntelligenceMcpTests
{
    private readonly IntelligenceWorld world = new();

    [Fact]
    public async Task ListsTheReadProfile_MarkedReadOnly()
    {
        await using var app = await StartAsync();

        var result = await RpcAsync(app.GetTestClient(), "tools/list", new { });

        var tools = result.GetProperty("tools").EnumerateArray().ToList();
        tools.Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo(KernelToolNames.McpProfile);
        tools.Should().NotContain(t => t.GetProperty("name").GetString() == KernelToolNames.Simulate);
        tools.Should().OnlyContain(t => t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
    }

    [Fact]
    public async Task CallsAToolAsTheSignedInPerson_ThroughTheEnvelope()
    {
        world.Visibility.Hide(IntelligenceWorld.Requester, IntelligenceWorld.HiddenSite);
        await using var app = await StartAsync();

        var result = await RpcAsync(app.GetTestClient(), "tools/call", new { name = KernelToolNames.SearchKernel, arguments = new { text = "site" } });

        result.GetProperty("isError").GetBoolean().Should().BeFalse();
        var envelope = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
        envelope.GetProperty("trust").GetString().Should().Be(ToolDataPolicy.UntrustedData);
        envelope.GetProperty("data").GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("ref").GetString())
            .Should().Equal(IntelligenceWorld.Site.ToString());
    }

    [Fact]
    public async Task AToolOutsideTheProfile_IsRefused()
    {
        await using var app = await StartAsync();

        var result = await RpcAsync(app.GetTestClient(), "tools/call", new { name = KernelToolNames.Simulate, arguments = new { candidate = "protocol_version:01920000-0000-7000-8000-000000000401" } });

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString().Should().Be(ToolErrors.NotAllowed);
    }

    [Fact]
    public async Task ASignInWithoutAnActiveTenant_ReadsNothing()
    {
        await using var app = await StartAsync(tenant: "unknown");

        var result = await RpcAsync(app.GetTestClient(), "tools/call", new { name = KernelToolNames.GetNode, arguments = new { node = IntelligenceWorld.Site.ToString() } });

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        await using var app = await StartAsync(authenticated: false);

        using var response = await PostAsync(app.GetTestClient(), "tools/list", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<WebApplication> StartAsync(string tenant = "acme", bool authenticated = true)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new TestCaller(authenticated, tenant));
        builder.Services.AddAuthentication(TestCallerHandler.Name).AddScheme<AuthenticationSchemeOptions, TestCallerHandler>(TestCallerHandler.Name, null);
        builder.Services.AddAuthorization(o => o.AddPolicy(CadencePolicies.SignedInCaller, p => p.RequireAuthenticatedUser()));
        world.Register(builder.Services);
        builder.Services.AddIntelligenceMcp();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapIntelligenceMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<JsonElement> RpcAsync(HttpClient client, string method, object parameters)
    {
        using var response = await PostAsync(client, method, parameters);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? body.Split('\n').Last(l => l.StartsWith("data:", StringComparison.Ordinal))["data:".Length..]
            : body;
        var message = JsonDocument.Parse(json).RootElement;
        message.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        return message.GetProperty("result").Clone();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string method, object parameters)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, IntelligenceMcp.Route)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record TestCaller(bool Authenticated, string Tenant);

    private sealed class TestCallerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestCaller caller)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "test-caller";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!caller.Authenticated)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(CadenceClaimTypes.Subject, IntelligenceWorld.Requester.Id), new Claim(CadenceClaimTypes.TenantId, caller.Tenant)], Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
