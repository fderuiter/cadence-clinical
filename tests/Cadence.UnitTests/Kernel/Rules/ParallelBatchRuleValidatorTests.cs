using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

public class ParallelBatchRuleValidatorTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 1);
    private static readonly TenantId TestTenant = TenantId.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly StudyId TestStudy = StudyId.Parse("00000000-0000-0000-0000-000000000002");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "ParallelBatchRuleValidator_NullRequest_ThrowsArgumentNullException")]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_NullRequest_ThrowsArgumentNullException()
    {
        var validator = new ParallelBatchRuleValidator(new DotNetRuleEvaluator(), new BatchJobStore(), new FakeClock());
        Func<Task> act = async () => await validator.ValidateAsync(null!, Ct);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact(DisplayName = "ParallelBatchRuleValidator_SyncExecution_ReturnsCompletedReportWithTiming")]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_SyncExecution_ReturnsCompletedReportWithTiming()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        var jobStore = new BatchJobStore();
        var validator = new ParallelBatchRuleValidator(new DotNetRuleEvaluator(), jobStore, clock);

        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Vitals Validation",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("Ref 1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(140m))),
                    "Systolic blood pressure above 140", null, EffectivePeriod.Always)
            ]);

        var records = new List<RuleRecord>
        {
            new("VS", "rec-1", new Dictionary<string, RuleValue> { ["SYSBP"] = RuleValue.FromNumber(150m) }),
            new("VS", "rec-2", new Dictionary<string, RuleValue> { ["SYSBP"] = RuleValue.FromNumber(120m) })
        };

        var request = new BatchValidationRequest(TestTenant, TestStudy, ValidationStage.Collection, AsOf, records, [pack])
        {
            AsyncThreshold = 100
        };

        var report = await validator.ValidateAsync(request, Ct);

        report.Should().NotBeNull();
        report.Study.Should().Be(TestStudy);
        report.IsAsync.Should().BeFalse();
        report.Status.Should().Be("Completed");
        report.Detections.Should().ContainSingle().Which.RuleId.Should().Be("R1");
        report.Timing.RecordsEvaluated.Should().Be(2);
        report.Timing.RulesEvaluated.Should().Be(1);
        report.Timing.ParallelWorkerCount.Should().BeGreaterThanOrEqualTo(1);

        jobStore.GetJob(report.JobId).Should().NotBeNull();
    }

    [Fact(DisplayName = "ParallelBatchRuleValidator_AsyncExecution_StoresPendingThenCompletedJob")]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_AsyncExecution_StoresPendingThenCompletedJob()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        var jobStore = new BatchJobStore();
        var validator = new ParallelBatchRuleValidator(new DotNetRuleEvaluator(), jobStore, clock);

        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Batch Pack",
            [
                new Rule(
                    "R1", 1, RuleAuthority.Sponsor, new SourceCitation("Ref 1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "LAB"),
                    ValidationSeverity.Warning,
                    new ComparisonExpression(ComparisonOperator.Less, new VariableExpression("ALT"), new LiteralExpression(RuleValue.FromNumber(10m))),
                    "ALT low", null, EffectivePeriod.Always)
            ]);

        var records = Enumerable.Range(1, 15).Select(i => new RuleRecord(
            "LAB",
            $"rec-{i}",
            new Dictionary<string, RuleValue> { ["ALT"] = RuleValue.FromNumber(i) })).ToList();

        var request = new BatchValidationRequest(TestTenant, TestStudy, ValidationStage.Collection, AsOf, records, [pack])
        {
            AsyncThreshold = 5
        };

        var initialReport = await validator.ValidateAsync(request, Ct);

        initialReport.IsAsync.Should().BeTrue();
        initialReport.JobId.Should().StartWith("job_");

        // The job finishes on a background task; poll for it rather than assume a fixed delay is enough
        // when the whole suite is running.
        var waited = Stopwatch.StartNew();
        var finalReport = jobStore.GetJob(initialReport.JobId);
        while (finalReport?.Status != "Completed" && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(20, Ct);
            finalReport = jobStore.GetJob(initialReport.JobId);
        }

        finalReport.Should().NotBeNull();
        finalReport!.Status.Should().Be("Completed");
        finalReport.Timing.RecordsEvaluated.Should().Be(15);
    }

    [Fact(DisplayName = "ParallelBatchRuleValidator_WithLoadedRulePacks_MatchesValidationRunnerFindingIdsAndDeterministicOrder")]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_WithLoadedRulePacks_MatchesValidationRunnerFindingIdsAndDeterministicOrder()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        var evaluator = new DotNetRuleEvaluator();
        var validator = new ParallelBatchRuleValidator(evaluator, new BatchJobStore(), clock);

        var packAlpha = new RulePack(
            RuleAuthority.Sponsor,
            "Human Readable Alpha Pack Title",
            [
                new Rule(
                    "R_VS_HIGH", 1, RuleAuthority.Sponsor, new SourceCitation("Ref A1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "VS"),
                    ValidationSeverity.Error,
                    new ComparisonExpression(ComparisonOperator.Greater, new VariableExpression("SYSBP"), new LiteralExpression(RuleValue.FromNumber(140m))),
                    "High SYSBP", null, EffectivePeriod.Always),
                new Rule(
                    "R_ANY_FLAG", 1, RuleAuthority.Sponsor, new SourceCitation("Ref A2", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, null),
                    ValidationSeverity.Notice,
                    new ComparisonExpression(ComparisonOperator.Equal, new VariableExpression("FLAG"), new LiteralExpression(RuleValue.FromText("Y"))),
                    "Flagged record", null, EffectivePeriod.Always),
            ]);

        var packBeta = new RulePack(
            RuleAuthority.Sponsor,
            "Human Readable Beta Pack Title",
            [
                new Rule(
                    "R_LB_LOW", 2, RuleAuthority.Sponsor, new SourceCitation("Ref B1", null, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "LB"),
                    ValidationSeverity.Warning,
                    new ComparisonExpression(ComparisonOperator.Less, new VariableExpression("HGB"), new LiteralExpression(RuleValue.FromNumber(11m))),
                    "Low HGB", null, EffectivePeriod.Always),
            ]);

        var loadedPacks = new List<LoadedRulePack>
        {
            new(LibraryItemId.New(), "mdr.pack.alpha", 1, "sha256:alpha", GovernanceStatus.Approved, packAlpha),
            new(LibraryItemId.New(), "mdr.pack.beta", 2, "sha256:beta", GovernanceStatus.Approved, packBeta),
        };

        var records = Enumerable.Range(1, 30).Select(BuildInterleavedRecord).ToList();

        var context = new RuleEvaluationContext(ValidationStage.Collection, AsOf);
        var expectedDetections = loadedPacks
            .SelectMany(lp => evaluator.Evaluate(lp.Pack, context, records)
                .Select(v => ValidationRunner.Detection(lp.Key, FindingTargetKind.DatasetRecord, v)))
            .ToList();

        var request = new BatchValidationRequest(
            TestTenant,
            TestStudy,
            ValidationStage.Collection,
            AsOf,
            records,
            [packAlpha, packBeta])
        {
            AsyncThreshold = 1000,
            LoadedRulePacks = loadedPacks,
        };

        var report = await validator.ValidateAsync(request, Ct);

        report.Detections.Select(d => (d.PackKey, d.Target.Dataset, d.Target.Key, d.RuleId))
            .Should().Equal(expectedDetections.Select(d => (d.PackKey, d.Target.Dataset, d.Target.Key, d.RuleId)));

        var batchFindingIds = report.Detections
            .Select(d => ValidationFindingId.For(TestTenant, new FindingIdentity(TestStudy, ValidationStage.Collection, d.PackKey, d.RuleId, d.Target)))
            .ToList();
        var runnerFindingIds = expectedDetections
            .Select(d => ValidationFindingId.For(TestTenant, new FindingIdentity(TestStudy, ValidationStage.Collection, d.PackKey, d.RuleId, d.Target)))
            .ToList();

        batchFindingIds.Should().NotBeEmpty();
        batchFindingIds.Should().Equal(runnerFindingIds);
    }

    private static RuleRecord BuildInterleavedRecord(int index)
    {
        if (index % 2 == 1)
        {
            var sysbp = index % 3 == 0 ? 155m : 120m;
            var flag = index % 5 == 0 ? "Y" : "N";
            return new RuleRecord(
                "VS",
                $"vs-{index:D2}",
                new Dictionary<string, RuleValue>
                {
                    ["SYSBP"] = RuleValue.FromNumber(sysbp),
                    ["FLAG"] = RuleValue.FromText(flag),
                });
        }

        var hgb = index % 4 == 0 ? 9.5m : 13.5m;
        var labFlag = index % 6 == 0 ? "Y" : "N";
        return new RuleRecord(
            "LB",
            $"lb-{index:D2}",
            new Dictionary<string, RuleValue>
            {
                ["HGB"] = RuleValue.FromNumber(hgb),
                ["FLAG"] = RuleValue.FromText(labFlag),
            });
    }
}
