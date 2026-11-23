using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Designer.Endpoints;
using Cadence.Kernel;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public async Task BatchValidate_WithoutPacksWhenStudyHasNoPins_Returns422ProblemDetails()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", "Sponsor Designer"));

        var missingPacksResponse = await client.PostAsJsonAsync(
            "/api/v1/studies/STUDY-101/rules/batch-validate",
            new BatchValidateApiRequest(Records: SampleRecords()),
            Ct);
        missingPacksResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await missingPacksResponse.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(422);

        var emptyPacksResponse = await client.PostAsJsonAsync(
            "/api/v1/studies/STUDY-101/rules/batch-validate",
            new BatchValidateApiRequest(Records: [], RulePacks: []),
            Ct);
        emptyPacksResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task BatchValidate_DefaultsRecordsToEmptyAndDerivesDeterministicStudyIdPerCode()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", "Sponsor Designer"));
        var request = new BatchValidateApiRequest(RulePacks: [SamplePack()]);

        var firstResp = await client.PostAsJsonAsync("/api/v1/studies/STUDY-101/rules/batch-validate", request, Ct);
        var secondResp = await client.PostAsJsonAsync("/api/v1/studies/STUDY-101/rules/batch-validate", request, Ct);
        var otherStudyResp = await client.PostAsJsonAsync("/api/v1/studies/STUDY-102/rules/batch-validate", request, Ct);

        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResp.StatusCode.Should().Be(HttpStatusCode.OK);
        otherStudyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstReport = await firstResp.Content.ReadFromJsonAsync<BatchValidationReport>(Ct);
        var secondReport = await secondResp.Content.ReadFromJsonAsync<BatchValidationReport>(Ct);
        var otherReport = await otherStudyResp.Content.ReadFromJsonAsync<BatchValidationReport>(Ct);

        firstReport!.Timing.RecordsEvaluated.Should().Be(0);
        firstReport.Detections.Should().BeEmpty();
        firstReport.Study.Should().Be(secondReport!.Study);
        firstReport.Study.Should().NotBe(otherReport!.Study);
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
    public async Task AmendmentRegression_ByAStudyDesigner_SucceedsAndSharesDeterministicPackKey()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", "Sponsor Designer"));

        var baselinePack = new RulePack(
            RuleAuthority.Sponsor,
            "Baseline Vitals v1",
            [
                new Rule(
                    "R_OLD", 1, RuleAuthority.Sponsor, new SourceCitation("S1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Warning,
                    new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(140m))),
                    "Old SYSBP threshold", null, EffectivePeriod.Always),
            ]);

        var targetPack = new RulePack(
            RuleAuthority.Sponsor,
            "Amended Vitals v2",
            [
                new Rule(
                    "R_NEW", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(145m))),
                    "New SYSBP threshold", null, EffectivePeriod.Always),
            ]);

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
        report.ResolvedFindings.Should().ContainSingle();
        report.NewViolations.Should().ContainSingle();
        report.ResolvedFindings[0].PackKey.Should().Be(report.NewViolations[0].PackKey);
        report.NewViolations[0].PackKey.Should().NotBe("pack_target").And.NotBe("pack_baseline");
    }

    [Fact]
    public async Task AmendmentRegression_WithoutPacksWhenStudyHasNoPins_Returns422ProblemDetails()
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("designer-1", "Sponsor Designer"));

        var response = await client.PostAsJsonAsync(
            "/api/v1/studies/STUDY-101/rules/amendment-regression",
            new AmendmentRegressionApiRequest(),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(422);
    }

    [Fact]
    public async Task Endpoints_LoadPinnedRulePacksAndStandardSchemasFromRegistry()
    {
        var studyId = StudyId.Parse("00000000-0000-0000-0000-000000000202");
        var packV1Id = LibraryItemId.New();
        var packV2Id = LibraryItemId.New();
        var stdVersionId = StandardVersionId.New();

        var packV1 = SamplePack();
        var packV2 = new RulePack(
            RuleAuthority.Sponsor,
            "Sample Pack",
            [
                packV1.Rules[0] with { Severity = ValidationSeverity.Warning },
                new Rule(
                    "R_BAD_REF", 1, RuleAuthority.Sponsor, new SourceCitation("S2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("MISSING_COL"), new LiteralExpression(RuleValue.FromNumber(1m))),
                    "Missing col check", null, EffectivePeriod.Always),
            ]);

        var v1Json = RulePackJson.ToJson(packV1);
        var v2Json = RulePackJson.ToJson(packV2);
        var v1View = new LibraryItemView(
            packV1Id, LibraryItemKind.RulePack, "sponsor.vitals", 1, "Sample Pack", v1Json,
            ContentHash.Compute(v1Json.GetRawText()), GovernanceStatus.Approved, [stdVersionId], AttestationId.New(), null);
        var v2View = new LibraryItemView(
            packV2Id, LibraryItemKind.RulePack, "sponsor.vitals", 2, "Sample Pack", v2Json,
            ContentHash.Compute(v2Json.GetRawText()), GovernanceStatus.Approved, [stdVersionId], AttestationId.New(), null);

        var standardPackage = new StandardPackage(
            "SDTMIG", "3.4", "SDTMIG 3.4", "CDISC", null, null,
            [
                new DatasetDefinitionData(
                    "VS", "Vital Signs", "Findings", null, 1,
                    [new VariableDefinitionData("SYSBP", "Systolic BP", 1, StandardDataType.Num, null, VariableCore.Expected, [], [], null, null)]),
            ],
            [], []);
        var profile = new StudyStandardsProfileView(
            StudyStandardsProfileId.New(), studyId, 1, [stdVersionId], [], [packV1Id]);
        var registry = new StubStandardsRegistry(profile, [v1View, v2View], stdVersionId, standardPackage);

        using var customFactory = factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.RemoveAll<RulePackLoader>();
                services.AddSingleton<IStandardsRegistry>(registry);
                services.AddSingleton<RulePackLoader>();
            }));
        using var client = customFactory.CreateSignedClient(
            TestSecurityContext.Identity("designer-1", "Sponsor Designer") with { StudyIds = [studyId.ToString()] });

        var batchResp = await client.PostAsJsonAsync(
            $"/api/v1/studies/{studyId}/rules/batch-validate",
            new BatchValidateApiRequest(Records: SampleRecords()),
            Ct);
        batchResp.StatusCode.Should().Be(HttpStatusCode.OK, await batchResp.Content.ReadAsStringAsync(Ct));
        var batchReport = await batchResp.Content.ReadFromJsonAsync<BatchValidationReport>(Ct);
        batchReport!.Detections.Should().ContainSingle().Which.PackKey.Should().Be("sponsor.vitals");

        var regResp = await client.PostAsJsonAsync(
            $"/api/v1/studies/{studyId}/rules/amendment-regression",
            new AmendmentRegressionApiRequest(BaselineVersion: "1", TargetVersion: "2"),
            Ct);
        regResp.StatusCode.Should().Be(HttpStatusCode.OK, await regResp.Content.ReadAsStringAsync(Ct));
        var regReport = await regResp.Content.ReadFromJsonAsync<AmendmentRegressionReport>(Ct);
        regReport!.RuleDiffs.ModifiedRules.Should().ContainSingle().Which.Id.Should().Be("R1");
        regReport.BrokenAstReferences.Should().ContainSingle().Which.ReferencedVariable.Should().Be("MISSING_COL");
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

    private sealed class StubStandardsRegistry(
        StudyStandardsProfileView profile,
        IReadOnlyList<LibraryItemView> items,
        StandardVersionId stdId,
        StandardPackage stdPackage) : IStandardsRegistry
    {
        private readonly Dictionary<LibraryItemId, LibraryItemView> _byId = items.ToDictionary(i => i.Id);

        public Task<IReadOnlyList<StandardVersionSummary>> ListStandardVersionsAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StandardVersionSummary>>([]);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, string standardCode, string version, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardPackage?> GetStandardPackageAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult(id == stdId ? stdPackage : null);

        public Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
            Task.FromResult(_byId.GetValueOrDefault(id));

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>([.. items.Where(i => i.Kind == kind && string.Equals(i.Key, key, StringComparison.Ordinal)).OrderByDescending(i => i.Version)]);

        public Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult(study == profile.StudyId ? profile : null);
    }

    public sealed class DesignerApiFactory : WebApplicationFactory<Cadence.Apps.Designer.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseTestGatewaySecret();
    }
}
