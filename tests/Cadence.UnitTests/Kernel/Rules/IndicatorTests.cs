using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Indicators;
using Cadence.Rules.Packs;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Rules;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

/// <summary>
/// Study Pulse metrics and attention rules as versioned indicators (ADR-2488, spec §10.4): a strict content format,
/// evaluation through the one rule evaluator port, reproducible hashes, drill-down to contributing objects and an
/// explanation from the same evaluation.
/// </summary>
public sealed class IndicatorTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 30);
    private static readonly NodeRef Study = NodeRef.For(StudyId.New());

    [Fact]
    public void Json_RoundTripsThroughTheLibraryContentFormat()
    {
        var set = SampleIndicatorSets.Pulse();

        var json = IndicatorSetJson.ToJson(set);
        var read = IndicatorSetJson.Parse(json);

        IndicatorSetJson.ToJson(read).GetRawText().Should().Be(json.GetRawText());
        read.Indicators.Select(i => (i.Id, i.Version)).Should().Equal(("pulse.enrolled", 1), ("pulse.screen_failure_rate", 2), ("qtl.open_error_findings", 1));
        read.Indicators[1].Threshold.Should().Be(set.Indicators[1].Threshold);
        new IndicatorSetContentValidator().Validate(json).Should().BeEmpty();
        new IndicatorSetContentValidator().Kind.Should().Be(LibraryItemKind.IndicatorSet);
    }

    [Theory]
    [InlineData("authority", "\"CDISC\"", "$.authority: \"CDISC\" is not one of Sponsor, Study.")]
    [InlineData("area", "\"Finance\"", "\"Finance\" is not one of Enrollment, Data")]
    [InlineData("measure", "\"Median\"", "\"Median\" is not one of Count, Percent.")]
    [InlineData("population", "\"two words\"", "must be 1 to 64 letters")]
    [InlineData("counts", """{"op":"??","args":[]}""", "indicators[0].counts: unknown operator")]
    [InlineData("threshold", """{"direction":"Above","limit":"ten","severity":"Error"}""", "threshold.limit: required, a number.")]
    [InlineData("kind", "\"Qtl\"", "threshold: required for a Qtl")]
    [InlineData("colour", "\"red\"", "unknown property \"colour\"")]
    public void Json_InvalidContentIsRefusedWithTheProblem(string property, string value, string problem)
    {
        var json = Mutate(IndicatorSetJson.ToJson(SampleIndicatorSets.Pulse()), property, value);

        var (set, problems) = IndicatorSetJson.Read(json);

        set.Should().BeNull();
        problems.Should().Contain(p => p.Contains(problem, StringComparison.Ordinal));
    }

    [Fact]
    public void Json_RefusesAnIndicatorListedTwice()
    {
        var pulse = SampleIndicatorSets.Pulse();
        var twice = pulse with { Indicators = [.. pulse.Indicators, pulse.Indicators[0] with { Version = 2 }] };

        IndicatorSetJson.Read(IndicatorSetJson.ToJson(twice)).Problems.Should().ContainSingle()
            .Which.Should().Contain("\"pulse.enrolled\" appears 2 times");
    }

    /// <summary>
    /// Each indicator counts the records its rule holds for, computes its measure and compares it with its threshold;
    /// the counted objects are the drill-down (UX-7) and the explanation names the rule version (K-EXP-3).
    /// </summary>
    [Fact]
    [KernelRule("K-EXP-3")]
    public void Evaluate_CountsMeasuresAndComparesWithTheThreshold()
    {
        var enrollments = SampleIndicatorSets.EnrollmentRecords("Enrolled", "Randomized", "ScreenFailed", "ScreenFailed", "Enrolled");
        var set = Loaded(SampleIndicatorSets.Pulse());

        var results = Evaluator().Evaluate(set, new IndicatorContext(Study, AsOf), Populations(enrollments, FindingRecords()));

        results.Select(r => (r.Indicator.IndicatorId, r.Population, r.Counted, r.Value, r.Breached)).Should().Equal(
            ("pulse.enrolled", 5, 3, 3m, false),
            ("pulse.screen_failure_rate", 5, 2, 40m, true),
            ("qtl.open_error_findings", 4, 2, 2m, true));

        var rate = results[1];
        rate.Indicator.Should().Be(new IndicatorVersionRef(set.Id, set.Key, set.Version, set.ContentHash, "pulse.screen_failure_rate", 2));
        rate.Area.Should().Be(IndicatorArea.Enrollment);
        rate.Contributors.Should().BeEquivalentTo(new[] { enrollments[2].Node, enrollments[3].Node });
        rate.Contributors.Select(c => c.ToString()).Should().BeInAscendingOrder(StringComparer.Ordinal);
        rate.Explanation.Producer.Should().Be("quality.indicator/pulse.screen_failure_rate@2");
        rate.Explanation.Subject.Should().Be(NodeRef.For(set.Id));
        rate.Explanation.Claim.Should().Be("Screen failure rate is 40% (2 of 5).");
        rate.Explanation.Steps[1].Evidence.Should().Equal(rate.Contributors);
        rate.Explanation.UnmetConditions.Should().ContainSingle().Which.Should().Match<EvaluatedCondition>(
            c => c.Code == IndicatorEvaluator.ThresholdCondition && c.Description == "The value is not above the limit of 30.");
        results[0].Explanation.Conditions.Should().BeEmpty("a metric without a threshold only reports");
    }

    /// <summary>The same inputs give the same hashes in any record order; a changed record or set version changes them (K-RULE-4).</summary>
    [Fact]
    [KernelRule("K-RULE-4")]
    public void Evaluate_IsReproducibleAndIndependentOfRecordOrder()
    {
        var enrollments = SampleIndicatorSets.EnrollmentRecords("Enrolled", "ScreenFailed", "Randomized");
        var set = Loaded(SampleIndicatorSets.Pulse());
        var context = new IndicatorContext(Study, AsOf);
        var evaluator = Evaluator();
        var findings = FindingRecords();

        var first = evaluator.Evaluate(set, context, Populations(enrollments, findings));
        var reversed = evaluator.Evaluate(set, context, Populations([.. enrollments.Reverse()], [.. findings.Reverse()]));
        var changed = evaluator.Evaluate(set, context, Populations(
            [enrollments[0], enrollments[1], enrollments[2] with { Values = new Dictionary<string, RuleValue> { ["STATUS"] = RuleValue.FromText("Withdrawn") } }],
            findings));
        var nextVersion = evaluator.Evaluate(set with { Version = 2, ContentHash = new string('b', 64) }, context, Populations(enrollments, findings));

        reversed.Select(r => (r.InputHash, r.ResultHash)).Should().Equal(first.Select(r => (r.InputHash, r.ResultHash)));
        changed[0].InputHash.Should().NotBe(first[0].InputHash);
        changed[0].ResultHash.Should().NotBe(first[0].ResultHash);
        changed[0].Counted.Should().Be(1);
        nextVersion[0].InputHash.Should().NotBe(first[0].InputHash, "the set version is an input");
        nextVersion[0].Counted.Should().Be(first[0].Counted);
    }

    /// <summary>Every indicator runs through <see cref="IRuleEvaluator"/>, once per population (K-RULE-3).</summary>
    [Fact]
    [KernelRule("K-RULE-3")]
    public void Evaluate_GoesThroughTheRuleEvaluatorPort()
    {
        var port = new RecordingEvaluator();
        new IndicatorEvaluator(port).Evaluate(
            Loaded(SampleIndicatorSets.Pulse()), new IndicatorContext(Study, AsOf),
            Populations(SampleIndicatorSets.EnrollmentRecords("Enrolled"), FindingRecords()));

        port.Datasets.Should().Equal(SampleIndicatorSets.Enrollments, SampleIndicatorSets.Findings);
        port.Packs.Should().OnlyContain(p => p.Rules.Count == 3 && p.Rules.All(r => r.Applicability.Dataset != null));
    }

    [Fact]
    public void Evaluate_APercentageOfNothingHasNoValueAndIsNotBreached()
    {
        var result = Evaluator().Evaluate(Loaded(SampleIndicatorSets.Pulse()), new IndicatorContext(Study, AsOf), Populations([], []))[1];

        result.Value.Should().BeNull();
        result.Breached.Should().BeFalse();
        result.Explanation.Claim.Should().Be("Screen failure rate has no value, because there are no enrollments records.");
        result.Explanation.UnmetConditions.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_LeavesOutIndicatorsNotEffectiveAndListsAtMostTheContributorLimit()
    {
        var pulse = SampleIndicatorSets.Pulse();
        var later = pulse with
        {
            Indicators = [pulse.Indicators[0], pulse.Indicators[2] with { Effective = new EffectivePeriod(AsOf.AddDays(1), null) }],
        };
        var many = SampleIndicatorSets.EnrollmentRecords([.. Enumerable.Repeat("Enrolled", IndicatorEvaluator.MaxContributors + 5)]);

        var results = Evaluator().Evaluate(Loaded(later), new IndicatorContext(Study, AsOf), Populations(many, []));

        results.Should().ContainSingle();
        results[0].Counted.Should().Be(IndicatorEvaluator.MaxContributors + 5);
        results[0].Contributors.Should().HaveCount(IndicatorEvaluator.MaxContributors);
    }

    [Fact]
    public void Evaluate_RefusesAMissingPopulationOrAnObjectCountedTwice()
    {
        var set = Loaded(SampleIndicatorSets.Pulse());
        var context = new IndicatorContext(Study, AsOf);
        var once = SampleIndicatorSets.EnrollmentRecords("Enrolled");
        var missing = new Dictionary<string, IReadOnlyList<IndicatorRecord>> { [SampleIndicatorSets.Enrollments] = once };

        FluentActions.Invoking(() => Evaluator().Evaluate(set, context, missing))
            .Should().Throw<ArgumentException>().WithMessage("*findings*");
        FluentActions.Invoking(() => Evaluator().Evaluate(set, context, Populations([once[0], once[0]], [])))
            .Should().Throw<ArgumentException>().WithMessage("*more than once*");
    }

    private static IndicatorEvaluator Evaluator() => new(new DotNetRuleEvaluator());

    private static LoadedIndicatorSet Loaded(IndicatorSet set) =>
        new(LibraryItemId.New(), "sample-pulse", 1, new string('a', 64), GovernanceStatus.Approved, set);

    private static Dictionary<string, IReadOnlyList<IndicatorRecord>> Populations(IReadOnlyList<IndicatorRecord> enrollments, IReadOnlyList<IndicatorRecord> findings) =>
        new(StringComparer.Ordinal) { [SampleIndicatorSets.Enrollments] = enrollments, [SampleIndicatorSets.Findings] = findings };

    private static IndicatorRecord[] FindingRecords() =>
    [
        Finding("Open", "Error"),
        Finding("Open", "Error"),
        Finding("Resolved", "Error"),
        Finding("Open", "Warning"),
    ];

    private static IndicatorRecord Finding(string status, string severity) => SampleIndicatorSets.Record(
        new NodeRef(NodeKinds.ValidationFinding, Guid.NewGuid()), ("STATUS", RuleValue.FromText(status)), ("SEVERITY", RuleValue.FromText(severity)));

    private static JsonElement Mutate(JsonElement set, string property, string value)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(set.GetRawText())!.AsObject();
        var target = property == "authority" ? root : root["indicators"]![0]!.AsObject();
        target[property] = System.Text.Json.Nodes.JsonNode.Parse(value);
        if (property == "kind")
        {
            target.Remove("threshold");
        }

        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }

    private sealed class RecordingEvaluator : IRuleEvaluator
    {
        private readonly DotNetRuleEvaluator _inner = new();

        public List<string> Datasets { get; } = [];

        public List<RulePack> Packs { get; } = [];

        public IReadOnlyList<RuleViolation> Evaluate(RulePack pack, RuleEvaluationContext context, IEnumerable<RuleRecord> records)
        {
            var list = records.ToList();
            Packs.Add(pack);
            Datasets.AddRange(list.Select(r => r.Dataset).Distinct());
            return _inner.Evaluate(pack, context, list);
        }
    }
}
