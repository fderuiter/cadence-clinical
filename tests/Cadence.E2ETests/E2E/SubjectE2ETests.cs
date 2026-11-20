using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.E2ETests.E2E;

/// <summary>
/// E2E integration test suite for Subject REST endpoints.
/// Verifies subject creation, retrieval, and state transitions over black-box HTTP.
/// </summary>
public class SubjectE2ETests : IClassFixture<ExecutionE2EFactory>
{
    private readonly ExecutionE2EFactory _factory;
    private readonly HttpClient _client;

    public SubjectE2ETests(ExecutionE2EFactory factory)
    {
        _factory = factory;
        _client = factory.CreateSignedClient(TestSecurityContext.DefaultIdentity with { SiteIds = ["SITE-101"] });
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
    public async Task CreateSubject_WithValidPayload_Returns201AndIsRetrievable()
    {
        // Arrange
        _factory.SkipUnlessPostgres();
        var request = new SubjectCreateRequest("SUBJ-E2E-999", "STUDY-E2E-001", "SITE-101", null);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var created = await _client.PostAsJsonAsync("/api/v1/execution/subjects", request, ExecutionE2EFactory.JsonOptions, ct);
        var fetched = await _client.GetAsync("/api/v1/execution/subjects/SUBJ-E2E-999", ct);

        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        created.Headers.Location!.ToString().Should().Be("/api/v1/execution/subjects/SUBJ-E2E-999");
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fetched.Content.ReadAsStringAsync(ct)).Should().Contain("SUBJ-E2E-999").And.Contain("STUDY-E2E-001");
    }
}
