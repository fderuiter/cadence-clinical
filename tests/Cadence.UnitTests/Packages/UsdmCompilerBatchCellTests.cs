using System.Collections.Generic;
using System.Linq;

using Cadence.Ddf;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class UsdmCompilerBatchCellTests
{
    [Fact(DisplayName = "UsdmCompiler_ProcessBatchCellMutations_SuccessfullyUpdatesBatchCellsAndLineageNodes")]
    public void UsdmCompilerProcessBatchCellMutationsSuccessfullyUpdatesBatchCellsAndLineageNodes()
    {
        // Arrange
        var mutations = new List<SoACellMutation>
        {
            new("ACT_VS", "ENC_V1", true, Details: "Mandatory Assessment", Footnotes: "Footnote 1", BiomedicalConceptCode: "C25298"),
            new("ACT_VS", "ENC_V2", true, Details: "Optional Assessment", BiomedicalConceptCode: "C25298"),
            new("ACT_EG", "ENC_V1", false, BiomedicalConceptCode: "C38054"),
        };

        var activeConcepts = new Dictionary<string, string>
        {
            ["C25298"] = "ACTIVE",
            ["C38054"] = "ACTIVE",
        };

        var request = new BatchCellMutationRequest(
            StudyId: "STUDY-NSCLC-01",
            VersionId: "v_draft_01",
            Mutations: mutations,
            ChangeReason: "Bulk schedule assignment for treatment epoch",
            ActiveStandardsProfileConcepts: activeConcepts);

        // Act
        var result = UsdmCompiler.ProcessBatchCellMutations(request);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("COMPLETED");
        result.StudyId.Should().Be("STUDY-NSCLC-01");
        result.VersionId.Should().Be("v_draft_01");
        result.AppliedCount.Should().Be(3);
        result.ValidationErrorsCount.Should().Be(0);

        result.UpdatedNodeIds.Should().Contain("node_ACT_VS_ENC_V1");
        result.UpdatedNodeIds.Should().Contain("node_ACT_VS_ENC_V2");
        result.UpdatedNodeIds.Should().Contain("node_ACT_EG_ENC_V1");

        result.LineageNodeIds.Should().Contain("lineage:soa:ACT_VS:ENC_V1");
        result.LineageNodeIds.Should().Contain("lineage:soa:ACT_VS:ENC_V2");
        result.LineageNodeIds.Should().Contain("lineage:soa:ACT_EG:ENC_V1");
    }

    [Fact(DisplayName = "UsdmCompiler_ProcessBatchCellMutations_ValidatesActiveBiomedicalConceptsAgainstStandardsProfile")]
    public void UsdmCompilerProcessBatchCellMutationsValidatesActiveBiomedicalConceptsAgainstStandardsProfile()
    {
        // Arrange
        var mutations = new List<SoACellMutation>
        {
            new("ACT_VS", "ENC_V1", true, BiomedicalConceptCode: "C25298"),
            new("ACT_OLD", "ENC_V1", true, BiomedicalConceptCode: "C_DEPRECATED_99"),
        };

        var activeConcepts = new Dictionary<string, string>
        {
            ["C25298"] = "ACTIVE",
            ["C_DEPRECATED_99"] = "INACTIVE",
        };

        var request = new BatchCellMutationRequest(
            StudyId: "STUDY-NSCLC-01",
            VersionId: "v_draft_01",
            Mutations: mutations,
            ActiveStandardsProfileConcepts: activeConcepts);

        // Act
        var result = UsdmCompiler.ProcessBatchCellMutations(request);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("COMPLETED_WITH_ERRORS");
        result.AppliedCount.Should().Be(1);
        result.ValidationErrorsCount.Should().Be(1);
        result.ValidationErrors.Should().ContainSingle()
            .Which.Should().Contain("C_DEPRECATED_99").And.Contain("not active");
    }
}
