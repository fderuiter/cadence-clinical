using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Cadence.Apps.AiGateway.Endpoints;
using Cadence.Core.Synthetic;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cadence.UnitTests.AiGateway;

public class AiGatewayTestFactory : WebApplicationFactory<Cadence.Apps.AiGateway.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
    }
}

public class SyntheticControlEndpointsTests : IClassFixture<AiGatewayTestFactory>
{
    private readonly AiGatewayTestFactory _factory;

    public SyntheticControlEndpointsTests(AiGatewayTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SimulateSyntheticControlArm_ReturnsMatchedPayload()
    {
        var client = _factory.CreateSignedClient();

        var request = new PropensityMatchingRequest(
            StudyId: "STUDY-AI-001",
            EligibilityCriteria: new List<EligibilityCriterion>
            {
                new("CRIT-01", "Age 18-75", "INCLUSION", "Age", "BETWEEN", "18", HighValue: "75"),
                new("CRIT-02", "Exclude BP > 160", "EXCLUSION", "SystolicBp", ">=", "160")
            },
            TargetSampleSize: 100,
            Caliper: 0.25);

        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync("/api/v1/ai/synthetic-controls/simulate", request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PropensityMatchingResult>(cancellationToken: ct);

        result.Should().NotBeNull();
        result!.StudyId.Should().Be("STUDY-AI-001");
        result.MatchedControlCount.Should().BeGreaterThan(0);
        result.CovariateBalance.Should().NotBeEmpty();
        result.MatchedSyntheticArm.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SimulateEligibilityOptimization_ReturnsAttritionWaterfall()
    {
        var client = _factory.CreateSignedClient();

        var request = new EligibilityOptimizationRequest(
            ProtocolId: "PROT-OPT-101",
            Criteria: new List<EligibilityCriterion>
            {
                new("CRIT-01", "Age >= 18", "INCLUSION", "Age", ">=", "18"),
                new("CRIT-02", "Systolic BP <= 150", "INCLUSION", "SystolicBp", "<=", "150")
            },
            InitialCohortSize: 1000);

        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync("/api/v1/ai/protocol-optimization/eligibility", request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AttritionWaterfallResult>(cancellationToken: ct);

        result.Should().NotBeNull();
        result!.ProtocolId.Should().Be("PROT-OPT-101");
        result.InitialPopulationCount.Should().Be(1000);
        result.Steps.Should().HaveCount(2);
        result.OverallFeasibilityYieldPercentage.Should().BeInRange(0.0, 100.0);
    }

    [Fact]
    public async Task SimulateSyntheticControlArm_RejectsInvalidCriteria()
    {
        var client = _factory.CreateSignedClient();

        var request = new PropensityMatchingRequest(
            StudyId: "STUDY-INVALID",
            EligibilityCriteria: new List<EligibilityCriterion>
            {
                new("CRIT-BAD", "Bad Operator", "INCLUSION", "Age", "INVALID_OP", "18")
            });

        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync("/api/v1/ai/synthetic-controls/simulate", request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
