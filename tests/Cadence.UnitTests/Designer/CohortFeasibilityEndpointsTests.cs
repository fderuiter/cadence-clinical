using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Cadence.Apps.Designer.Endpoints;
using Cadence.Core.Synthetic;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cadence.UnitTests.Designer;

public class DesignerTestFactory : WebApplicationFactory<Cadence.Apps.Designer.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
    }
}

public class CohortFeasibilityEndpointsTests : IClassFixture<DesignerTestFactory>
{
    private readonly DesignerTestFactory _factory;

    public CohortFeasibilityEndpointsTests(DesignerTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SimulateDesignerCohortFeasibility_ReturnsStructuredSummary()
    {
        var client = _factory.CreateSignedClient();

        var request = new DesignerCohortFeasibilityRequest(
            StudyId: "STUDY-DESIGNER-100",
            EligibilityCriteria: new List<EligibilityCriterion>
            {
                new("CRIT-01", "Age 18-75", "INCLUSION", "Age", "BETWEEN", "18", HighValue: "75"),
                new("CRIT-02", "Exclude Systolic BP >= 160", "EXCLUSION", "SystolicBp", ">=", "160")
            },
            TargetSampleSize: 200);

        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync("/api/v1/designer/cohort-feasibility/synthetic-simulate", request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<CohortFeasibilitySummary>(cancellationToken: ct);

        summary.Should().NotBeNull();
        summary!.ProtocolId.Should().Be("STUDY-DESIGNER-100");
        summary.EvaluatedCohortSize.Should().BeGreaterThan(0);
        summary.Waterfall.Should().NotBeNull();
        summary.Waterfall.Steps.Should().HaveCount(2);
        summary.SyntheticControlArm.Should().NotBeNull();
        summary.SyntheticControlArm!.MatchedSyntheticArm.Should().NotBeEmpty();
    }
}
