using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Ddf;
using Cadence.Ddf.Rules;
using Cadence.Ddf.Stages;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class DdfPipelineAndEtlTests
{
    private class FailingAccessibilityValidator : IAccessibilityValidatorPort
    {
        public Task<AccessibilityReport> ValidateAsync(string openRosaXml, CancellationToken cancellationToken = default)
        {
            var report = new AccessibilityReport(
                Status: "FAIL",
                FormHash: "dummy-hash",
                Violations: [new Dictionary<string, object?> { ["id"] = "color-contrast", ["description"] = "Insufficient contrast" }],
                LayoutErrors: ["Element overlaps nearby input field"]
            );
            return Task.FromResult(report);
        }
    }

    [Fact(DisplayName = "USDMNormalizerStage_SynthesizesMissingMetadata_AndEmitsWarnings")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-DDF-001")]
    public void UsdmNormalizerStageSynthesizesMissingMetadataAndEmitsWarnings()
    {
        // Arrange
        var normalizer = new USDMNormalizerStage(strictValidation: true);
        string incompleteJson = """
        {
          "protocol": {
            "name": "Test Protocol"
          },
          "documentedBy": [
            {}
          ]
        }
        """;

        // Act
        var (normalized, warnings) = normalizer.Normalize("STUDY-FALLBACK-001", incompleteJson);

        // Assert
        normalized.Should().NotBeNull();
        warnings.Should().NotBeEmpty();

        var obj = normalized.AsObject();
        obj["id"]?.ToString().Should().NotBeNullOrWhiteSpace();
        obj["name"]?.ToString().Should().Be("Test Protocol");

        var docArr = obj["documentedBy"]?.AsArray();
        docArr.Should().NotBeNull();
        docArr![0]!["type"]?.ToString().Should().Be("Protocol");
        docArr[0]!["templateName"]?.ToString().Should().Be("Standard Template");
        docArr[0]!["language"]!["code"]?.ToString().Should().Be("en");
    }

    [Fact(DisplayName = "USDMNormalizerStage_DetectsDuplicateIds_AndThrows")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-DDF-001")]
    public void UsdmNormalizerStageDetectsDuplicateIdsAndThrows()
    {
        // Arrange
        var normalizer = new USDMNormalizerStage(strictValidation: true);
        string dupJson = """
        {
          "id": "DUP-ID-01",
          "name": "Duplicate Test",
          "protocol": {
            "items": [
              { "id": "DUP-ID-01", "name": "Item 1" }
            ]
          }
        }
        """;

        // Act & Assert
        Action act = () => normalizer.Normalize("STUDY-001", dupJson);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Duplicate physical ID 'DUP-ID-01'*");
    }

    [Fact(DisplayName = "RuleCompiler_TranslatesConditionTrees_ToOpenRosaXPath")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-DDF-001")]
    public void RuleCompilerTranslatesConditionTreesToOpenRosaXPath()
    {
        // Arrange
        var ageRef = new FieldReference("AGE", FormId: "DEMOG");
        var ageNode = new ExpressionNode("field_ref", FieldRef: ageRef);
        var constNode = new ExpressionNode("constant", Value: 18);
        var compNode = new ExpressionNode("comparison", Operator: ">=", Operands: [ageNode, constNode]);

        // Act
        string xpath = XPathCompiler.CompileConditionToXPath(compNode);

        // Assert
        xpath.Should().Be("(/DEMOG/AGE >= 18)");
    }

    [Fact(DisplayName = "RuleCompiler_SanitizesIdentifiers_AndExtractsAppearance")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-DDF-001")]
    public void RuleCompilerSanitizesIdentifiersAndExtractsAppearance()
    {
        // Act & Assert
        XPathCompiler.SanitizeIdentifier("123 Field Name!").Should().StartWith("item_123_Field_Name");
        XPathCompiler.SanitizeIdentifier("SYS_BP").Should().Be("SYS_BP");

        var itemDict = new Dictionary<string, object?> { ["cols"] = 2 };
        XPathCompiler.ExtractAppearance(itemDict).Should().Be("w2");
    }

    [Fact(DisplayName = "CircularDependencyDetector_DetectsCycles_InSkipLogic")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-DDF-001")]
    public void CircularDependencyDetectorDetectsCyclesInSkipLogic()
    {
        // Arrange: A depends on B, B depends on A
        var ruleA = new Dictionary<string, object?>
        {
            ["type"] = "skip_logic",
            ["target_field"] = "FIELD_A",
            ["condition"] = new ExpressionNode("field_ref", FieldRef: new FieldReference("FIELD_B"))
        };
        var ruleB = new Dictionary<string, object?>
        {
            ["type"] = "skip_logic",
            ["target_field"] = "FIELD_B",
            ["condition"] = new ExpressionNode("field_ref", FieldRef: new FieldReference("FIELD_A"))
        };

        // Act
        var cycles = CircularDependencyDetector.DetectCircularDependencies([ruleA, ruleB]);

        // Assert
        cycles.Should().NotBeEmpty();
        cycles[0].Should().Contain("FIELD_A");
        cycles[0].Should().Contain("FIELD_B");
    }

    [Fact(DisplayName = "SmartBlocksCatalog_InstantiatesDomainDefinitions")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-DDF-001")]
    public void SmartBlocksCatalogInstantiatesDomainDefinitions()
    {
        // Act
        var catalog = SmartBlocksCatalogService.GetCatalog();
        var vsBlock = SmartBlocksCatalogService.InstantiateSmartBlock("BLOCK-VITAL-SIGNS", "FORM-VS-01");

        // Assert
        catalog.Should().HaveCount(2);
        vsBlock.Should().NotBeNull();
        vsBlock.FormId.Should().Be("FORM-VS-01");
        vsBlock.DataSchema.Variables.Should().Contain(v => v.Id == "VS_SYSBP");
        vsBlock.UiSchema.Sections.Should().ContainSingle();
    }

    [Fact(DisplayName = "DatasetJsonEtlProcessor_ExtractsSdtmDomainRecords")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-DDF-001")]
    public void DatasetJsonEtlProcessorExtractsSdtmDomainRecords()
    {
        // Arrange
        var vsForm = SmartBlocksCatalogService.InstantiateSmartBlock("BLOCK-VITAL-SIGNS", "FORM-VS-01");
        var obs = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["study_id"] = "STUDY-101",
                ["subject_id"] = "SUBJ-001",
                ["VS_SYSBP"] = 120,
                ["VS_DIABP"] = 80
            }
        };

        // Act
        var datasetJson = DatasetJsonEtlProcessor.ExtractSdtmDomain(vsForm, obs, domain: "VS");

        // Assert
        datasetJson.Should().NotBeNull();
        datasetJson["records"].Should().Be(2);
        datasetJson["domain"].Should().Be("VS");

        var rows = (IReadOnlyList<IReadOnlyList<object?>>)datasetJson["rows"]!;
        rows.Should().HaveCount(2);
        rows[0].Should().Contain("STUDY-101-SUBJ-001");
        rows[0].Should().Contain("120");
    }

    [Fact(DisplayName = "DDFCompiler_PipelineAsync_ExecutesWithAccessibilityValidationFailure")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-DDF-001")]
    public async Task DdfCompilerPipelineAsyncExecutesWithAccessibilityValidationFailure()
    {
        // Arrange
        var failingValidator = new FailingAccessibilityValidator();
        var compiler = new DDFCompiler(accessibilityValidator: failingValidator);

        string samplePayload = """
        {
          "protocol": {
            "name": "Accessibility Test Protocol",
            "items": [
              { "id": "TEST_ITEM", "name": "Test Item", "type": "string" }
            ]
          }
        }
        """;
        var jsonNode = JsonNode.Parse(samplePayload)!;

        // Act & Assert
        Func<Task> act = async () => await compiler.CompileAsync("STUDY-ACC-01", jsonNode);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Layout/Accessibility Validation Failed*");
    }
}
