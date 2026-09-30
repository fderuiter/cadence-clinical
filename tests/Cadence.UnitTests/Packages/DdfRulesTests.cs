using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.XPath;

using Cadence.Ddf;
using Cadence.Rules.Expressions;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Skip logic and constraints of DDF form items, written in the one rule language (K-RULE-3) and compiled to OpenRosa
/// XPath. Ported from the Python reference DDF rule compiler and IR builder (<c>packages/ddf/rules/</c>,
/// <c>packages/ddf/stages/ir_builder.py</c>), with its separate rule AST replaced by <c>Cadence.Rules</c>.
/// </summary>
public class DdfRulesTests
{
    private static readonly Dictionary<string, string> Paths = new() { ["AGE"] = "/data/Age", ["SEX"] = "/data/Sex", ["NOTE"] = "/data/Note" };

    [Theory]
    [InlineData("""{"op":"and","args":[{"op":">=","args":[{"var":"AGE"},18]},{"op":"==","args":[{"var":"SEX"},"F"]}]}""", "((/data/Age >= 18) and (/data/Sex = 'F'))")]
    [InlineData("""{"op":"or","args":[{"op":"<","args":[{"var":"AGE"},1.5]},{"op":"not","args":[true]}]}""", "((/data/Age < 1.5) or not(true()))")]
    [InlineData("""{"op":"!=","args":[{"op":"/","args":[{"var":"AGE"},{"op":"-","args":[{"var":"AGE"},2]}]},0]}""", "((/data/Age div (/data/Age - 2)) != 0)")]
    [InlineData("""{"op":"==","args":[{"var":"NOTE"},null]}""", "(string-length(/data/Note) = 0)")]
    [InlineData("""{"op":"!=","args":[null,{"var":"NOTE"}]}""", "(string-length(/data/Note) > 0)")]
    [InlineData("""{"op":"<","args":[{"var":"AGE"},null]}""", "false()")]
    [InlineData("""{"op":"is_empty","args":[{"var":"NOTE"}]}""", "(normalize-space(/data/Note) = '')")]
    [InlineData("""{"op":"is_not_empty","args":[{"var":"NOTE"}]}""", "(normalize-space(/data/Note) != '')")]
    [InlineData("""{"op":"in","args":[{"var":"SEX"},"F","M"]}""", "(/data/Sex = 'F' or /data/Sex = 'M')")]
    [InlineData("""{"op":"not_in","args":[{"var":"SEX"},"U"]}""", "not(/data/Sex = 'U')")]
    [InlineData("""{"op":">","args":[{"op":"length","args":[{"var":"NOTE"}]},200]}""", "(string-length(/data/Note) > 200)")]
    public void Compile_WritesOpenRosaXPath(string condition, string expected)
    {
        var (xpath, unresolved) = RuleXPathCompiler.Compile(Parse(condition), name => Paths.GetValueOrDefault(name));

        xpath.Should().Be(expected);
        unresolved.Should().BeEmpty();
        XPathExpression.Compile(xpath).Should().NotBeNull("the result is valid XPath 1.0");
    }

    [Fact]
    public void Compile_ReportsEachUnknownVariableOnce()
    {
        var (_, unresolved) = RuleXPathCompiler.Compile(
            Parse("""{"op":"and","args":[{"op":"==","args":[{"var":"X"},1]},{"op":"==","args":[{"var":"X"},2]},{"op":"==","args":[{"var":"AGE"},{"var":"Y"}]}]}"""),
            name => Paths.GetValueOrDefault(name));

        unresolved.Should().Equal("X", "Y");
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "\"it's\"")]
    [InlineData("say \"it's\"", "concat('say \"it', \"'\", 's\"')")]
    public void Literal_QuotesTextSoItCannotChangeTheExpression(string text, string expected)
    {
        string literal = RuleXPathCompiler.Literal(text);

        literal.Should().Be(expected);
        new XPathDocument(new System.IO.StringReader("<r/>")).CreateNavigator().Evaluate(literal).Should().Be(text);
    }

    [Theory]
    [InlineData("Systolic_BP", "Systolic_BP")]
    [InlineData("Systolic Blood Pressure", "Systolic_Blood_Pressure")]
    [InlineData("Systolic (mmHg)", "Systolic__28mmHg_29")]
    [InlineData("1st visit", "item_1st_visit")]
    [InlineData("xmlData", "item_xmlData")]
    [InlineData("Température", "Temp_e9rature")]
    [InlineData("  ", null)]
    public void Sanitize_MakesADeterministicXmlName(string raw, string? expected)
    {
        DdfIdentifier.Sanitize(raw).Should().Be(expected);
    }

    [Fact]
    public void FindCycles_ReportsEachLoopOnceFromItsSmallestId_AndSelfReferences()
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>
        {
            ["C"] = ["A"],
            ["A"] = ["B"],
            ["B"] = ["C", "D"],
            ["D"] = [],
            ["E"] = ["E"],
            ["F"] = ["D"],
        };

        DdfDependencyGraph.FindCycles(graph).Should().Equal("A -> B -> C -> A", "E -> E");
        DdfDependencyGraph.FindCycles(new Dictionary<string, IReadOnlyList<string>> { ["F"] = ["D"] }).Should().BeEmpty();
    }

    [Fact]
    public void CompileUsdm_PutsSkipLogicAndConstraintsOnTheForm()
    {
        const string usdm = """
            {"study":{"id":"S-1","name":"Rules"},"items":[
              {"id":"PREG","name":"Pregnant","type":"string"},
              {"id":"SEX","name":"Sex","type":"string"},
              {"id":"WEEKS","name":"Weeks pregnant","type":"integer","rules":[
                {"type":"skip_logic","condition":{"op":"==","args":[{"var":"SEX"},"F"]}},
                {"type":"skip_logic","action":"hide","condition":{"op":"==","args":[{"var":"PREG"},"N"]}},
                {"type":"constraint","condition":{"op":"<=","args":[{"var":"WEEKS"},45]},"query_message":"At most 45 weeks."}
              ]}
            ]}
            """;

        var result = UsdmCompiler.CompileUsdm(usdm);

        result.OpenRosaXml.Should().Contain(
            """<bind nodeset="/data/Weeks_pregnant" type="integer" relevant="(/data/Sex = &#x27;F&#x27;) and not((/data/Pregnant = &#x27;N&#x27;))" constraint="(/data/Weeks_pregnant &lt;= 45)" jr:constraintMsg="At most 45 weeks."/>""");
    }

    [Fact]
    public void CompileUsdm_RefusesEveryRuleProblemAtOnce()
    {
        const string usdm = """
            {"study":{"id":"S-1","name":"Rules"},"items":[
              {"id":"A","name":"A","rules":[{"type":"skip_logic","condition":{"op":"==","args":[{"var":"B"},1]}}]},
              {"id":"B","name":"B","rules":[{"type":"skip_logic","condition":{"op":"==","args":[{"var":"A"},1]}}]},
              {"id":"C","name":"C","rules":[
                {"type":"skip_logic","condition":{"op":"==","args":[{"var":"GHOST"},1]}},
                {"type":"constraint","condition":{"op":"bogus","args":[]},"query_message":"x"},
                {"type":"constraint","condition":true},
                {"type":"skip_logic","action":"toggle","condition":true},
                {"type":"cross_form_check","condition":true},
                {"type":"skip_logic","condition":true,"extra":1}
              ]},
              {"id":"C","name":"C2"},
              {"id":"D","name":"C"},
              {"id":"E","name":"E","constraint":". > 0"}
            ]}
            """;

        var compile = () => UsdmCompiler.CompileUsdm(usdm);

        var problems = compile.Should().Throw<DdfCompilationException>().Which.Problems;
        problems.Should().Contain("Item 'C' rule 0: the condition reads 'GHOST', which is not an item of this study.");
        problems.Should().Contain(p => p.StartsWith("Item 'C' rule 1: the condition does not parse"));
        problems.Should().Contain("Item 'C' rule 2: a constraint needs a 'query_message'.");
        problems.Should().Contain("Item 'C' rule 3: 'action' must be 'show' or 'hide'.");
        problems.Should().Contain("Item 'C' rule 4: 'type' must be 'skip_logic' or 'constraint'.");
        problems.Should().Contain("Item 'C' rule 5: unknown property 'extra'.");
        problems.Should().Contain("Item id 'C' is used more than once.");
        problems.Should().Contain("Items 'C' and 'D' both become the form element 'C'.");
        problems.Should().Contain("Item 'E': 'constraint' is no longer accepted; write a rule of type 'constraint' in 'rules'.");
        problems.Should().Contain("Skip logic makes a cycle: A -> B -> A.");
    }

    [Fact]
    public void CompileUsdm_NamesAnItemWithoutAnIdAfterItsElement_WithAWarning_SoTheFormHashIsStable()
    {
        const string usdm = """{"study":{"id":"S-1","name":"Ids"},"items":[{"name":"Heart rate","type":"integer"}]}""";

        var first = UsdmCompiler.CompileUsdm(usdm);
        var second = UsdmCompiler.CompileUsdm(usdm);

        second.FormHash.Should().Be(first.FormHash);
        first.Warnings.Should().ContainSingle().Which["generated_value"].Should().Be("Heart_rate");
    }

    [Fact]
    public void CompileUsdm_TurnsAColumnWidthIntoAnEnketoAppearance()
    {
        const string usdm = """
            {"study":{"id":"S-1","name":"Layout"},"items":[
              {"id":"A","name":"A","cols":2},
              {"id":"B","name":"B","layout":{"span":"3"}},
              {"id":"C","name":"C","grid":{"column_span":4}},
              {"id":"D","name":"D","cols":7}
            ]}
            """;

        var result = UsdmCompiler.CompileUsdm(usdm);

        result.OpenRosaXml.Should().Contain("""<input ref="/data/A" appearance="w2">""")
            .And.Contain("""<input ref="/data/B" appearance="w3">""")
            .And.Contain("""<input ref="/data/C" appearance="w4">""")
            .And.Contain("""<input ref="/data/D">""");
    }

    [Fact]
    public void CompileUsdm_WritesTheItemTextInEachLanguage()
    {
        const string usdm = """
            {"study":{"id":"S-1","name":"Text"},"items":[
              {"id":"AGE","name":"Age","label":"Age at consent","hint":"Whole years","description":"Age in completed years",
               "localizations":{"fr":{"label":"Âge au consentement","hint":"Années révolues"}}},
              {"id":"SEX","name":"Sex","localizations":{"fr":{"label":"Sexe"}}}
            ]}
            """;

        var result = UsdmCompiler.CompileUsdm(usdm);

        result.OpenRosaXml.Should().Contain("""<label xml:lang="en">Age at consent</label>""")
            .And.Contain("""<label xml:lang="fr">Âge au consentement</label>""")
            .And.Contain("""<hint xml:lang="en">Whole years</hint>""")
            .And.Contain("""<hint xml:lang="fr">Années révolues</hint>""")
            .And.Contain("""<label xml:lang="en">Sex</label>""")
            .And.Contain("""<label xml:lang="fr">Sexe</label>""");
        result.OdmXml.Should().Contain("""<TranslatedText xml:lang="en">Age in completed years</TranslatedText>""")
            .And.Contain("""<TranslatedText xml:lang="fr">Âge au consentement</TranslatedText>""");
        result.AccessibilityReport!.Passes!.Select(p => p["id"]).Should().Contain("label-language");
    }

    [Fact]
    public void CompileUsdm_RefusesAFormWithAnItemMissingALanguageOthersHave()
    {
        const string usdm = """
            {"study":{"id":"S-1","name":"Text"},"items":[
              {"id":"AGE","name":"Age","localizations":{"fr":{"label":"Âge"}}},
              {"id":"SEX","name":"Sex"}
            ]}
            """;

        var compile = () => UsdmCompiler.CompileUsdm(usdm);

        compile.Should().Throw<AccessibilityValidationException>()
            .Which.Report.Violations.Should().ContainSingle().Which["id"].Should().Be("label-language");
    }

    [Theory]
    [InlineData("""{"en_US":{"label":"x"}}""", "Item 'A' localization 'en_US': not a BCP 47 language tag.")]
    [InlineData("""{"fr":{"hint":"x"}}""", "Item 'A' localization 'fr': a 'label' is required.")]
    [InlineData("""{"fr":{"label":"x","tooltip":"y"}}""", "Item 'A' localization 'fr': unknown property 'tooltip'.")]
    [InlineData("""["fr"]""", "Item 'A': 'localizations' must be an object keyed by language.")]
    public void CompileUsdm_RefusesAMalformedLocalization(string localizations, string problem)
    {
        string usdm = $$"""{"study":{"id":"S-1","name":"Text"},"items":[{"id":"A","name":"A","localizations":{{localizations}}}]}""";

        var compile = () => UsdmCompiler.CompileUsdm(usdm);

        compile.Should().Throw<DdfCompilationException>().Which.Problems.Should().Contain(problem);
    }

    [Fact]
    public void CompileUsdm_ReportsAFilledInStudyIdAndName()
    {
        var result = UsdmCompiler.CompileUsdm("""{"items":[{"id":"A","name":"A"}]}""", fallbackStudyId: "S-FALLBACK");

        result.StudyId.Should().Be("S-FALLBACK");
        result.Warnings!.Select(w => w["field"]).Should().Equal("study.id", "study.name");
    }

    private static RuleExpression Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RuleExpressionJson.Parse(document.RootElement);
    }
}
