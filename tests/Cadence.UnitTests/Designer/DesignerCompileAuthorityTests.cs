using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cadence.UnitTests.Designer;

/// <summary>
/// Compiling a protocol is study design work: only a caller holding <c>study_design:create</c> (a sponsor designer or
/// a system administrator) may call the Designer compile route (#4958).
/// </summary>
public sealed class DesignerCompileAuthorityTests(DesignerCompileAuthorityTests.DesignerApiFactory factory)
    : IClassFixture<DesignerCompileAuthorityTests.DesignerApiFactory>
{
    private const string Route = "/api/v1/protocols/compile";

    private const string Usdm = """
        {"study":{"id":"STUDY-DESIGN-1","name":"Design authority","version":"1.0"},
         "items":[{"id":"SYS_BP","name":"Systolic Blood Pressure","type":"integer","label":"Systolic BP (mmHg)"}]}
        """;

    [Theory]
    [InlineData("Sponsor Designer")]
    [InlineData("sysadmin")]
    public async Task Compile_ByAStudyDesigner_Succeeds(string role)
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", role));

        var response = await client.PostAsJsonAsync(Route, new { usdmJson = Usdm }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData("CRA")]
    [InlineData("Site Investigator")]
    [InlineData("Data Manager")]
    [InlineData("Subject")]
    public async Task Compile_ByAnyoneElse_Returns403(string role)
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("not-designer", role));

        var response = await client.PostAsJsonAsync(Route, new { usdmJson = Usdm }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Compile_AFormThatFailsTheAccessibilityCheck_Returns422WithTheProblems()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", "Sponsor Designer"));
        const string broken = """{"study":{"id":"S-1","name":"  "},"items":[{"id":"BP","name":"Systolic","type":"integer"}]}""";

        var response = await client.PostAsJsonAsync(Route, new { usdmJson = broken }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("title").GetString().Should().Be("The compiled form failed its accessibility check.");
        problem.GetProperty("violations").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("id").GetString().Should().Be("document-title");
    }

    [Fact]
    public async Task Compile_AStudyWithARuleProblem_Returns422WithTheProblems()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", "Sponsor Designer"));
        const string cyclic = """
            {"study":{"id":"S-1","name":"Cycle"},"items":[
              {"id":"A","name":"A","rules":[{"type":"skip_logic","condition":{"op":"==","args":[{"var":"A"},1]}}]}]}
            """;

        var response = await client.PostAsJsonAsync(Route, new { usdmJson = cyclic }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("problems").EnumerateArray().Select(p => p.GetString()).Should().Equal("Skip logic makes a cycle: A -> A.");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The Designer service with the test gateway secret.</summary>
    public sealed class DesignerApiFactory : WebApplicationFactory<Cadence.Apps.Designer.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseTestGatewaySecret();
    }
}
