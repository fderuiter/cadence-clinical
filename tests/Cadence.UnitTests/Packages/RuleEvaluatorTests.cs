using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain.Rules;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Pins <see cref="RuleEvaluator"/> to the Python reference in <c>packages/engine_core/rules/evaluator.py</c>, deleted once these tests landed and kept in git history:
/// arithmetic, comparisons, logical operators, every function, date handling, strict-variable mode and
/// <see cref="RuleEvaluator.ParseJsonLogic"/>. Integral numeric results come back as <see cref="long"/> where the
/// C# port normalises them, and as <see cref="double"/> otherwise.
/// </summary>
public class RuleEvaluatorTests
{
    private static readonly DateTime AsOf = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    private static ExpressionNode C(object? value) => new() { Type = "constant", Value = value };

    private static ExpressionNode V(string name) => new() { Type = "variable", VariableName = name };

    private static ExpressionNode F(string fieldId) => new() { Type = "field_ref", FieldRef = new FieldReference(fieldId) };

    private static ExpressionNode Node(string type, string op, params ExpressionNode[] operands) =>
        new() { Type = type, Operator = op, Operands = operands.ToList() };

    private static ExpressionNode Arith(string op, params object?[] values) =>
        Node("arithmetic", op, values.Select(C).ToArray());

    private static ExpressionNode Cmp(string op, params object?[] values) =>
        Node("comparison", op, values.Select(C).ToArray());

    private static ExpressionNode Fn(string op, params object?[] values) =>
        Node("function", op, values.Select(C).ToArray());

    private static object? Eval(ExpressionNode node, Dictionary<string, object?>? context = null, bool strict = false) =>
        new RuleEvaluator(AsOf, strict).Evaluate(node, context ?? []);

    // ---------------------------------------------------------------- Node types

    [Fact]
    public void Evaluate_Constant_ReturnsValue()
    {
        Eval(C("abc")).Should().Be("abc");
        Eval(C(null)).Should().BeNull();
    }

    [Fact]
    public void Evaluate_UnknownNodeType_Throws()
    {
        var act = () => Eval(new ExpressionNode { Type = "lambda" });

        act.Should().Throw<EvaluationException>().WithMessage("Unknown node type: 'lambda'");
    }

    [Fact]
    public void Evaluate_FieldRefWithoutReference_ReturnsNull()
    {
        Eval(new ExpressionNode { Type = "field_ref" }, strict: true).Should().BeNull();
    }

    // ---------------------------------------------------------------- Variable resolution

    [Fact]
    public void Evaluate_Variable_ResolvesDottedPathsAndClinicalNamespaces()
    {
        var context = new Dictionary<string, object?>
        {
            ["vitals.hr"] = 72,
            ["vitals"] = new Dictionary<string, object?> { ["sbp"] = 120 },
            ["data"] = new Dictionary<string, object?> { ["weight"] = 70.5 },
            ["form"] = new Dictionary<string, object?> { ["visit"] = "V1" },
        };

        Eval(V("vitals.hr"), context).Should().Be(72);
        Eval(V("vitals.sbp"), context).Should().Be(120);
        Eval(V("weight"), context).Should().Be(70.5);
        Eval(F("visit"), context).Should().Be("V1");
        Eval(V("vitals.dbp"), context).Should().BeNull();
    }

    [Fact]
    public void Evaluate_Variable_DirectKeyWinsOverNamespace()
    {
        var context = new Dictionary<string, object?>
        {
            ["age"] = 40,
            ["subject"] = new Dictionary<string, object?> { ["age"] = 50 },
        };

        Eval(V("age"), context).Should().Be(40);
    }

    // ---------------------------------------------------------------- Strict variables

    [Fact]
    public void StrictMode_MissingVariable_Throws()
    {
        var act = () => Eval(V("missing"), strict: true);

        act.Should().Throw<UndefinedVariableException>().WithMessage("Undefined variable: 'missing'");
    }

    [Fact]
    public void StrictMode_VariableKeyPresentWithNullValue_ReturnsNull()
    {
        var context = new Dictionary<string, object?> { ["ae_term"] = null };

        Eval(V("ae_term"), context, strict: true).Should().BeNull();
    }

    [Fact]
    public void StrictMode_VariableFoundInNamespace_DoesNotThrow()
    {
        var context = new Dictionary<string, object?> { ["data"] = new Dictionary<string, object?> { ["hr"] = 60 } };

        Eval(V("hr"), context, strict: true).Should().Be(60);
    }

    [Fact]
    public void StrictMode_FieldRefWithNullValue_ThrowsEvenWhenKeyExists()
    {
        var context = new Dictionary<string, object?> { ["ae_term"] = null };

        var act = () => Eval(F("ae_term"), context, strict: true);

        act.Should().Throw<UndefinedVariableException>().WithMessage("Undefined field reference: 'ae_term'");
    }

    [Fact]
    public void NonStrictMode_MissingVariableAndField_ReturnNull()
    {
        Eval(V("missing")).Should().BeNull();
        Eval(F("missing")).Should().BeNull();
    }

    [Fact]
    public void StrictMode_AndShortCircuits_BeforeMissingVariable()
    {
        var node = Node("logical", "and", C(false), V("missing"));

        Eval(node, strict: true).Should().Be(false);
    }

    // ---------------------------------------------------------------- Logical

    [Theory]
    [InlineData("and", true, true, true)]
    [InlineData("and", true, false, false)]
    [InlineData("or", false, true, true)]
    [InlineData("or", false, false, false)]
    [InlineData("xor", true, false, true)]
    [InlineData("xor", true, true, false)]
    public void Logical_BinaryOperators(string op, bool left, bool right, bool expected)
    {
        Eval(Node("logical", op, C(left), C(right))).Should().Be(expected);
    }

    [Fact]
    public void Logical_Not_NegatesTruthiness()
    {
        Eval(Node("logical", "not", C(true))).Should().Be(false);
        Eval(Node("logical", "not", C(null))).Should().Be(true);
    }

    [Fact]
    public void Logical_FalsyValues_FollowPythonTruthiness()
    {
        object?[] falsy = [null, false, 0, 0L, 0.0, "", new List<object?>(), new Dictionary<string, object?>()];

        foreach (var value in falsy)
        {
            Eval(Node("logical", "not", C(value))).Should().Be(true, "Python bool({0}) is False", value);
        }
    }

    [Fact]
    public void Logical_TruthyValues_FollowPythonTruthiness()
    {
        object?[] truthy = [true, 1, -2L, 0.5, "0", "no", new List<object?> { 0 }];

        foreach (var value in truthy)
        {
            Eval(Node("logical", "not", C(value))).Should().Be(false, "Python bool({0}) is True", value);
        }
    }

    [Fact]
    public void Logical_IfThenElse_SelectsBranch()
    {
        Eval(Node("logical", "if_then_else", C(true), C("yes"), C("no"))).Should().Be("yes");
        Eval(Node("logical", "if_then_else", C(0), C("yes"), C("no"))).Should().Be("no");
        Eval(Node("logical", "if_then_else", C(false), C("yes"))).Should().BeNull();
    }

    [Fact]
    public void Logical_UnsupportedOperator_Throws()
    {
        var act = () => Eval(Node("logical", "nand", C(true), C(true)));

        act.Should().Throw<EvaluationException>().WithMessage("Unsupported logical operator: 'nand'");
    }

    // ---------------------------------------------------------------- Comparison

    [Theory]
    [InlineData(1, 1.0, true)]
    [InlineData(2, 3, false)]
    [InlineData("true", true, true)]
    [InlineData("1", true, true)]
    [InlineData("yes", true, true)]
    [InlineData("no", true, false)]
    [InlineData(false, "false", true)]
    [InlineData("abc", "abc", true)]
    [InlineData(null, null, true)]
    [InlineData(null, 0, false)]
    public void Comparison_Equality(object? left, object? right, bool expected)
    {
        Eval(Cmp("==", left, right)).Should().Be(expected);
        Eval(Cmp("!=", left, right)).Should().Be(!expected);
    }

    [Theory]
    [InlineData("<", 3, 5, true)]
    [InlineData("<=", 5, 5, true)]
    [InlineData(">", 5.5, 5, true)]
    [InlineData(">=", 4, 5, false)]
    // Numeric strings compare as numbers, not text: "10" > "9".
    [InlineData(">", "10", "9", true)]
    [InlineData("<", " 2 ", 10, true)]
    // ISO dates compare chronologically.
    [InlineData("<", "2024-01-15", "2024-02-01", true)]
    [InlineData(">=", "2024-03-01T00:00:00Z", "2024-02-29T23:00:00-02:00", false)]
    // Text falls back to ordinal comparison.
    [InlineData("<", "apple", "banana", true)]
    [InlineData(">", "B", "a", false)]
    // Mixed or boolean operands are not comparable.
    [InlineData("<", "abc", 5, false)]
    [InlineData(">", true, false, false)]
    [InlineData("<", null, 5, false)]
    public void Comparison_Ordering(string op, object? left, object? right, bool expected)
    {
        Eval(Cmp(op, left, right)).Should().Be(expected);
    }

    [Fact]
    public void Comparison_DecimalStrings_AreNotReadAsDates()
    {
        // "10.5" and "10.25" are numbers; parsing them as 5 and 25 October would invert the answer.
        Eval(Cmp("<", "10.5", "10.25")).Should().Be(false);
        Eval(Cmp(">", "10.5", "10.25")).Should().Be(true);
    }

    [Fact]
    public void Comparison_NumbersWithSeparatorsOrParentheses_AreNotNumeric()
    {
        Eval(Cmp(">", "1,000", 5)).Should().Be(false);
        Eval(Cmp("<", "(5)", 0)).Should().Be(false);
    }

    [Fact]
    public void Comparison_In_ListAndSubstring()
    {
        Eval(Cmp("in", "b", new List<object?> { "a", "b" })).Should().Be(true);
        Eval(Cmp("in", "c", new List<object?> { "a", "b" })).Should().Be(false);
        Eval(Cmp("in", "ell", "hello")).Should().Be(true);
        Eval(Cmp("in", "ELL", "hello")).Should().Be(false);
        Eval(Cmp("in", "a", null)).Should().Be(false);
        Eval(Cmp("in", 1, 5)).Should().Be(false);
        Eval(Cmp("not_in", "c", new List<object?> { "a", "b" })).Should().Be(true);
        Eval(Cmp("not_in", "a", new List<object?> { "a", "b" })).Should().Be(false);
    }

    [Theory]
    [InlineData(5, 1, 10, true)]
    [InlineData(1, 1, 10, true)]
    [InlineData(10, 1, 10, true)]
    [InlineData(11, 1, 10, false)]
    [InlineData("5.5", "5", "6", true)]
    [InlineData("2024-02-10", "2024-02-01", "2024-02-29", true)]
    [InlineData("2024-03-10", "2024-02-01", "2024-02-29", false)]
    [InlineData("abc", 1, 10, false)]
    [InlineData(5, null, 10, false)]
    public void Comparison_Between_IsInclusive(object? value, object? low, object? high, bool expected)
    {
        Eval(Cmp("between", value, low, high)).Should().Be(expected);
    }

    [Theory]
    [InlineData("ABC-123", @"\d{3}", true)]
    [InlineData("ABC-123", @"^\d", false)]
    [InlineData(12345, "^123", true)]
    [InlineData("abc", "[unclosed", false)]
    [InlineData(null, "a", false)]
    [InlineData("a", null, false)]
    public void Comparison_RegexMatch_SearchesAnywhere(object? value, object? pattern, bool expected)
    {
        Eval(Cmp("regex_match", value, pattern)).Should().Be(expected);
    }

    [Fact]
    public void Comparison_UnsupportedOperator_Throws()
    {
        var act = () => Eval(Cmp("<>", 1, 2));

        act.Should().Throw<EvaluationException>().WithMessage("Unsupported comparison operator: '<>'");
    }

    // ---------------------------------------------------------------- Arithmetic

    [Fact]
    public void Arithmetic_Addition()
    {
        Eval(Arith("+", 2, 3, 4)).Should().Be(9L);
        Eval(Arith("+", 2.5, 0.25)).Should().Be(2.75);
        Eval(Arith("+", "2", 3)).Should().Be("23");
        Eval(Arith("+", "BMI: ", 22.5, null)).Should().Be("BMI: 22.5");
        Eval(Arith("+", 1, null)).Should().BeNull();
    }

    [Fact]
    public void Arithmetic_Subtraction()
    {
        Eval(Arith("-", 10, 4)).Should().Be(6L);
        Eval(Arith("-", 1, 0.25)).Should().Be(0.75);
        Eval(Arith("-", 5)).Should().Be(-5.0);
        Eval(Arith("-", "x")).Should().BeNull();
        Eval(Arith("-", 1, null)).Should().BeNull();
    }

    [Fact]
    public void Arithmetic_Multiplication()
    {
        Eval(Arith("*", 2, 3, 4)).Should().Be(24L);
        Eval(Arith("*", 2, 1.5)).Should().Be(3L);
        Eval(Arith("*", 2, 0.25)).Should().Be(0.5);
        Eval(Arith("*", "3", 2)).Should().Be(6L);
        Eval(Arith("*", "x", 2)).Should().BeNull();
    }

    [Fact]
    public void Arithmetic_Division()
    {
        Eval(Arith("/", 7, 2)).Should().Be(3.5);
        Eval(Arith("/", 8, 2)).Should().Be(4L);
        Eval(Arith("/", 1, 0)).Should().BeNull();
        Eval(Arith("/", null, 2)).Should().BeNull();
        Eval(Arith("//", 7, 2)).Should().Be(3L);
        Eval(Arith("//", -7, 2)).Should().Be(-4L);
        Eval(Arith("//", 7, 0)).Should().BeNull();
    }

    [Theory]
    [InlineData(7, 3, 1.0)]
    [InlineData(-7, 3, 2.0)]
    [InlineData(7, -3, -2.0)]
    [InlineData(-7, -3, -1.0)]
    [InlineData(6, 3, 0.0)]
    [InlineData(5.5, 2, 1.5)]
    public void Arithmetic_Modulo_IsFlooredLikePython(object left, object right, double expected)
    {
        Eval(Arith("%", left, right)).Should().Be(expected);
    }

    [Fact]
    public void Arithmetic_ModuloByZero_ReturnsNull()
    {
        Eval(Arith("%", 5, 0)).Should().BeNull();
    }

    [Fact]
    public void Arithmetic_Power()
    {
        Eval(Arith("^", 2, 10)).Should().Be(1024L);
        Eval(Arith("**", 2, -1)).Should().Be(0.5);
        Eval(Arith("**", 9, 0.5)).Should().Be(3L);
        Eval(Arith("^", 2, null)).Should().BeNull();
    }

    [Fact]
    public void Arithmetic_UnaryFunctions()
    {
        Eval(Arith("abs", -3.5)).Should().Be(3.5);
        Eval(Arith("abs", "x")).Should().BeNull();
        Eval(Arith("floor", 2.7)).Should().Be(2.0);
        Eval(Arith("floor", -2.1)).Should().Be(-3.0);
        Eval(Arith("ceil", 2.1)).Should().Be(3.0);
        Eval(Arith("ceil", (object?)null)).Should().BeNull();
    }

    [Fact]
    public void Arithmetic_Round_UsesBankersRoundingAndOptionalDigits()
    {
        Eval(Arith("round", 2.5)).Should().Be(2.0);
        Eval(Arith("round", 3.5)).Should().Be(4.0);
        Eval(Arith("round", 3.14159, 2)).Should().Be(3.14);
        Eval(Arith("round", 1234, -2)).Should().Be(1200.0);
        Eval(Arith("round", null, 2)).Should().BeNull();
    }

    [Fact]
    public void Arithmetic_UnsupportedOperator_Throws()
    {
        var act = () => Eval(Arith("sqrt", 4));

        act.Should().Throw<EvaluationException>().WithMessage("Unsupported arithmetic operator: 'sqrt'");
    }

    // ---------------------------------------------------------------- Functions: emptiness and strings

    [Fact]
    public void Function_IsEmpty()
    {
        Eval(Fn("is_empty", (object?)null)).Should().Be(true);
        Eval(Fn("is_empty", "")).Should().Be(true);
        Eval(Fn("empty", new List<object?>())).Should().Be(true);
        Eval(Fn("is_empty", new Dictionary<string, object?>())).Should().Be(true);
        Eval(Fn("is_empty", " ")).Should().Be(false);
        Eval(Fn("is_empty", 0)).Should().Be(false);
        Eval(Fn("is_empty", new List<object?> { 1 })).Should().Be(false);
        Eval(Fn("is_not_empty", "x")).Should().Be(true);
        Eval(Fn("is_not_empty", (object?)null)).Should().Be(false);
    }

    [Fact]
    public void Function_Coalesce_SkipsNullAndEmptyString()
    {
        Eval(Fn("coalesce", null, "", "x", "y")).Should().Be("x");
        Eval(Fn("coalesce", null, 0)).Should().Be(0);
        Eval(Fn("if_null", null, "")).Should().BeNull();
    }

    [Fact]
    public void Function_Contains()
    {
        Eval(Fn("contains", new List<object?> { "a", "b" }, "b")).Should().Be(true);
        Eval(Fn("contains", new List<object?> { "a", "b" }, "c")).Should().Be(false);
        Eval(Fn("contains", "Severe Headache", "headache")).Should().Be(true);
        Eval(Fn("contains", "Headache", "fever")).Should().Be(false);
        Eval(Fn("contains", null, "a")).Should().Be(false);
        Eval(Fn("contains", "a", null)).Should().Be(false);
    }

    [Fact]
    public void Function_StartsAndEndsWith_AreCaseSensitive()
    {
        Eval(Fn("starts_with", "SUBJ-001", "SUBJ")).Should().Be(true);
        Eval(Fn("starts_with", "SUBJ-001", "subj")).Should().Be(false);
        Eval(Fn("starts_with", 12345, 12)).Should().Be(true);
        Eval(Fn("starts_with", null, "a")).Should().Be(false);
        Eval(Fn("ends_with", "report.pdf", ".pdf")).Should().Be(true);
        Eval(Fn("ends_with", "report.pdf", ".PDF")).Should().Be(false);
        Eval(Fn("ends_with", "a", null)).Should().Be(false);
    }

    [Fact]
    public void Function_CaseAndTrim()
    {
        Eval(Fn("lower", "MiXeD")).Should().Be("mixed");
        Eval(Fn("upper", "MiXeD")).Should().Be("MIXED");
        Eval(Fn("upper", 1.5)).Should().Be("1.5");
        Eval(Fn("trim", "  padded  ")).Should().Be("padded");
        Eval(Fn("lower", (object?)null)).Should().Be("");
        Eval(Fn("upper", (object?)null)).Should().Be("");
        Eval(Fn("trim", (object?)null)).Should().Be("");
    }

    [Fact]
    public void Function_Length()
    {
        Eval(Fn("length", (object?)null)).Should().Be(0);
        Eval(Fn("length", "hello")).Should().Be(5);
        Eval(Fn("length", new List<object?> { 1, 2, 3 })).Should().Be(3);
        Eval(Fn("length", new Dictionary<string, object?> { ["a"] = 1 })).Should().Be(1);
        Eval(Fn("length", 12345)).Should().Be(5);
        Eval(Fn("length", 1.5)).Should().Be(3);
    }

    // ---------------------------------------------------------------- Functions: aggregates

    [Fact]
    public void Function_Aggregates_FlattenListsAndSkipNonNumbers()
    {
        var values = new List<object?> { 1, "2", null, "x", 3.5 };

        Eval(Fn("sum", values)).Should().Be(6.5);
        Eval(Fn("sum", 1, 2, 3)).Should().Be(6.0);
        Eval(Fn("count", values)).Should().Be(3);
        Eval(Fn("min", values, 0.5)).Should().Be(0.5);
        Eval(Fn("max", values, "10")).Should().Be(10.0);
        Eval(Fn("avg", 1, 2, 2)).Should().Be(1.6667);
    }

    [Fact]
    public void Function_Aggregates_WithoutNumbers()
    {
        Eval(Fn("count", "x", null)).Should().Be(0);
        Eval(Fn("sum", "x")).Should().BeNull();
        Eval(Fn("avg", new List<object?>())).Should().BeNull();
        Eval(Fn("min")).Should().BeNull();
        Eval(Fn("max", (object?)null)).Should().BeNull();
    }

    // ---------------------------------------------------------------- Functions: dates

    [Fact]
    public void Function_DateDiffDays()
    {
        Eval(Fn("date_diff_days", "2024-03-01", "2024-02-01")).Should().Be(29);
        Eval(Fn("date_diff_days", "2024-02-01", "2024-03-01")).Should().Be(-29);
        Eval(Fn("date_diff_days", "2024-01-02T06:00:00Z", "2024-01-01T12:00:00Z")).Should().Be(0);
        Eval(Fn("date_diff_days", "not a date", "2024-01-01")).Should().BeNull();
        Eval(Fn("date_diff_days", "2024-01-01", null)).Should().BeNull();
    }

    [Fact]
    public void Function_DateDiffDays_FloorsNegativePartialDays()
    {
        // Python: (datetime(2024,1,1,0) - datetime(2024,1,1,12)).days == -1.
        Eval(Fn("date_diff_days", "2024-01-01T00:00:00", "2024-01-01T12:00:00")).Should().Be(-1);
    }

    [Fact]
    public void Function_DateDiffDays_AcceptsDateTimeDateOnlyAndOffsets()
    {
        var context = new Dictionary<string, object?>
        {
            ["visit"] = new DateOnly(2024, 1, 11),
            ["baseline"] = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            ["dosed"] = new DateTimeOffset(2024, 1, 6, 1, 0, 0, TimeSpan.FromHours(2)),
        };

        Eval(Node("function", "date_diff_days", V("visit"), V("baseline")), context).Should().Be(10);
        // 2024-01-06T01:00+02:00 is 2024-01-05T23:00Z: 4 whole days after baseline.
        Eval(Node("function", "date_diff_days", V("dosed"), V("baseline")), context).Should().Be(4);
    }

    [Theory]
    [InlineData("2024-01-15", 10, "2024-01-25")]
    [InlineData("2024-02-28", 1, "2024-02-29")]
    [InlineData("2024-03-01", -1, "2024-02-29")]
    [InlineData("2024-01-15T22:00:00Z", 0.25, "2024-01-16")]
    [InlineData("20240115", "7", "2024-01-22")]
    public void Function_DateAddDays_ReturnsIsoDate(object date, object days, string expected)
    {
        Eval(Fn("date_add_days", date, days)).Should().Be(expected);
    }

    [Fact]
    public void Function_DateAddDays_InvalidInput_ReturnsNull()
    {
        Eval(Fn("date_add_days", "01/15/2024", 1)).Should().BeNull();
        Eval(Fn("date_add_days", "2024-01-15", "x")).Should().BeNull();
    }

    [Theory]
    [InlineData("2025-06-15T12:00:01Z", true, false)]
    [InlineData("2025-06-15T11:59:59Z", false, true)]
    [InlineData("2025-06-15T12:00:00Z", false, false)]
    [InlineData("2025-06-15T14:00:00+02:00", false, false)]
    [InlineData("2030-01-01", true, false)]
    [InlineData("", false, false)]
    [InlineData("garbage", false, false)]
    public void Function_IsFutureAndIsPast_UseAsOf(string date, bool future, bool past)
    {
        Eval(Fn("is_future", date)).Should().Be(future);
        Eval(Fn("is_past", date)).Should().Be(past);
    }

    [Fact]
    public void Function_IsFuture_TreatsUnspecifiedAsOfAsUtc()
    {
        var evaluator = new RuleEvaluator(asOf: new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Unspecified));

        evaluator.AsOf.Kind.Should().Be(DateTimeKind.Utc);
        evaluator.Evaluate(Fn("is_future", "2025-06-15T12:30:00Z"), []).Should().Be(true);
    }

    [Theory]
    [InlineData("1990-06-15", 35)]
    [InlineData("1990-06-16", 34)]
    [InlineData("1990-12-31", 34)]
    [InlineData("2030-01-01", 0)]
    public void Function_AgeYears_AgainstAsOf(string birthDate, int expected)
    {
        Eval(Fn("age_years", birthDate)).Should().Be(expected);
    }

    [Fact]
    public void Function_AgeYears_WithReferenceDate()
    {
        Eval(Fn("age_years", "2000-02-29", "2021-02-28")).Should().Be(20);
        Eval(Fn("age_years", "2000-02-29", "2021-03-01")).Should().Be(21);
        // An unparseable reference falls back to AsOf.
        Eval(Fn("age_years", "2000-01-01", "unknown")).Should().Be(25);
        Eval(Fn("age_years", (object?)null)).Should().BeNull();
    }

    // ---------------------------------------------------------------- Functions: repeating groups

    [Fact]
    public void Function_IndexedRepeat_ReadsOneBasedEntry()
    {
        var context = new Dictionary<string, object?>
        {
            ["con_meds"] = new List<Dictionary<string, object?>>
            {
                new() { ["drug"] = "Aspirin" },
                new() { ["drug"] = "Ibuprofen" },
            },
        };

        Eval(Fn("indexed_repeat", "drug", "con_meds", 2), context).Should().Be("Ibuprofen");
        Eval(Fn("indexed_repeat", "drug", "con_meds", "1"), context).Should().Be("Aspirin");
        // Python's `or 1` makes a zero or missing index select the first entry.
        Eval(Fn("indexed_repeat", "drug", "con_meds", 0), context).Should().Be("Aspirin");
        Eval(Fn("indexed_repeat", "drug", "con_meds", null), context).Should().Be("Aspirin");
        Eval(Fn("indexed_repeat", "drug", "con_meds", 3), context).Should().BeNull();
        Eval(Fn("indexed_repeat", "dose", "con_meds", 1), context).Should().BeNull();
        Eval(Fn("indexed_repeat", "drug", "missing_group", 1), context).Should().BeNull();
    }

    [Fact]
    public void Function_Unsupported_Throws()
    {
        var act = () => Eval(Fn("soundex", "a"));

        act.Should().Throw<EvaluationException>().WithMessage("Unsupported function: 'soundex'");
    }

    // ---------------------------------------------------------------- ParseJsonLogic

    [Theory]
    [InlineData(5)]
    [InlineData("text")]
    [InlineData(true)]
    [InlineData(null)]
    public void ParseJsonLogic_Primitive_BecomesConstant(object? value)
    {
        var node = RuleEvaluator.ParseJsonLogic(value);

        node.Type.Should().Be("constant");
        node.Value.Should().Be(value);
    }

    [Fact]
    public void ParseJsonLogic_MultiKeyDictionary_IsLiteralConstant()
    {
        var literal = new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 };

        var node = RuleEvaluator.ParseJsonLogic(literal);

        node.Type.Should().Be("constant");
        node.Value.Should().BeSameAs(literal);
    }

    [Fact]
    public void ParseJsonLogic_Var_BecomesFieldReference()
    {
        var fromString = RuleEvaluator.ParseJsonLogic(new Dictionary<string, object?> { ["var"] = "vitals.sbp" });
        var fromList = RuleEvaluator.ParseJsonLogic(new Dictionary<string, object?> { ["var"] = new List<object?> { "age", 0 } });
        var empty = RuleEvaluator.ParseJsonLogic(new Dictionary<string, object?> { ["var"] = new List<object?>() });
        var numeric = RuleEvaluator.ParseJsonLogic(new Dictionary<string, object?> { ["var"] = 1 });

        fromString.Type.Should().Be("field_ref");
        fromString.FieldRef!.FieldId.Should().Be("vitals.sbp");
        fromList.Type.Should().Be("field_ref");
        fromList.FieldRef!.FieldId.Should().Be("age");
        empty.Type.Should().Be("constant");
        empty.Value.Should().BeNull();
        numeric.Type.Should().Be("constant");
        numeric.Value.Should().BeNull();
    }

    [Theory]
    [InlineData("==", "comparison", "==")]
    [InlineData("===", "comparison", "==")]
    [InlineData("!=", "comparison", "!=")]
    [InlineData("!==", "comparison", "!=")]
    [InlineData("<", "comparison", "<")]
    [InlineData("<=", "comparison", "<=")]
    [InlineData(">", "comparison", ">")]
    [InlineData(">=", "comparison", ">=")]
    [InlineData("in", "comparison", "in")]
    [InlineData("and", "logical", "and")]
    [InlineData("or", "logical", "or")]
    [InlineData("!", "logical", "not")]
    [InlineData("not", "logical", "not")]
    [InlineData("?:", "logical", "if_then_else")]
    [InlineData("if", "logical", "if_then_else")]
    [InlineData("+", "arithmetic", "+")]
    [InlineData("-", "arithmetic", "-")]
    [InlineData("*", "arithmetic", "*")]
    [InlineData("/", "arithmetic", "/")]
    [InlineData("%", "arithmetic", "%")]
    [InlineData("min", "arithmetic", "min")]
    [InlineData("max", "arithmetic", "max")]
    [InlineData("date_diff_days", "function", "date_diff_days")]
    public void ParseJsonLogic_MapsOperators(string jsonOp, string expectedType, string expectedOp)
    {
        var node = RuleEvaluator.ParseJsonLogic(new Dictionary<string, object?> { [jsonOp] = new List<object?> { 1, 2 } });

        node.Type.Should().Be(expectedType);
        node.Operator.Should().Be(expectedOp);
        node.Operands.Should().HaveCount(2);
        node.Operands!.Select(o => o.Value).Should().Equal(1, 2);
    }

    [Fact]
    public void ParseJsonLogic_SingleArgument_IsWrappedAsOneOperand()
    {
        var node = RuleEvaluator.ParseJsonLogic(new Dictionary<string, object?>
        {
            ["!"] = new Dictionary<string, object?> { ["var"] = "consent" },
        });

        node.Type.Should().Be("logical");
        node.Operator.Should().Be("not");
        node.Operands.Should().ContainSingle().Which.Type.Should().Be("field_ref");
    }

    [Fact]
    public void ParseJsonLogic_ArrayArguments_AreAccepted()
    {
        var node = RuleEvaluator.ParseJsonLogic(new Dictionary<string, object?> { ["+"] = new object?[] { 1, 2 } });

        node.Operands.Should().HaveCount(2);
        Eval(node).Should().Be(3L);
    }

    [Fact]
    public void ParseJsonLogic_EndToEnd_EvaluatesEligibilityRule()
    {
        var logic = new Dictionary<string, object?>
        {
            ["and"] = new List<object?>
            {
                new Dictionary<string, object?> { [">="] = new List<object?> { new Dictionary<string, object?> { ["var"] = "age" }, 18 } },
                new Dictionary<string, object?> { ["==="] = new List<object?> { new Dictionary<string, object?> { ["var"] = "consent" }, true } },
            },
        };
        var node = RuleEvaluator.ParseJsonLogic(logic);

        Eval(node, new Dictionary<string, object?> { ["age"] = 30, ["consent"] = "yes" }).Should().Be(true);
        Eval(node, new Dictionary<string, object?> { ["age"] = 17, ["consent"] = true }).Should().Be(false);
        Eval(node, new Dictionary<string, object?> { ["subject"] = new Dictionary<string, object?> { ["age"] = 40, ["consent"] = true } })
            .Should().Be(true);
    }

    [Fact]
    public void ParseJsonLogic_EndToEnd_IfSelectsBranch()
    {
        var logic = new Dictionary<string, object?>
        {
            ["if"] = new List<object?>
            {
                new Dictionary<string, object?> { [">"] = new List<object?> { new Dictionary<string, object?> { ["var"] = "sbp" }, 140 } },
                "HIGH",
                "NORMAL",
            },
        };
        var node = RuleEvaluator.ParseJsonLogic(logic);

        Eval(node, new Dictionary<string, object?> { ["sbp"] = 150 }).Should().Be("HIGH");
        Eval(node, new Dictionary<string, object?> { ["sbp"] = "120" }).Should().Be("NORMAL");
    }

    [Fact]
    public void ParseJsonLogic_EndToEnd_StrictModeRejectsMissingVar()
    {
        var node = RuleEvaluator.ParseJsonLogic(new Dictionary<string, object?>
        {
            ["=="] = new List<object?> { new Dictionary<string, object?> { ["var"] = "missing" }, 1 },
        });

        var act = () => Eval(node, strict: true);

        act.Should().Throw<UndefinedVariableException>();
    }
}
