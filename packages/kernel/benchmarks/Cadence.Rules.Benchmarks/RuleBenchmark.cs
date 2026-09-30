using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Benchmarks;

/// <summary>
/// The outcome of one benchmark run. <see cref="ResultHash"/> is a SHA-256 over every violation in order, so equal
/// hashes mean identical results (K-RULE-4).
/// </summary>
public sealed record BenchmarkResult(int Records, int Rules, int Violations, TimeSpan Elapsed, long AllocatedBytes, string ResultHash)
{
    public double RecordsPerSecond => Records / Math.Max(Elapsed.TotalSeconds, 1e-9);
}

/// <summary>
/// Runs a fixed rule pack over a synthetic dataset through <see cref="IRuleEvaluator"/>. The dataset streams from a
/// seeded generator, so a million records never sit in memory at once, and the same seed always gives the same
/// records. The pack exercises every operator and function of the expression language.
/// </summary>
public static class RuleBenchmark
{
    public static readonly DateOnly AsOf = new(2026, 1, 1);

    private static readonly string[] Positions = ["SITTING", "STANDING", "SUPINE", "", "LYING"];
    private static readonly string[] Units = ["mmHg", "kPa", "mm[Hg]"];

    public static BenchmarkResult Run(IRuleEvaluator evaluator, int records, int seed)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(records);

        var pack = Pack();
        var context = new RuleEvaluationContext(RuleStage.Collection, AsOf);
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();
        var violations = evaluator.Evaluate(pack, context, Dataset(records, seed));
        clock.Stop();
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;

        return new BenchmarkResult(records, pack.Rules.Count, violations.Count, clock.Elapsed, allocated, Hash(violations));
    }

    /// <summary>The synthetic records: two datasets of vital-sign-like measurements with text, numeric and missing values.</summary>
    public static IEnumerable<RuleRecord> Dataset(int count, int seed)
    {
        var random = new Random(seed);
        for (var i = 1; i <= count; i++)
        {
            var values = new Dictionary<string, RuleValue>(8, StringComparer.Ordinal)
            {
                ["systolic"] = Maybe(random, () => RuleValue.FromNumber(random.Next(60, 200))),
                ["diastolic"] = Maybe(random, () => RuleValue.FromNumber(random.Next(30, 130))),
                ["pulse"] = RuleValue.FromNumber(Math.Round((decimal)(random.NextDouble() * 150 + 20), 1)),
                ["position"] = RuleValue.FromText(Positions[random.Next(Positions.Length)]),
                ["unit"] = RuleValue.FromText(Units[random.Next(Units.Length)]),
                ["result_text"] = RuleValue.FromText(random.Next(0, 300).ToString(CultureInfo.InvariantCulture)),
                ["done"] = RuleValue.FromBoolean(random.Next(10) > 0),
            };
            yield return new RuleRecord(random.Next(4) == 0 ? "LABS" : "VITALS", i.ToString(CultureInfo.InvariantCulture), values);
        }
    }

    /// <summary>Twelve rules that together use every operator and function.</summary>
    public static RulePack Pack()
    {
        var rules = new (string Condition, string? Dataset)[]
        {
            ("""{"op":"<","args":[{"var":"systolic"},{"var":"diastolic"}]}""", "VITALS"),
            ("""{"op":"and","args":[{"var":"done"},{"op":"is_empty","args":[{"var":"systolic"}]}]}""", null),
            ("""{"op":"or","args":[{"op":"<","args":[{"var":"pulse"},30]},{"op":">","args":[{"var":"pulse"},160]}]}""", null),
            ("""{"op":"not_in","args":[{"var":"position"},"SITTING","STANDING","SUPINE"]}""", "VITALS"),
            ("""{"op":"not","args":[{"op":"in","args":[{"var":"unit"},"mmHg","kPa"]}]}""", null),
            ("""{"op":">","args":[{"op":"-","args":[{"var":"systolic"},{"var":"diastolic"}]},100]}""", null),
            ("""{"op":">","args":[{"op":"/","args":[{"op":"+","args":[{"var":"systolic"},{"op":"*","args":[2,{"var":"diastolic"}]}]},3]},140]}""", null),
            ("""{"op":">=","args":[{"var":"result_text"},290]}""", "LABS"),
            ("""{"op":">","args":[{"op":"length","args":[{"var":"position"}]},7]}""", null),
            ("""{"op":"and","args":[{"op":"!=","args":[{"var":"done"},true]},{"op":"is_not_empty","args":[{"var":"systolic"}]}]}""", null),
            ("""{"op":"==","args":[{"var":"position"},""]}""", "VITALS"),
            ("""{"op":"<=","args":[{"var":"missing_variable"},0]}""", null),
        };

        var citation = new SourceCitation("Synthetic benchmark rules", null, null);
        return new RulePack(RuleAuthority.Sponsor, "Benchmark pack",
        [
            .. rules.Select((r, i) => new Rule(
                $"B{i + 1:00}", 1, RuleAuthority.Sponsor, citation,
                new RuleApplicability(RuleStage.Collection, null, null, null, r.Dataset),
                RuleSeverity.Warning, Parse(r.Condition), $"Benchmark rule {i + 1}.", null, EffectivePeriod.Always)),
        ]);
    }

    private static RuleValue Maybe(Random random, Func<RuleValue> value) => random.Next(20) == 0 ? RuleValue.Null : value();

    private static RuleExpression Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RuleExpressionJson.Parse(document.RootElement);
    }

    private static string Hash(IReadOnlyList<RuleViolation> violations)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var v in violations)
        {
            sha.AppendData(Encoding.UTF8.GetBytes($"{v.Dataset}|{v.RecordKey}|{v.RuleId}@{v.RuleVersion}|{string.Join(",", v.Observed.Select(o => o.Value))}\n"));
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
