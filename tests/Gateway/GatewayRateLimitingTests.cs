using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Cadence.Apps.Gateway;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// Checks that the gateway limits each caller separately, answers 429 with <c>Retry-After</c> over the
/// limit, and never limits the health probes.
/// </summary>
public sealed class GatewayRateLimitingTests
{
    private const string StatusPath = "/api/v1/status";

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-189")]
    public async Task CallerOverTheLimit_Gets429WithRetryAfter_WhileOtherCallersAreServed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var gateway = GatewayResilienceTests.CreateGateway(
            "http://127.0.0.1:1", (GatewayRateLimiting.PermitsPerMinuteKey, "2"));
        using var first = GatewayResilienceTests.AuthenticatedClient(gateway, "cra-01");
        using var second = GatewayResilienceTests.AuthenticatedClient(gateway, "cra-02");

        (await first.GetAsync(StatusPath, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.GetAsync(StatusPath, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var limited = await first.GetAsync(StatusPath, ct);

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.TryGetValues("Retry-After", out var retryAfter).Should().BeTrue();
        int.Parse(retryAfter!.Single(), System.Globalization.CultureInfo.InvariantCulture).Should().BePositive();
        (await second.GetAsync(StatusPath, ct)).StatusCode.Should().Be(HttpStatusCode.OK, "each caller has its own limit");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-189")]
    public async Task HealthProbes_AreNeverRateLimited()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var gateway = GatewayResilienceTests.CreateGateway(
            "http://127.0.0.1:1", (GatewayRateLimiting.PermitsPerMinuteKey, "1"));
        using var client = gateway.CreateClient();

        for (int i = 0; i < 5; i++)
        {
            (await client.GetAsync("/healthz/live", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
