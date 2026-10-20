using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.OpenApi;

/// <summary>
/// The Execution OpenAPI document must describe value objects and statuses the way their JSON converters
/// write them, because the web app generates its typed client from that document.
/// </summary>
public sealed class ExecutionOpenApiWireTypesTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private async Task<JsonElement> GetSchemasAsync()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("components").GetProperty("schemas").Clone();
    }

    [Theory]
    [InlineData("SubjectId")]
    [InlineData("SubjectCode")]
    [InlineData("StudyCode")]
    [InlineData("SiteCode")]
    public async Task ValueObjects_AreDescribedAsStrings(string schemaName)
    {
        var schemas = await GetSchemasAsync();

        var schema = schemas.GetProperty(schemaName);
        schema.GetProperty("type").GetString().Should().Be("string");
        schema.TryGetProperty("properties", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("QueryStatus", new[] { "NONE", "CANDIDATE", "DRAFT_AI", "OPEN", "ANSWERED", "RESOLVED", "CLOSED", "REOPENED", "CANCELLED" })]
    public async Task Statuses_AreDescribedAsTheirWireCodes(string schemaName, string[] codes)
    {
        var schemas = await GetSchemasAsync();

        var schema = schemas.GetProperty(schemaName);
        schema.GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("enum").EnumerateArray().Select(code => code.GetString())
            .Should().BeEquivalentTo(codes);
    }
}
