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
        result.OdmXml.Should().Contain("<ItemDef OID=\"SYS_BP\" Name=\"Systolic_Blood_Pressure\" DataType=\"integer\" />");
        result.OdmXml.Should().Contain("<ItemDef OID=\"DIAS_BP\" Name=\"Diastolic_Blood_Pressure\" DataType=\"integer\" />");

        // Validate OpenRosa XForms
        result.OpenRosaXml.Should().Contain("<h:html xmlns=\"http://www.w3.org/2002/xforms\"");
        result.OpenRosaXml.Should().Contain("<h:title>Cardiovascular Outcomes Study</h:title>");
        result.OpenRosaXml.Should().Contain("<model>");
        result.OpenRosaXml.Should().Contain("<Systolic_Blood_Pressure />");
        result.OpenRosaXml.Should().Contain("<Diastolic_Blood_Pressure />");
        result.OpenRosaXml.Should().Contain("<input ref=\"/data/Systolic_Blood_Pressure\">");

        // Validate Form Hash
        result.FormHash.Should().NotBeNullOrWhiteSpace();
        result.FormHash.Length.Should().Be(64); // SHA-256 hex string

        // Deterministic form hash verification
        var secondRun = UsdmCompiler.CompileUsdm(SampleUsdmJson);
        secondRun.FormHash.Should().Be(result.FormHash);
    }

    /// <summary>
    /// Validates that UsdmCompiler with ValidateAccessibility option produces an accessibility validation report.
    /// </summary>
    [Fact(DisplayName = "UsdmCompiler_Compile_WithAccessibilityValidation_ProducesReport")]
    [Trait("Category", "GxP_OQ")]
    public void UsdmCompilerCompileWithAccessibilityValidationProducesReport()
    {
        // Arrange
        var options = new CompileOptions { ValidateAccessibility = true };

        // Act
        var result = UsdmCompiler.CompileUsdm(SampleUsdmJson, options: options);

        // Assert
        result.AccessibilityReport.Should().NotBeNull();
        result.AccessibilityReport!.Status.Should().Be("PASS");
        result.AccessibilityReport!.FormHash.Should().Be(result.FormHash);
        result.AccessibilityReport!.Violations.Should().BeEmpty();
    }
}
