using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Application.Protocol;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;

using Xunit;

namespace Cadence.UnitTests.Kernel;

public sealed class MatrixGapAnalyzerTests
{
    [Fact]
    public void Analyze_CompleteValidDesign_ReturnsNoGaps()
    {
        var proposal = GenerativeProtocolAuthor.GenerateProposal("Phase II Trial");
        var gaps = MatrixGapAnalyzer.Analyze(proposal.Design);

        Assert.Empty(gaps);
    }

    [Fact]
    public void Analyze_MissingPrimaryObjectiveAndEndpoint_DetectsIchM11ViolationsWithPointerPaths()
    {
        var design = new ProtocolDesign
        {
            Title = "Incomplete Design",
            Objectives = [],
            Endpoints = [],
            Arms = [new Arm("arm.1", "Arm 1", ArmType.Experimental, [])],
            Epochs = [new Epoch("ep.1", "Epoch 1", 1)],
            EligibilityCriteria = [],
        };

        var gaps = MatrixGapAnalyzer.Analyze(design);

        Assert.Contains(gaps, g => g.Kind == MatrixGapKind.IchM11Violation && g.JsonPointerPath == "/objectives");
        Assert.Contains(gaps, g => g.Kind == MatrixGapKind.IchM11Violation && g.JsonPointerPath == "/endpoints");
        Assert.Contains(gaps, g => g.Kind == MatrixGapKind.IchM11Violation && g.JsonPointerPath == "/eligibilityCriteria");
    }

    [Fact]
    public void Analyze_UnlinkedEndpointReference_DetectsUnlinkedEndpointWithJsonPointer()
    {
        var design = new ProtocolDesign
        {
            Title = "Unlinked Endpoint Test",
            Objectives = [new Objective("obj.1", ObjectiveLevel.Primary, "Primary Objective")],
            Endpoints = [new Endpoint("ep.1", "obj.nonexistent", ObjectiveLevel.Primary, "Primary Endpoint")],
            Arms = [new Arm("arm.1", "Arm 1", ArmType.Experimental, [])],
            Epochs = [new Epoch("ep.1", "Epoch 1", 1)],
            EligibilityCriteria = [new EligibilityCriterion("crit.1", CriterionCategory.Inclusion, "Inclusion 1", [])],
            PlannedActivities = [
                new PlannedActivity("act.1", "Activity 1", "ep.1", [ActivityLocation.Site], [], Purpose: new DataPurpose { EndpointIds = ["ep.ghost"] })
            ]
        };

        var gaps = MatrixGapAnalyzer.Analyze(design);

        var epGap = Assert.Single(gaps, g => g.JsonPointerPath == "/endpoints/0/objectiveId");
        Assert.Equal(MatrixGapKind.UnlinkedEndpoint, epGap.Kind);
        Assert.Equal(MatrixGapSeverity.Error, epGap.Severity);

        var actGap = Assert.Single(gaps, g => g.JsonPointerPath == "/plannedActivities/0/purpose/endpointIds/0");
        Assert.Equal(MatrixGapKind.UnlinkedEndpoint, actGap.Kind);
    }

    [Fact]
    public void Analyze_OrphanEncounterAndUnreferencedCollectionSpec_DetectsOrphanGaps()
    {
        var design = new ProtocolDesign
        {
            Title = "Orphan Test",
            Objectives = [new Objective("obj.1", ObjectiveLevel.Primary, "Primary Objective")],
            Endpoints = [new Endpoint("ep.1", "obj.1", ObjectiveLevel.Primary, "Primary Endpoint")],
            Arms = [new Arm("arm.1", "Arm 1", ArmType.Experimental, [])],
            Epochs = [new Epoch("ep.1", "Epoch 1", 1)],
            EligibilityCriteria = [new EligibilityCriterion("crit.1", CriterionCategory.Inclusion, "Inclusion 1", [])],
            CollectionSpecs = [new CollectionSpec("spec.orphan", "Unreferenced Spec", [])],
            PlannedActivities = [
                new PlannedActivity("act.1", "Activity 1", "ep.ghost", [ActivityLocation.Site], [], Purpose: new DataPurpose { EndpointIds = ["ep.1"] })
            ]
        };

        var gaps = MatrixGapAnalyzer.Analyze(design);

        Assert.Contains(gaps, g => g.Kind == MatrixGapKind.OrphanEncounter && g.JsonPointerPath == "/plannedActivities/0/epochId");
        Assert.Contains(gaps, g => g.Kind == MatrixGapKind.OrphanEncounter && g.JsonPointerPath == "/collectionSpecs/0");
    }

    [Fact]
    public void Analyze_UnscheduledSafetyProcedure_DetectsUnscheduledSafetyRule()
    {
        var design = new ProtocolDesign
        {
            Title = "Unscheduled Safety Rule Test",
            Objectives = [new Objective("obj.1", ObjectiveLevel.Primary, "Primary Objective")],
            Endpoints = [new Endpoint("ep.1", "obj.1", ObjectiveLevel.Primary, "Primary Endpoint")],
            Arms = [new Arm("arm.1", "Arm 1", ArmType.Experimental, [])],
            Epochs = [new Epoch("ep.1", "Epoch 1", 1)],
            EligibilityCriteria = [new EligibilityCriterion("crit.1", CriterionCategory.Inclusion, "Inclusion 1", [])],
            SafetyRules = [new ProtocolRule("rule.unscheduled", "Safety rule text", [])]
        };

        var gaps = MatrixGapAnalyzer.Analyze(design);

        var safetyGap = Assert.Single(gaps, g => g.Kind == MatrixGapKind.UnscheduledSafetyProcedure);
        Assert.Equal("/safetyRules/0", safetyGap.JsonPointerPath);
    }

    [Fact]
    public void Analyze_CyclicEpochTransition_DetectsOutofOrderStudyDayTiming()
    {
        var design = new ProtocolDesign
        {
            Title = "Cyclic Epoch Test",
            Objectives = [new Objective("obj.1", ObjectiveLevel.Primary, "Primary Objective")],
            Endpoints = [new Endpoint("ep.1", "obj.1", ObjectiveLevel.Primary, "Primary Endpoint")],
            Arms = [new Arm("arm.1", "Arm 1", ArmType.Experimental, [])],
            Epochs = [new Epoch("ep.1", "Screening", 1), new Epoch("ep.2", "Treatment", 2)],
            EligibilityCriteria = [new EligibilityCriterion("crit.1", CriterionCategory.Inclusion, "Inclusion 1", [])],
            PlannedActivities = [
                new PlannedActivity("act.1", "Screening Visit", "ep.1", [ActivityLocation.Site], [], Timing: new ActivityTiming(20, 0, 0), Purpose: new DataPurpose { EndpointIds = ["ep.1"] }),
                new PlannedActivity("act.2", "Treatment Visit 1", "ep.2", [ActivityLocation.Site], [], Timing: new ActivityTiming(5, 0, 0), Purpose: new DataPurpose { EndpointIds = ["ep.1"] }),
            ]
        };

        var gaps = MatrixGapAnalyzer.Analyze(design);

        var cyclicGap = Assert.Single(gaps, g => g.Kind == MatrixGapKind.CyclicEpochTransition);
        Assert.Equal("/plannedActivities/1/timing/day", cyclicGap.JsonPointerPath);
    }
}
