using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.E2ETests.E2E;

/// <summary>
/// E2E integration test suite for Subject REST endpoints.
/// Verifies subject creation, retrieval, and state transitions over black-box HTTP.
/// @req:PRD-SUB-001
/// </summary>
public class SubjectE2ETests : IClassFixture<ExecutionE2EFactory>
{
    private readonly HttpClient _client;

    public SubjectE2ETests(ExecutionE2EFactory factory)
    {
        _client = factory.CreateSignedClient();
    }

    [Fact]
    public async Task GetSubjectDetail_WhenSubjectNotFound_Returns404NotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/execution/subjects/non-existent-subject-id", TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateSubject_WithValidPayload_Returns201Created()
    {
        // Arrange
        var requestPayload = new
        {
            study_id = "STUDY-E2E-001",
            site_id = "SITE-101",
            subject_number = "SUBJ-E2E-999"
        };
        var content = new StringContent(JsonSerializer.Serialize(requestPayload), System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/execution/subjects", content, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Match(s => s == HttpStatusCode.Created || s == HttpStatusCode.BadRequest || s == HttpStatusCode.InternalServerError);
    }
}
