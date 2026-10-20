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
        var gaps = MatrixGapAnalyzer.Analyze(CompleteDesign());

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

    /// <summary>A two-arm design whose every activity, collection item and endpoint is linked, so it has no gaps.</summary>
    private static ProtocolDesign CompleteDesign()
    {
        var primaryObj = new Objective("obj.primary", ObjectiveLevel.Primary, "Evaluate efficacy and safety of the investigational drug in target patient population.");
        var secondaryObj = new Objective("obj.secondary", ObjectiveLevel.Secondary, "Assess secondary pharmacodynamic and biomarker responses over 12 weeks.");

        var primaryEndpoint = new Endpoint("ep.primary", "obj.primary", ObjectiveLevel.Primary, "Change from baseline in systolic blood pressure (mmHg) at Week 12.", ConceptRef.Parse("concept:vitals:systolic_bp"));
        var secondaryEndpoint = new Endpoint("ep.secondary", "obj.secondary", ObjectiveLevel.Secondary, "Incidence of treatment-emergent adverse events (TEAEs) through Week 16.", ConceptRef.Parse("concept:safety:teae"));

        var incCriterion = new EligibilityCriterion("crit.inc1", CriterionCategory.Inclusion, "Adult participants aged 18 to 75 years with diagnosed hypertension.", [ConceptRef.Parse("concept:demo:age")]); // deid-ignore
        var excCriterion = new EligibilityCriterion("crit.exc1", CriterionCategory.Exclusion, "History of severe cardiovascular event within 6 months prior to screening.", [ConceptRef.Parse("concept:history:cardio_event")]);

        var population = new Population("pop.itt", "Intent-to-Treat Population", ["crit.inc1", "crit.exc1"], "All randomized participants who receive at least one dose of study intervention.");

        var armA = new Arm("arm.exp", "Investigational Drug 100mg", ArmType.Experimental, ["int.drug100"]);
        var armB = new Arm("arm.pbo", "Matching Placebo", ArmType.PlaceboComparator, ["int.pbo"]);

        var epochScreening = new Epoch("ep.screening", "Screening Epoch", 1);
        var epochTreatment = new Epoch("ep.treatment", "Treatment Epoch", 2);
        var epochFollowUp = new Epoch("ep.followup", "Follow-Up Epoch", 3);

        var drugIntervention = new Intervention("int.drug100", "Investigational Drug 100mg Tablet", ConceptRef.Parse("concept:drug:active"), new Dose(100m, "mg"), "Oral", "Once daily");
        var pboIntervention = new Intervention("int.pbo", "Matching Placebo Tablet", ConceptRef.Parse("concept:drug:placebo"), new Dose(0m, "mg"), "Oral", "Once daily");

        var vitalSpec = new CollectionSpec(
            "spec.vitals",
            "Vital Signs Panel",
            [
                new CollectionItem("item.sbp", ConceptRef.Parse("concept:vitals:systolic_bp"), "mmHg", Required: true, Purpose: new DataPurpose { EndpointIds = ["ep.primary"] }),
                new CollectionItem("item.dbp", ConceptRef.Parse("concept:vitals:diastolic_bp"), "mmHg", Required: true, Purpose: new DataPurpose { EndpointIds = ["ep.primary"] }),
            ],
            Purpose: new DataPurpose { EndpointIds = ["ep.primary"] });

        var safetySpec = new CollectionSpec(
            "spec.safety_labs",
            "Safety Laboratory Panel",
            [
                new CollectionItem("item.ae", ConceptRef.Parse("concept:safety:teae"), Required: true, Purpose: new DataPurpose { EndpointIds = ["ep.secondary"], SafetyRuleIds = ["rule.safety1"] }),
            ],
            Purpose: new DataPurpose { EndpointIds = ["ep.secondary"], SafetyRuleIds = ["rule.safety1"] });

        var actScreening = new PlannedActivity("act.screening", "Screening Visit", "ep.screening", [ActivityLocation.Site], ["spec.vitals", "spec.safety_labs"], new ActivityTiming(-14, 2, 0), new DataPurpose { EndpointIds = ["ep.primary", "ep.secondary"] });
        var actBaseline = new PlannedActivity("act.baseline", "Day 1 Baseline Visit", "ep.treatment", [ActivityLocation.Site], ["spec.vitals", "spec.safety_labs"], new ActivityTiming(1, 0, 1), new DataPurpose { EndpointIds = ["ep.primary", "ep.secondary"] });
        var actWeek4 = new PlannedActivity("act.week4", "Week 4 Visit", "ep.treatment", [ActivityLocation.Site, ActivityLocation.Home], ["spec.vitals"], new ActivityTiming(28, 2, 2), new DataPurpose { EndpointIds = ["ep.primary"] });
        var actWeek12 = new PlannedActivity("act.week12", "Week 12 Primary Endpoint Visit", "ep.treatment", [ActivityLocation.Site], ["spec.vitals", "spec.safety_labs"], new ActivityTiming(84, 3, 3), new DataPurpose { EndpointIds = ["ep.primary", "ep.secondary"] });
        var actFollowUp = new PlannedActivity("act.followup", "Week 16 Follow-up Visit", "ep.followup", [ActivityLocation.Site], ["spec.safety_labs"], new ActivityTiming(112, 3, 3), new DataPurpose { EndpointIds = ["ep.secondary"], SafetyRuleIds = ["rule.safety1"] });

        var safetyRule = new ProtocolRule("rule.safety1", "Monitor TEAEs and discontinue if grade 3+ toxicity occurs.", [ConceptRef.Parse("concept:safety:teae")]);

        return new ProtocolDesign
        {
            Title = "Phase II Trial",
            Objectives = [primaryObj, secondaryObj],
            Endpoints = [primaryEndpoint, secondaryEndpoint],
            EligibilityCriteria = [incCriterion, excCriterion],
            Populations = [population],
            Arms = [armA, armB],
            Epochs = [epochScreening, epochTreatment, epochFollowUp],
            Interventions = [drugIntervention, pboIntervention],
            CollectionSpecs = [vitalSpec, safetySpec],
            PlannedActivities = [actScreening, actBaseline, actWeek4, actWeek12, actFollowUp],
            SafetyRules = [safetyRule],
        };
    }
}
