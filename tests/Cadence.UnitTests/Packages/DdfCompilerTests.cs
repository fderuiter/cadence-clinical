using System.Collections.Generic;
using System.Linq;

using Cadence.Ddf;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class DdfCompilerTests
{
    private const string SampleUsdmJson = """
    {
      "study": {
        "id": "STUDY-USDM-701",
        "name": "Cardiovascular Outcomes Study",
        "version": "2.1"
      },
      "items": [
        {
          "id": "SYS_BP",
          "name": "Systolic Blood Pressure",
          "type": "integer",
          "label": "Sitting Systolic Blood Pressure (mmHg)",
          "required": true
        },
        {
          "id": "DIAS_BP",
          "name": "Diastolic Blood Pressure",
          "type": "integer",
          "label": "Sitting Diastolic Blood Pressure (mmHg)",
          "required": true
        },
        {
          "id": "SMOKER",
          "name": "Current Smoker",
          "type": "text",
          "label": "Does the subject currently smoke tobacco?",
          "required": false
        }
      ]
    }
    """;

    /// <summary>
    /// Validates that UsdmCompiler compiles USDM JSON into DDF Intermediate Representation, ODM XML, and OpenRosa XForms.
    /// </summary>
    [Fact(DisplayName = "UsdmCompiler_CompileUsdm_GeneratesCompleteArtifacts")]
    [Trait("Category", "GxP_OQ")]
    public void UsdmCompilerCompileUsdmGeneratesCompleteArtifacts()
    {
        // Act
        var result = UsdmCompiler.CompileUsdm(SampleUsdmJson);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("COMPLETED");
        result.StudyId.Should().Be("STUDY-USDM-701");
        result.Name.Should().Be("Cardiovascular Outcomes Study");

        // Validate ODM XML
        result.OdmXml.Should().Contain("<ODM xmlns=\"http://www.cdisc.org/ns/odm/v1.3\"");
        result.OdmXml.Should().Contain("<Study OID=\"STUDY-USDM-701\">");
        result.OdmXml.Should().Contain("<ItemDef OID=\"SYS_BP\" Name=\"Systolic_Blood_Pressure\" DataType=\"integer\">");
        result.OdmXml.Should().Contain("<TranslatedText xml:lang=\"en\">Sitting Systolic Blood Pressure (mmHg)</TranslatedText>");
        result.OdmXml.Should().Contain("<ItemDef OID=\"DIAS_BP\" Name=\"Diastolic_Blood_Pressure\" DataType=\"integer\">");

        // Validate OpenRosa XForms
        result.OpenRosaXml.Should().Contain("<h:html xmlns=\"http://www.w3.org/2002/xforms\"");
        result.OpenRosaXml.Should().Contain("<h:title>Cardiovascular Outcomes Study</h:title>");
        result.OpenRosaXml.Should().Contain("<model>");
        result.OpenRosaXml.Should().Contain("<Systolic_Blood_Pressure />");
        result.OpenRosaXml.Should().Contain("<Diastolic_Blood_Pressure />");
        result.OpenRosaXml.Should().Contain("<input ref=\"/data/Systolic_Blood_Pressure\">");
        result.OpenRosaXml.Should().Contain("<label xml:lang=\"en\">Sitting Systolic Blood Pressure (mmHg)</label>");

        // Validate Form Hash
        result.FormHash.Should().NotBeNullOrWhiteSpace();
        result.FormHash.Length.Should().Be(64); // SHA-256 hex string

        // Deterministic form hash verification
        var secondRun = UsdmCompiler.CompileUsdm(SampleUsdmJson);
        secondRun.FormHash.Should().Be(result.FormHash);
    }

    /// <summary>
    /// The compiler runs the built-in accessibility check on the rendered form and returns its real report.
    /// </summary>
    [Fact]
    public void CompileUsdm_ChecksTheRenderedFormAndReturnsTheReport()
    {
        var result = UsdmCompiler.CompileUsdm(SampleUsdmJson);

        result.AccessibilityReport.Should().NotBeNull();
        result.AccessibilityReport!.Status.Should().Be(AccessibilityStatus.Pass);
        result.AccessibilityReport.FormHash.Should().Be(result.FormHash);
        result.AccessibilityReport.Violations.Should().BeEmpty();
        result.AccessibilityReport.LayoutErrors.Should().BeEmpty();
        result.AccessibilityReport.Passes!.Select(p => p["id"]).Should().Contain(["document-title", "label"]);
    }

    /// <summary>
    /// A form the check fails is never returned: a study whose name is blank renders a form without a title.
    /// </summary>
    [Fact]
    public void CompileUsdm_RefusesAFormThatFailsTheCheck()
    {
        const string usdm = """{"study":{"id":"S-1","name":"  "},"items":[{"id":"BP","name":"Systolic","type":"integer"}]}""";

        var compile = () => UsdmCompiler.CompileUsdm(usdm);

        compile.Should().Throw<AccessibilityValidationException>()
            .Which.Report.Violations.Should().ContainSingle().Which["id"].Should().Be("document-title");
    }

    /// <summary>
    /// A supplied validator replaces the built-in one, and its violations stop the compile.
    /// </summary>
    [Fact]
    public void CompileUsdm_UsesTheSuppliedValidator_AndStopsOnItsViolations()
    {
        var validator = new FixedValidator(new AccessibilityReport(
            Status: AccessibilityStatus.Fail,
            Violations: [new Dictionary<string, object?> { ["id"] = "color-contrast", ["description"] = "Contrast is too low." }]));

        var compile = () => UsdmCompiler.CompileUsdm(SampleUsdmJson, accessibilityValidator: validator);

        compile.Should().Throw<AccessibilityValidationException>().WithMessage("*color-contrast*Contrast is too low.*");
        validator.Calls.Should().Be(1);
    }

    /// <summary>
    /// A report that says PASS while it lists a violation or a layout error does not let the form through.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CompileUsdm_DoesNotTrustAPassStatusThatListsProblems(bool violation, bool layoutError)
    {
        var validator = new FixedValidator(new AccessibilityReport(
            Status: AccessibilityStatus.Pass,
            Violations: violation ? [new Dictionary<string, object?> { ["id"] = "label" }] : [],
            LayoutErrors: layoutError ? ["The control /data/x has no bind."] : []));

        var compile = () => UsdmCompiler.CompileUsdm(SampleUsdmJson, accessibilityValidator: validator);

        compile.Should().Throw<AccessibilityValidationException>();
    }

    /// <summary>
    /// With the check turned off the validator is not called and the result carries no report.
    /// </summary>
    [Fact]
    public void CompileUsdm_WithTheCheckOff_ReturnsNoReport()
    {
        var validator = new FixedValidator(new AccessibilityReport(Status: AccessibilityStatus.Fail));

        var result = UsdmCompiler.CompileUsdm(SampleUsdmJson, options: new CompileOptions(ValidateAccessibility: false), accessibilityValidator: validator);

        result.AccessibilityReport.Should().BeNull();
        validator.Calls.Should().Be(0);
    }

    private sealed class FixedValidator(AccessibilityReport report) : IAccessibilityValidator
    {
        public int Calls { get; private set; }

        public AccessibilityReport Validate(string openRosaXml)
        {
            Calls++;
            return report;
        }
    }
}
