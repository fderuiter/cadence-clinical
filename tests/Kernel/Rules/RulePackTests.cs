using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;

using Cadence.Kernel.Standards;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

/// <summary>
/// Rule packs as data (spec §10.1): the pack format is strict and cites public sources (K-RULE-2), the evaluator
/// applies only the rules that fit the context, and evaluation is deterministic (K-RULE-4).
/// </summary>
public class RulePackTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 30);

    private static readonly RuleEvaluationContext Standardization = new(RuleStage.Standardization, AsOf)
    {
        Standard = "SDTMIG",
        StandardVersion = "3.4",
    };

    [Fact(DisplayName = "RulePack_Json_RoundTripsThroughTheLibraryContentFormat")]
    [Trait("Category", "Unit")]
    public void RulePackJsonRoundTripsThroughTheLibraryContentFormat()
    {
        var pack = SamplePack();

        var json = RulePackJson.ToJson(pack);
        var read = RulePackJson.Parse(json);

        RulePackJson.ToJson(read).GetRawText().Should().Be(json.GetRawText());
        read.Rules.Select(r => r.Id).Should().Equal("R1", "R2", "R3", "R4");
        read.Rules[0].Citation.Should().Be(pack.Rules[0].Citation);
        read.Rules[2].Effective.Should().Be(pack.Rules[2].Effective);
        new RulePackContentValidator().Validate(json).Should().BeEmpty();
    }

    [Theory(DisplayName = "RulePack_InvalidContent_ReportsEveryProblem")]
    [Trait("Category", "Unit")]
    [InlineData("citation", "null", "citation: required. Every rule cites the public document it implements (K-RULE-2).")]
    [InlineData("citation", """{"document":"Pinnacle 21 Community validator rules"}""", "cites a vendor's validator")]
    [InlineData("citation", """{"document":"FDA Validator Rules","uri":"ftp://x"}""", "is not an http(s) URI")]
    [InlineData("severity", "\"Fatal\"", "\"Fatal\" is not one of Error, Warning, Notice")]
    [InlineData("applicability", """{"stage":"Lock"}""", "\"Lock\" is not one of Design, Collection")]
    [InlineData("applicability", """{"stage":"Design","standardVersion":"3.4"}""", "standardVersion: needs a standard")]
    [InlineData("condition", """{"op":"??","args":[]}""", "rules[0].condition: unknown operator")]
    [InlineData("version", "0", "version: required, a whole number from 1")]
    [InlineData("id", "\" R1\"", "must be non-blank text")]
    [InlineData("effective", """{"from":"2025-01-01","until":"2024-01-01"}""", "\"until\" must be after \"from\"")]
    [InlineData("colour", "\"red\"", "unknown property \"colour\"")]
    public void RulePackInvalidContentReportsEveryProblem(string property, string value, string expected)
    {
        var rule = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(RulePackJson.ToJson(SamplePack()).GetProperty("rules")[0].GetRawText())!;
        rule[property] = JsonDocument.Parse(value).RootElement;
        var json = JsonDocument.Parse($$"""{"authority":"CDISC","title":"T","rules":[{{JsonSerializer.Serialize(rule)}}]}""").RootElement;

        var (pack, problems) = RulePackJson.Read(json);

        pack.Should().BeNull();
        problems.Should().Contain(p => p.Contains(expected, StringComparison.Ordinal));
        new RulePackContentValidator().Validate(json).Should().Equal(problems);
    }

    [Fact(DisplayName = "RulePack_DuplicateRuleIds_AreRejected")]
    [Trait("Category", "Unit")]
    public void RulePackDuplicateRuleIdsAreRejected()
    {
        var pack = SamplePack();
        var json = RulePackJson.ToJson(pack with { Rules = [pack.Rules[0], pack.Rules[0] with { Version = 2 }] });

        RulePackJson.Read(json).Problems.Should().ContainSingle().Which.Should().Contain("rule \"R1\" appears 2 times");
    }

    [Fact(DisplayName = "RulePackContentValidator_IsTheRulePackKind")]
    [Trait("Category", "Unit")]
    public void RulePackContentValidatorIsTheRulePackKind() =>
        new RulePackContentValidator().Kind.Should().Be(LibraryItemKind.RulePack);

    [Fact(DisplayName = "Evaluator_AppliesOnlyRulesThatFitTheContext_InRecordThenRuleOrder")]
    [Trait("Category", "Unit")]
    public void EvaluatorAppliesOnlyRulesThatFitTheContextInRecordThenRuleOrder()
    {
        var records = new[]
        {
            Record("VITALS", "row-1", ("SYS", 90m), ("DIA", 100m)),
            Record("VITALS", "row-2", ("SYS", 120m), ("DIA", 80m)),
            Record("OTHER", "row-3", ("SYS", 50m), ("DIA", 60m)),
        };

        var violations = new DotNetRuleEvaluator().Evaluate(SamplePack(), Standardization, records);

        // R1 (VITALS only) flags row-1; R2 (any dataset) flags row-1 and row-3; R3 is not yet effective; R4 is a
        // design-stage rule.
        violations.Select(v => (v.RecordKey, v.RuleId)).Should().Equal(("row-1", "R1"), ("row-1", "R2"), ("row-3", "R2"));
        violations[0].Observed.Should().Equal(new ObservedValue("SYS", RuleValue.FromNumber(90m)), new ObservedValue("DIA", RuleValue.FromNumber(100m)));
        violations[0].Severity.Should().Be(RuleSeverity.Error);
        violations[0].Explanation.Should().Be("Systolic below diastolic.");
        violations[0].RuleVersion.Should().Be(3);

        new DotNetRuleEvaluator().Evaluate(SamplePack(), Standardization with { AsOf = new DateOnly(2027, 1, 1) }, records)
            .Select(v => v.RuleId).Should().Contain("R3", "R3 is effective from 2027");
        new DotNetRuleEvaluator().Evaluate(SamplePack(), Standardization with { Standard = "SENDIG" }, records)
            .Select(v => v.RuleId).Should().NotContain("R1", "R1 applies to SDTMIG only");
        new DotNetRuleEvaluator().Evaluate(SamplePack(), new RuleEvaluationContext(RuleStage.Design, AsOf), records)
            .Select(v => v.RuleId).Distinct().Should().Equal("R4");
    }

    /// <summary>
    /// K-RULE-4 as a property over generated packs and records: the same rule versions over the same records give the
    /// same violations, whether evaluated twice, record by record, under another culture, by a fresh evaluator, or
    /// after the pack is written to its stored JSON form and read back.
    /// @req:PRD-CRF-004
    /// </summary>
    [Fact(DisplayName = "Evaluator_IsDeterministic_OverGeneratedPacksAndRecords")]
    [GxPRequirement("PRD-CRF-004")]
    [Trait("Category", "Unit")]
    public void EvaluatorIsDeterministicOverGeneratedPacksAndRecords()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var random = new Random(seed);
            var pack = RandomRuleGenerator.Pack(random, rules: 8);
            var records = RandomRuleGenerator.Records(random, count: 40);
            var evaluator = new DotNetRuleEvaluator();
            var context = new RuleEvaluationContext(RuleStage.Collection, AsOf);

            var expected = Describe(evaluator.Evaluate(pack, context, records));

            Describe(evaluator.Evaluate(pack, context, records)).Should().Equal(expected, $"seed {seed}: a second run");
            Describe(records.SelectMany(r => evaluator.Evaluate(pack, context, [r]))).Should().Equal(expected, $"seed {seed}: record by record");
            Describe(new DotNetRuleEvaluator().Evaluate(RulePackJson.Parse(RulePackJson.ToJson(pack)), context, records))
                .Should().Equal(expected, $"seed {seed}: the stored form");
            foreach (var culture in new[] { "tr-TR", "de-DE", "ar-SA", "ja-JP" })
            {
                Describe(UnderCulture(culture, () => new DotNetRuleEvaluator().Evaluate(pack, context, records)))
                    .Should().Equal(expected, $"seed {seed}: culture {culture}");
            }
        }
    }

    [Fact(DisplayName = "RandomRuleGenerator_ProducesViolations_SoTheDeterminismPropertyIsNotVacuous")]
    [Trait("Category", "Unit")]
    public void RandomRuleGeneratorProducesViolationsSoTheDeterminismPropertyIsNotVacuous()
    {
        var total = Enumerable.Range(1, 50).Sum(seed =>
        {
            var random = new Random(seed);
            return new DotNetRuleEvaluator()
                .Evaluate(RandomRuleGenerator.Pack(random, 8), new RuleEvaluationContext(RuleStage.Collection, AsOf), RandomRuleGenerator.Records(random, 40))
                .Count;
        });

        total.Should().BeGreaterThan(500);
    }

    private static List<string> Describe(IEnumerable<RuleViolation> violations) =>
        [.. violations.Select(v => $"{v.Dataset}|{v.RecordKey}|{v.RuleId}@{v.RuleVersion}|{v.Severity}|{string.Join(",", v.Observed.Select(o => $"{o.Variable}={o.Value}"))}")];

    private static T UnderCulture<T>(string name, Func<T> action)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            return action();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    private static RuleRecord Record(string dataset, string key, params (string Name, decimal Value)[] values) =>
        new(dataset, key, values.ToDictionary(v => v.Name, v => RuleValue.FromNumber(v.Value), StringComparer.Ordinal));

    internal static RulePack SamplePack()
    {
        var citation = new SourceCitation("Sample public guidance", "4.1", "https://example.org/guidance");
        var sdtmig = new RuleApplicability(RuleStage.Standardization, "SDTMIG", "3.4", null, "VITALS");
        var anyDataset = new RuleApplicability(RuleStage.Standardization, null, null, null, null);
        return new RulePack(RuleAuthority.Cdisc, "Sample pack",
        [
            new Rule("R1", 3, RuleAuthority.Cdisc, citation, sdtmig, RuleSeverity.Error,
                Condition("""{"op":"<","args":[{"var":"SYS"},{"var":"DIA"}]}"""), "Systolic below diastolic.", "Check the values.", EffectivePeriod.Always),
            new Rule("R2", 1, RuleAuthority.Cdisc, citation, anyDataset, RuleSeverity.Warning,
                Condition("""{"op":"<","args":[{"var":"SYS"},100]}"""), "Low systolic.", null, EffectivePeriod.Always),
            new Rule("R3", 1, RuleAuthority.Cdisc, citation, anyDataset, RuleSeverity.Notice,
                Condition("""{"op":"is_not_empty","args":[{"var":"SYS"}]}"""), "Future rule.", null, new EffectivePeriod(new DateOnly(2027, 1, 1), null)),
            new Rule("R4", 1, RuleAuthority.Cdisc, citation, new RuleApplicability(RuleStage.Design, null, null, null, null), RuleSeverity.Error,
                Condition("true"), "Design-stage rule.", null, EffectivePeriod.Always),
        ]);
    }

    private static RuleExpression Condition(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RuleExpressionJson.Parse(document.RootElement);
    }
}

/// <summary>Generates random, valid rule packs and records from a seed, for property tests and the benchmark.</summary>
internal static class RandomRuleGenerator
{
    private static readonly string[] Variables = ["A", "B", "C", "D", "E"];
    private static readonly string[] Texts = ["", " ", "X", "Y", "10", "9", "abc", "ÅÍ", "i", "I"];
    private static readonly string[] Datasets = ["ONE", "TWO"];

    public static RulePack Pack(Random random, int rules) => new(RuleAuthority.Sponsor, "Generated",
    [
        .. Enumerable.Range(1, rules).Select(i => new Rule(
            $"G{i}", 1 + random.Next(3), RuleAuthority.Sponsor, new SourceCitation("Generated sponsor rule", null, null),
            new RuleApplicability(RuleStage.Collection, null, null, null, random.Next(3) == 0 ? Datasets[random.Next(Datasets.Length)] : null),
            (RuleSeverity)random.Next(3), Expression(random, 0), $"Generated rule {i}.", null, EffectivePeriod.Always)),
    ]);

    public static List<RuleRecord> Records(Random random, int count) =>
    [
        .. Enumerable.Range(1, count).Select(i => new RuleRecord(
            Datasets[random.Next(Datasets.Length)],
            i.ToString(CultureInfo.InvariantCulture),
            Variables.Where(_ => random.Next(5) > 0).ToDictionary(v => v, _ => Value(random), StringComparer.Ordinal))),
    ];

    private static RuleExpression Expression(Random random, int depth)
    {
        if (depth >= 4 || random.Next(4) == 0)
        {
            return random.Next(2) == 0 ? new VariableExpression(Variables[random.Next(Variables.Length)]) : new LiteralExpression(Value(random));
        }

        RuleExpression Next() => Expression(random, depth + 1);
        return random.Next(7) switch
        {
            0 => new NotExpression(Next()),
            1 => new LogicalExpression((LogicalOperator)random.Next(2), [.. Enumerable.Range(0, 1 + random.Next(3)).Select(_ => Next())]),
            2 or 3 => new ComparisonExpression((ComparisonOperator)random.Next(6), Next(), Next()),
            4 => new ArithmeticExpression((ArithmeticOperator)random.Next(4), Next(), Next()),
            5 => new FunctionExpression((RuleFunction)random.Next(2), [Next()]),
            _ => random.Next(2) == 0
                ? new FunctionExpression((RuleFunction)(2 + random.Next(2)), [Next(), Next(), Next()])
                : new FunctionExpression(RuleFunction.Length, [Next()]),
        };
    }

    private static RuleValue Value(Random random) => random.Next(5) switch
    {
        0 => RuleValue.Null,
        1 => RuleValue.FromBoolean(random.Next(2) == 0),
        2 => RuleValue.FromText(Texts[random.Next(Texts.Length)]),
        _ => RuleValue.FromNumber(Math.Round((decimal)(random.NextDouble() * 200 - 50), random.Next(3))),
    };
}
