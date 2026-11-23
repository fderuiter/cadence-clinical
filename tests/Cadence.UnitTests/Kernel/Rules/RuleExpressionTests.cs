using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

/// <summary>
/// The rule expression language (spec §10.1, K-RULE-3): parsing is strict, the canonical form round-trips, the
/// semantics are the documented Kleene and comparison rules.
/// </summary>
public class RuleExpressionTests
{
    private static readonly Dictionary<string, RuleValue> Values = new(StringComparer.Ordinal)
    {
        ["SYS"] = RuleValue.FromNumber(120m),
        ["DIA"] = RuleValue.FromNumber(80m),
        ["TEXTNUM"] = RuleValue.FromText("120"),
        ["NAME"] = RuleValue.FromText("SUPINE"),
        ["BLANK"] = RuleValue.FromText("  "),
        ["FLAG"] = RuleValue.True,
        ["NOTHING"] = RuleValue.Null,
    };

    private static readonly RuleRecord Record = new("X", "1", Values);

    [Theory(DisplayName = "Expression_CanonicalJson_RoundTrips")]
    [Trait("Category", "Unit")]
    [InlineData("""{"op":"and","args":[{"op":">","args":[{"var":"SYS"},140]},{"op":"not","args":[{"op":"is_empty","args":[{"var":"DIA"}]}]}]}""")]
    [InlineData("""{"op":"in","args":[{"var":"NAME"},"SITTING","STANDING","SUPINE"]}""")]
    [InlineData("""{"op":"/","args":[{"op":"+","args":[{"var":"SYS"},{"op":"*","args":[2,{"var":"DIA"}]}]},3]}""")]
    [InlineData("""{"op":"or","args":[true,false,null,"x",1.50]}""")]
    [InlineData("""{"op":">","args":[{"op":"length","args":[{"var":"NAME"}]},8]}""")]
    public void ExpressionCanonicalJsonRoundTrips(string json)
    {
        var expression = Parse(json);

        expression.ToString().Should().Be(json);
        RuleExpressionJson.Parse(RuleExpressionJson.ToJson(expression)).ToString().Should().Be(json);
    }

    [Theory(DisplayName = "Expression_EdcGrammarShapes_ParseToTheCanonicalForm")]
    [Trait("Category", "Unit")]
    [InlineData("""{"operator":"&&","operands":[{"var":"FLAG"},{"node_type":"LITERAL","value":true}]}""", """{"op":"and","args":[{"var":"FLAG"},true]}""")]
    [InlineData("""{"node_type":"FUNCTION","operator":"empty","children":[{"var":"BLANK"}]}""", """{"op":"is_empty","args":[{"var":"BLANK"}]}""")]
    [InlineData("""{"node_type":"OPERATOR","value":"||","args":[false,{"op":"!","args":[true]}]}""", """{"op":"or","args":[false,{"op":"not","args":[true]}]}""")]
    [InlineData("""{"type":"constant","value":42}""", "42")]
    public void ExpressionEdcGrammarShapesParseToTheCanonicalForm(string edc, string canonical) =>
        Parse(edc).ToString().Should().Be(canonical);

    [Theory(DisplayName = "Expression_InvalidJson_IsRejectedWithItsPath")]
    [Trait("Category", "Unit")]
    [InlineData("""{"op":"between","args":[1,2,3]}""", "unknown operator \"between\"")]
    [InlineData("""{"op":"==","args":[1]}""", "\"==\" takes 2 argument(s), not 1")]
    [InlineData("""{"op":"in","args":[1]}""", "\"in\" takes at least 2 argument(s)")]
    [InlineData("""{"node_type":"XPATH","value":"/clinical_data/SYS"}""", "node type \"XPATH\" is not supported")]
    [InlineData("""{"op":"indexed-repeat","args":[1,2,3]}""", "unknown operator \"indexed-repeat\"")]
    [InlineData("""{"var":"1BAD"}""", "is not a variable name")]
    [InlineData("""{"var":"SYS","extra":1}""", "unknown property \"extra\"")]
    [InlineData("""{"op":"not","args":[[true]]}""", "$.args[0]: an array is not an expression")]
    [InlineData("""{"op":"and"}""", "\"and\" takes at least 1 argument(s), not 0")]
    [InlineData("""{"op":"==","operator":"!=","args":[1,2]}""", "\"op\" is given twice")]
    [InlineData("""{"op":"+","args":[1e400,1]}""", "outside the decimal range")]
    public void ExpressionInvalidJsonIsRejectedWithItsPath(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);

        RuleExpressionJson.TryParse(document.RootElement, out var expression, out var error).Should().BeFalse();

        expression.Should().BeNull();
        error.Should().Contain(expected);
    }

    [Fact(DisplayName = "Expression_TooDeepOrTooLarge_IsRejected")]
    [Trait("Category", "Unit")]
    public void ExpressionTooDeepOrTooLargeIsRejected()
    {
        var deep = string.Concat(Enumerable.Repeat("""{"op":"not","args":[""", RuleExpressionJson.MaxDepth + 1)) + "true"
            + string.Concat(Enumerable.Repeat("]}", RuleExpressionJson.MaxDepth + 1));
        using var deepDocument = JsonDocument.Parse(deep, new JsonDocumentOptions { MaxDepth = 1000 });
        RuleExpressionJson.TryParse(deepDocument.RootElement, out _, out var deepError).Should().BeFalse();
        deepError.Should().Contain($"nested more than {RuleExpressionJson.MaxDepth} levels");

        var wide = """{"op":"or","args":[""" + string.Join(",", Enumerable.Repeat("true", RuleExpressionJson.MaxNodes)) + "]}";
        using var wideDocument = JsonDocument.Parse(wide);
        RuleExpressionJson.TryParse(wideDocument.RootElement, out _, out var wideError).Should().BeFalse();
        wideError.Should().Contain($"more than {RuleExpressionJson.MaxNodes} nodes");
    }

    [Theory(DisplayName = "Expression_KleeneLogic_FollowsTheTruthTable")]
    [Trait("Category", "Unit")]
    [InlineData("and", "true", "true", "true")]
    [InlineData("and", "true", "null", "null")]
    [InlineData("and", "false", "null", "false")]
    [InlineData("and", "null", "null", "null")]
    [InlineData("and", "true", "\"x\"", "null")]
    [InlineData("or", "false", "false", "false")]
    [InlineData("or", "false", "null", "null")]
    [InlineData("or", "true", "null", "true")]
    [InlineData("or", "null", "1", "null")]
    public void ExpressionKleeneLogicFollowsTheTruthTable(string op, string left, string right, string expected) =>
        Evaluate($$"""{"op":"{{op}}","args":[{{left}},{{right}}]}""").ToString().Should().Be(expected);

    [Theory(DisplayName = "Expression_Comparisons_AreNeverUnknown")]
    [Trait("Category", "Unit")]
    [InlineData("""{"op":"==","args":[null,null]}""", true)]
    [InlineData("""{"op":"!=","args":[null,null]}""", false)]
    [InlineData("""{"op":"==","args":[{"var":"NOTHING"},1]}""", false)]
    [InlineData("""{"op":"!=","args":[{"var":"MISSING"},1]}""", true)]
    [InlineData("""{"op":"<","args":[null,1]}""", false)]
    [InlineData("""{"op":">=","args":[1,null]}""", false)]
    [InlineData("""{"op":"==","args":[{"var":"TEXTNUM"},{"var":"SYS"}]}""", true)]
    [InlineData("""{"op":">","args":["10","9"]}""", true)]
    [InlineData("""{"op":"==","args":[1.50,1.5]}""", true)]
    [InlineData("""{"op":"<","args":["ABC","abc"]}""", true)]
    [InlineData("""{"op":"==","args":[true,{"var":"FLAG"}]}""", true)]
    [InlineData("""{"op":"<","args":[false,true]}""", false)]
    [InlineData("""{"op":"==","args":["SUPINE","supine"]}""", false)]
    public void ExpressionComparisonsAreNeverUnknown(string json, bool expected) =>
        Evaluate(json).Should().Be(RuleValue.FromBoolean(expected));

    [Theory(DisplayName = "Expression_ArithmeticAndFunctions_FollowTheDocumentedRules")]
    [Trait("Category", "Unit")]
    [InlineData("""{"op":"-","args":[{"var":"SYS"},{"var":"DIA"}]}""", "40")]
    [InlineData("""{"op":"/","args":[1,0]}""", "null")]
    [InlineData("""{"op":"*","args":[79228162514264337593543950335,2]}""", "null")]
    [InlineData("""{"op":"+","args":["abc",1]}""", "null")]
    [InlineData("""{"op":"+","args":[{"var":"TEXTNUM"},1]}""", "121")]
    [InlineData("""{"op":"is_empty","args":[{"var":"BLANK"}]}""", "true")]
    [InlineData("""{"op":"is_empty","args":[{"var":"MISSING"}]}""", "true")]
    [InlineData("""{"op":"is_not_empty","args":[{"var":"NAME"}]}""", "true")]
    [InlineData("""{"op":"is_empty","args":[0]}""", "false")]
    [InlineData("""{"op":"in","args":[{"var":"NAME"},"SITTING","SUPINE"]}""", "true")]
    [InlineData("""{"op":"not_in","args":[{"var":"NAME"},"SITTING"]}""", "true")]
    [InlineData("""{"op":"in","args":[null,"A",null]}""", "true")]
    [InlineData("""{"op":"length","args":[{"var":"NAME"}]}""", "6")]
    [InlineData("""{"op":"length","args":["Ω😀"]}""", "2")]
    [InlineData("""{"op":"length","args":[12]}""", "null")]
    [InlineData("""{"op":"not","args":[5]}""", "null")]
    public void ExpressionArithmeticAndFunctionsFollowTheDocumentedRules(string json, string expected) =>
        Evaluate(json).ToString().Should().Be(expected);

    [Fact(DisplayName = "Expression_Variables_ListsEachReadOnceInOrder")]
    [Trait("Category", "Unit")]
    public void ExpressionVariablesListsEachReadOnceInOrder() =>
        RuleExpressionJson.Variables(Parse("""{"op":"and","args":[{"op":"<","args":[{"var":"SYS"},{"var":"DIA"}]},{"op":"is_empty","args":[{"var":"SYS"}]},{"var":"FLAG"}]}"""))
            .Should().Equal("SYS", "DIA", "FLAG");

    [Fact(DisplayName = "Expression_Length_CountsRunesWithoutEnumeratorBoxing")]
    [Trait("Category", "Unit")]
    public void ExpressionLengthCountsRunesWithoutEnumeratorBoxing()
    {
        var record = new RuleRecord("X", "1", new Dictionary<string, RuleValue>(StringComparer.Ordinal)
        {
            ["UNICODE"] = RuleValue.FromText("Ω😀é"),
        });
        var cold = RuleExpressionCompiler.Compile(Parse("""{"op":"length","args":[{"var":"UNICODE"}]}"""));

        var beforeCold = GC.GetAllocatedBytesForCurrentThread();
        var coldResult = cold(record);
        var coldAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeCold;

        var beforeLoop = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            _ = cold(record);
        }

        var loopAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeLoop;

        coldResult.Should().Be(RuleValue.FromNumber(3m));
        coldAllocated.Should().Be(0);
        loopAllocated.Should().Be(0);
    }

    [Theory(DisplayName = "Expression_LiteralAndDynamicComparisons_PreserveSemanticsAcrossOperandShapes")]
    [Trait("Category", "Unit")]
    [InlineData("""{"op":"==","args":["SITTING",{"var":"TEXTNUM"}]}""", false)]
    [InlineData("""{"op":"!=","args":[{"var":"TEXTNUM"},"SITTING"]}""", true)]
    [InlineData("""{"op":"<","args":[{"var":"SYS"},"SITTING"]}""", true)]
    [InlineData("""{"op":">","args":["SITTING",{"var":"SYS"}]}""", true)]
    [InlineData("""{"op":"==","args":[{"var":"FLAG"},"true"]}""", true)]
    [InlineData("""{"op":"==","args":["true",{"var":"FLAG"}]}""", true)]
    [InlineData("""{"op":"==","args":[120,{"var":"TEXTNUM"}]}""", true)]
    [InlineData("""{"op":"<","args":["1.50",{"var":"NAME"}]}""", true)]
    [InlineData("""{"op":"==","args":[null,{"var":"NOTHING"}]}""", true)]
    [InlineData("""{"op":"<","args":[null,{"var":"SYS"}]}""", false)]
    [InlineData("""{"op":"in","args":[{"var":"TEXTNUM"},"SITTING","STANDING","SUPINE"]}""", false)]
    [InlineData("""{"op":"in","args":[{"var":"FLAG"},"false","true"]}""", true)]
    [InlineData("""{"op":"in","args":[{"var":"TEXTNUM"},"SITTING",120.0,"SUPINE"]}""", true)]
    [InlineData("""{"op":"not_in","args":[{"var":"SYS"},"SITTING","STANDING"]}""", true)]
    [InlineData("""{"op":"in","args":[{"var":"TEXTNUM"},{"var":"NAME"},{"var":"SYS"}]}""", true)]
    public void ExpressionLiteralAndDynamicComparisonsPreserveSemanticsAcrossOperandShapes(string json, bool expected) =>
        Evaluate(json).Should().Be(RuleValue.FromBoolean(expected));

    [Theory(DisplayName = "RuleValue_TryGetNumber_MatchesInvariantFloatParsingRules")]
    [Trait("Category", "Unit")]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("SITTING", false)]
    [InlineData("mm[Hg]", false)]
    [InlineData("2026-09-30", false)]
    [InlineData("120/80", false)]
    [InlineData("001-0042", false)]
    [InlineData("37.5C", false)]
    [InlineData(".", false)]
    [InlineData("+.", false)]
    [InlineData("1e", false)]
    [InlineData("1e+", false)]
    [InlineData("120", true)]
    [InlineData(" -42.5 ", true)]
    [InlineData("+.25", true)]
    [InlineData("5.", true)]
    [InlineData("1.5e2", true)]
    [InlineData("-3E-4", true)]
    [InlineData("120\0", true)]
    public void RuleValueTryGetNumberMatchesInvariantFloatParsingRules(string text, bool expected)
    {
        var value = RuleValue.FromText(text);

        value.TryGetNumber(out var parsed).Should().Be(expected);
        if (!expected)
        {
            RuleValue.MayBeInvariantDecimal(text).Should().BeFalse();
            parsed.Should().Be(0m);
        }
    }

    [Theory(DisplayName = "Expression_Evaluation_IsInvariantAcrossCultures")]
    [Trait("Category", "Unit")]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("ja-JP")]
    public void ExpressionEvaluationIsInvariantAcrossCultures(string cultureName)
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            Evaluate("""{"op":"==","args":["120.0",{"var":"SYS"}]}""").Should().Be(RuleValue.True);
            Evaluate("""{"op":">","args":["1.5e2",{"var":"SYS"}]}""").Should().Be(RuleValue.True);
            Evaluate("""{"op":"in","args":[{"var":"NAME"},"sitting","SITTING","SUPINE"]}""").Should().Be(RuleValue.True);
            Evaluate("""{"op":"in","args":["i","I"]}""").Should().Be(RuleValue.False);
            Evaluate("""{"op":"==","args":[{"op":"length","args":["Ω😀"]},2]}""").Should().Be(RuleValue.True);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    private static RuleExpression Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RuleExpressionJson.Parse(document.RootElement);
    }

    private static RuleValue Evaluate(string json) => RuleExpressionCompiler.Evaluate(Parse(json), Record);
}
