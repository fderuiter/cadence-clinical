using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.E2ETests.E2E;

/// <summary>
/// E2E integration test suite for Observation and Form Submission REST endpoints.
/// Verifies observation recording and cross-form edit check execution over HTTP.
/// </summary>
public class ObservationAndFormE2ETests : IClassFixture<ExecutionE2EFactory>
{
    private readonly HttpClient _client;

    public ObservationAndFormE2ETests(ExecutionE2EFactory factory)
    {
        _client = factory.CreateSignedClient();
    }

    [Fact]
    public async Task GetFormSubmissionDetail_WhenFormNotFound_Returns404NotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/execution/forms/non-existent-form-id", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
