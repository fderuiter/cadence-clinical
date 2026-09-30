using System;
using System.Globalization;
using System.Linq;

using Cadence.Rules.Benchmarks;
using Cadence.Rules.Evaluation;

// Usage: dotnet run -c Release --project packages/kernel/benchmarks/Cadence.Rules.Benchmarks -- [records] [runs] [seed]
var records = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 1_000_000;
var runs = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
var seed = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 20260930;

var evaluator = new DotNetRuleEvaluator();
Console.WriteLine($"IRuleEvaluator: {evaluator.GetType().Name}; {records:N0} records; {runs} measured run(s) after one warm-up; seed {seed}.");

var warmUp = RuleBenchmark.Run(evaluator, Math.Min(records, 10_000), seed);
Console.WriteLine($"Warm-up: {warmUp.Rules} rules, {warmUp.Violations:N0} violations.");

var results = Enumerable.Range(1, runs).Select(_ => RuleBenchmark.Run(evaluator, records, seed)).ToList();
foreach (var (result, run) in results.Select((r, i) => (r, i + 1)))
{
    Console.WriteLine(
        $"Run {run}: {result.Elapsed.TotalMilliseconds,10:N0} ms  {result.RecordsPerSecond,12:N0} records/s  " +
        $"{result.AllocatedBytes / 1024d / 1024d,8:N1} MiB allocated  {result.Violations:N0} violations  {result.ResultHash[..16]}");
}

if (results.Select(r => r.ResultHash).Distinct(StringComparer.Ordinal).Count() != 1)
{
    await Console.Error.WriteLineAsync("Runs produced different results: evaluation is not deterministic (K-RULE-4).");
    return 1;
}

Console.WriteLine($"Median: {results.Select(r => r.RecordsPerSecond).Order().ElementAt(results.Count / 2):N0} records/s; every run gave the same result.");
return 0;
