using System.Collections.Generic;
using System.Linq;

using Cadence.Ddf;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The compiler's built-in structural WCAG check of an OpenRosa form, ported from the fail-closed accessibility
/// stage of the Python DDF compiler (<c>packages/ddf/stages/accessibility.py</c>).
/// </summary>
public class XFormsAccessibilityValidatorTests
{
    private static readonly XFormsAccessibilityValidator Validator = XFormsAccessibilityValidator.Instance;

    [Fact]
    public void Validate_PassesALabelledBoundForm()
    {
        var report = Validator.Validate(Form(
            binds: ["age", "sex"],
            body: """
                <input ref="/data/age"><label>Age (years)</label></input>
                <select1 ref="/data/sex"><label>Sex</label>
                  <item><label>Female</label><value>F</value></item>
                  <item><label>Male</label><value>M</value></item>
                </select1>
                """));

        report.Status.Should().Be(AccessibilityStatus.Pass);
        AccessibilityStatus.Passes(report).Should().BeTrue();
        Ids(report.Passes).Should().Equal("document-title", "label", "select-option-name");
        Ids(report.Inapplicable).Should().Equal("label-language");
    }

    [Theory]
    [InlineData("""<input ref="/data/age"/>""")]
    [InlineData("""<input ref="/data/age"><label>  </label></input>""")]
    [InlineData("""<input ref="/data/age"><hint>Years</hint></input>""")]
    public void Validate_ReportsAControlWithoutALabel(string control)
    {
        var report = Validator.Validate(Form(binds: ["age"], body: control));

        report.Status.Should().Be(AccessibilityStatus.Fail);
        var violation = report.Violations.Should().ContainSingle().Subject;
        violation["id"].Should().Be("label");
        Targets(violation).Should().Equal("/data/age");
    }

    [Fact]
    public void Validate_AcceptsAnItextLabelReference()
    {
        var report = Validator.Validate(Form(binds: ["age"], body: """<input ref="/data/age"><label ref="jr:itext('/data/age:label')"/></input>"""));

        report.Status.Should().Be(AccessibilityStatus.Pass);
    }

    [Fact]
    public void Validate_ReportsAControlMissingALanguageTheFormUses()
    {
        var report = Validator.Validate(Form(
            binds: ["age", "weight"],
            body: """
                <input ref="/data/age"><label xml:lang="en">Age</label><label xml:lang="fr">Âge</label></input>
                <input ref="/data/weight"><label xml:lang="en">Weight</label></input>
                """));

        var violation = report.Violations.Should().ContainSingle().Subject;
        violation["id"].Should().Be("label-language");
        Targets(violation).Should().Equal("/data/weight (no label in fr)");
    }

    [Fact]
    public void Validate_ReportsAnUnlabelledChoice()
    {
        var report = Validator.Validate(Form(
            binds: ["sex"],
            body: """<select1 ref="/data/sex"><label>Sex</label><item><value>F</value></item></select1>"""));

        var violation = report.Violations.Should().ContainSingle().Subject;
        violation["id"].Should().Be("select-option-name");
        Targets(violation).Should().Equal("/data/sex choice F");
    }

    [Fact]
    public void Validate_ReportsAnEmptyTitle()
    {
        var report = Validator.Validate(Form(binds: ["age"], body: """<input ref="/data/age"><label>Age</label></input>""", title: " "));

        report.Violations.Should().ContainSingle().Which["id"].Should().Be("document-title");
    }

    [Fact]
    public void Validate_ReportsLayoutErrors_ForAMissingRefAMissingBindAndADuplicateRef()
    {
        var report = Validator.Validate(Form(
            binds: ["age"],
            body: """
                <input><label>Orphan</label></input>
                <input ref="/data/age"><label>Age</label></input>
                <input ref="/data/age"><label>Age again</label></input>
                <input ref="/data/weight"><label>Weight</label></input>
                """));

        report.Status.Should().Be(AccessibilityStatus.Fail);
        report.Violations.Should().BeEmpty();
        report.LayoutErrors.Should().HaveCount(3);
        report.LayoutErrors![0].Should().StartWith("A input control has no ref");
        report.LayoutErrors.Skip(1).Should().Equal("More than one control binds /data/age.", "The control /data/weight has no bind.");
    }

    [Theory]
    [InlineData("<h:html xmlns:h=\"http://www.w3.org/1999/xhtml\"><h:head>")]
    [InlineData("<form/>")]
    [InlineData("<!DOCTYPE h:html [<!ENTITY x \"y\">]><h:html xmlns:h=\"http://www.w3.org/1999/xhtml\"><h:body/></h:html>")]
    public void Validate_FailsAFormThatIsNotAWellFormedXForm(string xml)
    {
        var report = Validator.Validate(xml);

        report.Status.Should().Be(AccessibilityStatus.Fail);
        report.LayoutErrors.Should().ContainSingle();
        AccessibilityStatus.Passes(report).Should().BeFalse();
    }

    [Fact]
    public void Exception_NamesEachProblem()
    {
        var report = Validator.Validate(Form(binds: [], body: """<input ref="/data/age"/>"""));

        var exception = new AccessibilityValidationException(report);

        exception.Report.Should().BeSameAs(report);
        exception.Message.Should().Contain("The control /data/age has no bind.").And.Contain("Accessibility violation label").And.Contain("(/data/age)");
    }

    private static string Form(IEnumerable<string> binds, string body, string title = "Vitals") => $"""
        <?xml version="1.0"?>
        <h:html xmlns="http://www.w3.org/2002/xforms" xmlns:h="http://www.w3.org/1999/xhtml" xmlns:jr="http://openrosa.org/javarosa">
          <h:head>
            <h:title>{title}</h:title>
            <model>
              <instance><data id="S-1"/></instance>
              {string.Concat(binds.Select(b => $"<bind nodeset=\"/data/{b}\" type=\"string\"/>"))}
            </model>
          </h:head>
          <h:body>{body}</h:body>
        </h:html>
        """.Trim();

    private static IEnumerable<object?> Ids(IReadOnlyList<IReadOnlyDictionary<string, object?>>? rules) => rules!.Select(r => r["id"]);

    private static IEnumerable<object?> Targets(IReadOnlyDictionary<string, object?> violation) =>
        ((IEnumerable<IReadOnlyDictionary<string, object?>>)violation["nodes"]!).Select(n => n["target"]);
}
