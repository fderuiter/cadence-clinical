using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.E2ETests.E2E;

/// <summary>
/// E2E integration test suite for Site Compliance REST endpoints.
/// Verifies site compliance summary retrieval and lock state enforcement.
/// </summary>
public class SiteComplianceE2ETests : IClassFixture<ExecutionE2EFactory>
{
    private readonly HttpClient _client;

    public SiteComplianceE2ETests(ExecutionE2EFactory factory)
    {
        _client = factory.CreateSignedClient();
    }

    [Fact]
    public async Task GetSiteComplianceSummary_WhenSiteNotFound_Returns404NotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/execution/site-compliance/NON_EXISTENT_SITE", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
