using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Designer.Endpoints;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cadence.UnitTests.Designer;

public sealed class RuleBatchEndpointsTests(RuleBatchEndpointsTests.DesignerApiFactory factory)
    : IClassFixture<RuleBatchEndpointsTests.DesignerApiFactory>
{
    [Fact]
    public async Task BatchValidate_ByAStudyDesigner_Succeeds()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", "Sponsor Designer"));

        var pack = SamplePack();
        var records = SampleRecords();

        var request = new BatchValidateApiRequest(
            Records: records,
            RulePacks: [pack],
            IsDryRun: true,
            AsyncThreshold: 100);

        const string studyId = "00000000-0000-0000-0000-000000000101";
        var response = await client.PostAsJsonAsync($"/api/v1/studies/{studyId}/rules/batch-validate", request, Ct);
        var content = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);
        var report = await response.Content.ReadFromJsonAsync<BatchValidationReport>(Ct);
        report.Should().NotBeNull();
        report!.Study.ToString().Should().Be(studyId);
        report.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task BatchValidate_ByUnauthorizedUser_Returns403()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("cra-1", "CRA"));

        var request = new BatchValidateApiRequest();

        var response = await client.PostAsJsonAsync("/api/v1/studies/STUDY-101/rules/batch-validate", request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBatchValidationJob_ReturnsReportOr404()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", "Sponsor Designer"));

        var notFoundRes = await client.GetAsync("/api/v1/studies/STUDY-101/rules/batch-validate/unknown_job_id", Ct); // pragma: allowlist secret
        notFoundRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var request = new BatchValidateApiRequest(
            Records: SampleRecords(),
            RulePacks: [SamplePack()],
            IsDryRun: true,
            AsyncThreshold: 100);

        var postRes = await client.PostAsJsonAsync("/api/v1/studies/STUDY-101/rules/batch-validate", request, Ct);
        var report = await postRes.Content.ReadFromJsonAsync<BatchValidationReport>(Ct);

        var getRes = await client.GetAsync($"/api/v1/studies/STUDY-101/rules/batch-validate/{report!.JobId}", Ct);
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getRes.Content.ReadFromJsonAsync<BatchValidationReport>(Ct);
        fetched!.JobId.Should().Be(report.JobId);
    }

    [Fact]
    public async Task AmendmentRegression_ByAStudyDesigner_Succeeds()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", "Sponsor Designer"));

        var baselinePack = SamplePack();
        var targetPack = SamplePack();

        var request = new AmendmentRegressionApiRequest(
            BaselineVersion: "1",
            TargetVersion: "2",
            BaselinePack: baselinePack,
            TargetPack: targetPack,
            Records: SampleRecords());

        var response = await client.PostAsJsonAsync("/api/v1/studies/STUDY-101/rules/amendment-regression", request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var report = await response.Content.ReadFromJsonAsync<AmendmentRegressionReport>(Ct);
        report.Should().NotBeNull();
        report!.BaselineVersion.Should().Be("v1");
        report.TargetVersion.Should().Be("v2");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RulePack SamplePack() =>
        new(
            RuleAuthority.Sponsor,
            "Sample Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(140m))),
                    "High SYSBP", null, EffectivePeriod.Always)
            ]);

    private static List<RuleRecord> SampleRecords() =>
        [
            new("VS", "rec-1", new Dictionary<string, RuleValue> { ["SYSBP"] = RuleValue.FromNumber(150m) })
        ];

    public sealed class DesignerApiFactory : WebApplicationFactory<Cadence.Apps.Designer.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseTestGatewaySecret();
    }
}
