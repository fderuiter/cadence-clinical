using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Boots the real execution composition root and checks that it only acts on identity the gateway signed:
/// anonymous and forged callers get 401 before anything is read or stored. Role checks and e-signatures on stored
/// records run against PostgreSQL in <c>Cadence.IntegrationTests.Execution.ExecutionWriteAuthorizationTests</c>.
/// </summary>
public sealed class ExecutionAuthenticationTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private const string ReviewQueuePath = "/api/v1/execution/labs/toxicity/review-queue";

    private static readonly GatewayIdentity Cra = TestSecurityContext.Identity("cra-01", "CRA") with { SiteIds = ["SITE-01"] };

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task Healthz_WithoutCredentials_IsAnonymous()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_WithoutGatewaySignature_Returns401()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ReviewQueuePath);
        request.Headers.Add(GatewayHeaders.UserId, "cra-01");
        request.Headers.Add(GatewayHeaders.Roles, "CRA");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_SignedWithWrongSecret_Returns401()
    {
        using var client = factory.CreateClient();
        var forged = TestSecurityContext.CreateHmacHeaders(Cra, secret: "not-the-gateway-secret-0123456789abcdef");

        var response = await SendWithHeadersAsync(client, forged);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_WithRolesAddedAfterSigning_Returns401()
    {
        using var client = factory.CreateClient();
        var headers = new System.Collections.Generic.Dictionary<string, string>(TestSecurityContext.CreateHmacHeaders(Cra))
        {
            [GatewayHeaders.Roles] = "CRA,Data Manager",
        };

        var response = await SendWithHeadersAsync(client, headers);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_WithStaleSignature_Returns401()
    {
        using var client = factory.CreateClient();
        var stale = TestSecurityContext.CreateHmacHeaders(Cra, SystemClock.Instance.UtcNow.AddMinutes(-10));

        var response = await SendWithHeadersAsync(client, stale);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_WithValidSignature_IsAuthenticated()
    {
        using var client = factory.CreateSignedClient(Cra);

        var response = await client.GetAsync(ReviewQueuePath, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public void HostStartup_WithoutGatewaySecret_FailsFast()
    {
        using var unconfigured = new StartupValidationFactory<Cadence.Apps.Execution.Program>(builder => builder
            .UseTestPostgresConnectionString()
            .UseTestDemographicsKey().UseTestRandomizationKey().UseTestFieldProtectionKeys()
            .UseSetting("GATEWAY_SECRET", string.Empty));

        unconfigured.ValidateStartup().Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain("GATEWAY_SECRET");
    }

    private static async Task<HttpResponseMessage> SendWithHeadersAsync(
        HttpClient client,
        System.Collections.Generic.IReadOnlyDictionary<string, string> headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReviewQueuePath);
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
