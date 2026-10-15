using System.Collections.Generic;

using Cadence.Ddf;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The USDM compiler accepts a study tagged with each therapeutic area the web wizard offers. The payload is a
/// hand-written minimal USDM document, not the wizard's output.
/// </summary>
public class UsdmCompilerTherapeuticAreaTests
{
    [Theory]
    [InlineData("Oncology", "CDNC-2026-ONC", "A Phase II Study in Advanced NSCLC")]
    [InlineData("Cardiology", "CDNC-2026-CARD", "A Phase III Study in Heart Failure")]
    [InlineData("Neurology", "CDNC-2026-NEUR", "A Phase II Study in Alzheimer's Disease")]
    [InlineData("Immunology", "CDNC-2026-IMM", "A Phase III Study in Rheumatoid Arthritis")]
    [InlineData("Vaccines", "CDNC-2026-VAC", "A Phase I/II mRNA Vaccine Study")]
    public void CompileUsdm_CompilesStudyTaggedWithTherapeuticArea(
        string taCategory, string protocolId, string studyTitle)
    {
        // Arrange
        var options = new CompileOptions { ValidateAccessibility = false };
        string usdmJson = $$"""
        {
          "id": "{{protocolId}}",
          "name": "{{studyTitle}}",
          "version": "1.0",
          "study": {
            "id": "{{protocolId}}",
            "name": "{{studyTitle}}",
            "version": "1.0"
          },
          "therapeutic_area": "{{taCategory}}",
          "activities": [
            {
              "name": "Vital Signs",
              "items": [
                {
                  "id": "VS_SYSBP",
                  "name": "SYSBP",
                  "type": "integer",
                  "label": "Systolic Blood Pressure (mmHg)"
                },
                {
                  "id": "VS_DIABP",
                  "name": "DIABP",
                  "type": "integer",
                  "label": "Diastolic Blood Pressure (mmHg)"
                }
              ]
            },
            {
              "name": "ECG",
              "items": [
                {
                  "id": "EG_HR",
                  "name": "HR",
                  "type": "integer",
                  "label": "Heart Rate (bpm)"
                }
              ]
            }
          ]
        }
        """;

        // Act
        var result = UsdmCompiler.CompileUsdm(usdmJson, fallbackStudyId: protocolId, options: options);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("COMPLETED");
        result.StudyId.Should().Be(protocolId);
        result.Name.Should().Be(studyTitle);
        result.OdmXml.Should().NotBeNullOrEmpty();
        result.OpenRosaXml.Should().NotBeNullOrEmpty();
        result.FormHash.Should().NotBeNullOrEmpty();
    }
}
