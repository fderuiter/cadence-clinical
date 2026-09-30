using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;

namespace Cadence.TestSupport.Kernel.Protocol;

/// <summary>Protocol designs (ADR-2472) for tests: a minimal one with no outside references, and a complete one.</summary>
internal static class ProtocolDesignSamples
{
    /// <summary>A two-arm design that refers to nothing outside itself, so it can be submitted in any tenant.</summary>
    public static string Minimal { get; } = ProtocolDesignDocument.Write(MinimalDesign());

    /// <summary><see cref="Minimal"/> with a third arm, as an amendment would add it.</summary>
    public static string WithArmC { get; } = ProtocolDesignDocument.Write(MinimalDesign() with
    {
        Arms = [.. MinimalDesign().Arms, new Arm("arm.C", "Cohort C", ArmType.Experimental, [])],
    });

    public static ProtocolDesign MinimalDesign() => new()
    {
        Title = "CARDIO-1",
        Arms =
        [
            new Arm("arm.A", "Active", ArmType.Experimental, []),
            new Arm("arm.B", "Placebo", ArmType.PlaceboComparator, []),
        ],
    };

    /// <summary>
    /// A design that uses every element kind and names <paramref name="concept"/>, <paramref name="codeSystemVersion"/>
    /// and <paramref name="artifact"/> outside itself. Its scientific chain is complete, so it has no design findings.
    /// </summary>
    public static ProtocolDesign Complete(ConceptRef concept, CodeSystemVersionId codeSystemVersion, ArtifactId? artifact = null) => new()
    {
        Title = "CARDIO-1",
        Objectives = [new Objective("obj.1", ObjectiveLevel.Primary, "Show that the drug lowers systolic blood pressure.")],
        Endpoints = [new Endpoint("ep.1", "obj.1", ObjectiveLevel.Primary, "Change in systolic blood pressure at week 12.", concept)],
        Estimands =
        [
            new Estimand(
                "est.1",
                "ep.1",
                "pop.itt",
                ["arm.A", "arm.B"],
                "Change from baseline in SBP at week 12",
                [new IntercurrentEvent("Rescue medication", IntercurrentEventStrategy.TreatmentPolicy)],
                "Difference in means"),
        ],
        Populations = [new Population("pop.itt", "Intention to treat", ["ic.1"])],
        EligibilityCriteria = [new EligibilityCriterion("ic.1", CriterionCategory.Inclusion, "Systolic blood pressure of 140 mmHg or more.", [concept], "sbp >= 140")],
        Arms =
        [
            new Arm("arm.A", "Active", ArmType.Experimental, ["int.1"]),
            new Arm("arm.B", "Placebo", ArmType.PlaceboComparator, []),
        ],
        Epochs = [new Epoch("ep.screening", "Screening", 1), new Epoch("ep.treatment", "Treatment", 2)],
        Cohorts = [new Cohort("coh.1", "Main", ["arm.A", "arm.B"])],
        Interventions = [new Intervention("int.1", "Drug X", Dose: new Dose(10, "mg"), Route: "oral", Regimen: "once daily")],
        PlannedActivities =
        [
            new PlannedActivity(
                "act.week12",
                "Week 12 visit",
                "ep.treatment",
                [ActivityLocation.Site, ActivityLocation.Home],
                ["cs.vitals"],
                new ActivityTiming(84, 3, 3)),
        ],
        CollectionSpecs =
        [
            new CollectionSpec(
                "cs.vitals",
                "Vital signs",
                [
                    new CollectionItem("item.sbp", concept, "mm[Hg]", Required: true, Purpose: new DataPurpose { EndpointIds = ["ep.1"] }),
                    new CollectionItem("item.position", concept, Terminology: new TerminologyBinding(codeSystemVersion, "C78431"), Purpose: new DataPurpose { OperationalNeed = "Measurement conditions" }),
                ]),
        ],
        SafetyRules = [new ProtocolRule("safety.1", "Stop dosing when SBP falls below 90 mmHg.", [concept], "sbp < 90")],
        DiscontinuationRules = [new ProtocolRule("disc.1", "Discontinue after two missed visits.", [])],
        RandomizationScheme = new DesignReference("rand.1", "1:1 permuted blocks"),
        StatisticalDesign = new DesignReference("sap.1", "Statistical analysis plan", artifact),
    };
}
