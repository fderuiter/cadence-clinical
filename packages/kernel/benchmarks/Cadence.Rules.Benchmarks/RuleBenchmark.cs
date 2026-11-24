using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Kernel.Validation;
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
    private static readonly string[] ResultTexts = [.. Enumerable.Range(0, 300).Select(n => n.ToString(CultureInfo.InvariantCulture))];

    public static BenchmarkResult Run(IRuleEvaluator evaluator, int records, int seed)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(records);

        var pack = Pack();
        var context = new RuleEvaluationContext(ValidationStage.Collection, AsOf);
        using var sink = new IncrementalSha256Sink();
        var cursor = new BenchmarkRecordCursor(records, seed);
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();
        evaluator.Evaluate(pack, context, cursor, sink);
        var hash = sink.CompleteHash();
        clock.Stop();
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;

        return new BenchmarkResult(records, pack.Rules.Count, sink.Violations, clock.Elapsed, allocated, hash);
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
                ["result_text"] = RuleValue.FromText(ResultTexts[random.Next(0, 300)]),
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
                new RuleApplicability(ValidationStage.Collection, null, null, null, r.Dataset),
                ValidationSeverity.Warning, Parse(r.Condition), $"Benchmark rule {i + 1}.", null, EffectivePeriod.Always)),
        ]);
    }

    /// <summary>Computes the canonical SHA-256 digest over a buffered violation sequence.</summary>
    public static string Hash(IReadOnlyList<RuleViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var v in violations)
        {
            sha.AppendData(Encoding.UTF8.GetBytes($"{v.Dataset}|{v.RecordKey}|{v.RuleId}@{v.RuleVersion}|{string.Join(",", v.Observed.Select(o => o.Value))}\n"));
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static RuleValue Maybe(Random random, Func<RuleValue> value) => random.Next(20) == 0 ? RuleValue.Null : value();

    private static RuleExpression Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RuleExpressionJson.Parse(document.RootElement);
    }

    private sealed class BenchmarkRecordCursor(int count, int seed) : IRuleRecordCursor
    {
        private readonly Random _random = new(seed);
        private int _index;
        private string _dataset = string.Empty;
        private string? _key;
        private RuleValue _systolic;
        private RuleValue _diastolic;
        private RuleValue _pulse;
        private RuleValue _position;
        private RuleValue _unit;
        private RuleValue _resultText;
        private RuleValue _done;

        string IRuleRecordCursor.Dataset => _dataset;

        public string Key => _key ??= _index.ToString(CultureInfo.InvariantCulture);

        public RuleValue this[string name] => name switch
        {
            "systolic" => _systolic,
            "diastolic" => _diastolic,
            "pulse" => _pulse,
            "position" => _position,
            "unit" => _unit,
            "result_text" => _resultText,
            "done" => _done,
            _ => RuleValue.Null,
        };

        public bool MoveNext()
        {
            if (_index >= count)
            {
                return false;
            }

            _index++;
            _key = null;
            _systolic = _random.Next(20) == 0 ? RuleValue.Null : RuleValue.FromNumber(_random.Next(60, 200));
            _diastolic = _random.Next(20) == 0 ? RuleValue.Null : RuleValue.FromNumber(_random.Next(30, 130));
            _pulse = RuleValue.FromNumber(Math.Round((decimal)(_random.NextDouble() * 150 + 20), 1));
            _position = RuleValue.FromText(Positions[_random.Next(Positions.Length)]);
            _unit = RuleValue.FromText(Units[_random.Next(Units.Length)]);
            _resultText = RuleValue.FromText(ResultTexts[_random.Next(0, 300)]);
            _done = RuleValue.FromBoolean(_random.Next(10) > 0);
            _dataset = _random.Next(4) == 0 ? "LABS" : "VITALS";
            return true;
        }
    }

    private sealed class IncrementalSha256Sink : IRuleViolationSink, IDisposable
    {
        private readonly IncrementalHash _sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private byte[] _buffer = new byte[4096];
        private int _pos;

        public int Violations { get; private set; }

        public void OnViolation(RuleViolationHit violation)
        {
            Violations++;
            WriteText(violation.Dataset);
            WriteByte((byte)'|');
            WriteText(violation.RecordKey);
            WriteByte((byte)'|');
            WriteText(violation.RuleId);
            WriteByte((byte)'@');
            WriteInt32(violation.RuleVersion);
            WriteByte((byte)'|');

            var observed = violation.Observed;
            for (var i = 0; i < observed.Length; i++)
            {
                if (i > 0)
                {
                    WriteByte((byte)',');
                }

                WriteRuleValue(observed[i].Value);
            }

            WriteByte((byte)'\n');
        }

        public string CompleteHash()
        {
            Flush();
            return Convert.ToHexStringLower(_sha.GetHashAndReset());
        }

        public void Dispose() => _sha.Dispose();

        private void WriteText(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            var maxBytes = Encoding.UTF8.GetMaxByteCount(text.Length);
            EnsureCapacity(maxBytes);
            _pos += Encoding.UTF8.GetBytes(text.AsSpan(), _buffer.AsSpan(_pos));
        }

        private void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _buffer[_pos++] = value;
        }

        private void WriteInt32(int value)
        {
            EnsureCapacity(16);
            Utf8Formatter.TryFormat(value, _buffer.AsSpan(_pos), out var written);
            _pos += written;
        }

        private void WriteRuleValue(RuleValue value)
        {
            switch (value.Kind)
            {
                case RuleValueKind.Null:
                    EnsureCapacity(4);
                    "null"u8.CopyTo(_buffer.AsSpan(_pos));
                    _pos += 4;
                    break;
                case RuleValueKind.Boolean:
                    value.TryGetBoolean(out var boolean);
                    if (boolean)
                    {
                        EnsureCapacity(4);
                        "true"u8.CopyTo(_buffer.AsSpan(_pos));
                        _pos += 4;
                    }
                    else
                    {
                        EnsureCapacity(5);
                        "false"u8.CopyTo(_buffer.AsSpan(_pos));
                        _pos += 5;
                    }

                    break;
                case RuleValueKind.Number:
                    value.TryGetNumber(out var number);
                    EnsureCapacity(64);
                    number.TryFormat(_buffer.AsSpan(_pos), out var written, default, CultureInfo.InvariantCulture);
                    _pos += written;
                    break;
                default:
                    value.TryGetText(out var text);
                    WriteText(text);
                    break;
            }
        }

        private void EnsureCapacity(int needed)
        {
            if (_buffer.Length - _pos >= needed)
            {
                return;
            }

            Flush();
            if (_buffer.Length < needed)
            {
                _buffer = new byte[Math.Max(needed, _buffer.Length * 2)];
            }
        }

        private void Flush()
        {
            if (_pos > 0)
            {
                _sha.AppendData(_buffer.AsSpan(0, _pos));
                _pos = 0;
            }
        }
    }
}
