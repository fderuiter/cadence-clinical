using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;

namespace Cadence.TestSupport.Kernel.Protocol;

/// <summary>
/// Protocols for Schedule of Activities tests, and the expected matrices and diffs in <c>tests/fixtures/soa</c>
/// (ADR-2475).
/// </summary>
internal static class SoaSamples
{
    /// <summary>The concept versions the CARDIO-2 schedule names, with fixed ids so its expected files are stable.</summary>
    public static readonly ConceptRef Sbp = new(ConceptId.FromGuid(Guid.Parse("0192f3c4-0000-7000-8000-00000000000a")), 1);

    public static readonly ConceptRef Hba1c = new(ConceptId.FromGuid(Guid.Parse("0192f3c4-0000-7000-8000-00000000000b")), 2);

    public static readonly ConceptRef QtInterval = new(ConceptId.FromGuid(Guid.Parse("0192f3c4-0000-7000-8000-00000000000c")), 1);

    public static readonly ConceptRef ConsentDate = new(ConceptId.FromGuid(Guid.Parse("0192f3c4-0000-7000-8000-00000000000d")), 1);

    /// <summary>
    /// The design the CDISC USDM 4 example study imports to (ADR-2473), read from its checked-in expected file in
    /// <c>tests/fixtures/usdm</c>.
    /// </summary>
    public static ProtocolDesign CdiscExample()
    {
        var expected = JsonNode.Parse(File.ReadAllText(UsdmSamples.PathOf($"{UsdmSamples.CdiscExample}.design.json")))!;
        return ProtocolDesignDocument.Read(expected["design"]!.ToJsonString()).Design
            ?? throw new InvalidOperationException("The CDISC example's expected design does not read as a design.");
    }

    /// <summary>
    /// An amendment of the CDISC example study: the follow-up call moves from day 43 to day 57 and may no longer be by
    /// telehealth, week 2's window widens, a day 71 follow-up visit collects the procedures, the optional weight is no
    /// longer collected at week 2, one exclusion criterion is reworded and a new one is added.
    /// </summary>
    public static ProtocolDesign CdiscAmendment()
    {
        var design = CdiscExample();
        return design with
        {
            PlannedActivities =
            [
                .. design.PlannedActivities.Select(a => a.Id switch
                {
                    "ScheduledActivityInstance_4" => a with
                    {
                        CollectionSpecIds = ["Activity_2"],
                        Timing = a.Timing! with { WindowBefore = 2, WindowAfter = 2 },
                    },
                    "ScheduledActivityInstance_6" => a with
                    {
                        Locations = [ActivityLocation.Home],
                        Timing = a.Timing! with { Day = 57 },
                    },
                    _ => a,
                }),
                new PlannedActivity("ScheduledActivityInstance_7", "E7", "StudyEpoch_4", [ActivityLocation.Site], ["Activity_2"], new ActivityTiming(71, 3, 3)),
            ],
            EligibilityCriteria =
            [
                .. design.EligibilityCriteria.Select(c => c.Id == "EligibilityCriterion_4" ? c with { Text = c.Text + " Reworded by the amendment." } : c),
                new EligibilityCriterion("EligibilityCriterion_6", CriterionCategory.Exclusion, "Receiving another investigational product.", []),
            ],
            Populations =
            [
                .. design.Populations.Select(p => p.Id == "StudyDesignPopulation_1" ? p with { CriterionIds = [.. p.CriterionIds, "EligibilityCriterion_6"] } : p),
            ],
        };
    }

    /// <summary>
    /// CARDIO-2: a remote screening call, a baseline visit, three treatment visits with windows, an event-driven early
    /// termination visit, a specification collected for an operational need and one no activity collects.
    /// </summary>
    public static ProtocolDesign Cardio() => Cardio(Sbp, Hba1c, QtInterval, ConsentDate);

    /// <summary>CARDIO-2 recording the given concept versions, for tests that register them in a tenant first.</summary>
    public static ProtocolDesign Cardio(ConceptRef sbp, ConceptRef hba1c, ConceptRef qtInterval, ConceptRef consentDate) => new()
    {
        Title = "CARDIO-2",
        Objectives = [new Objective("obj.1", ObjectiveLevel.Primary, "Show that the drug lowers systolic blood pressure.")],
        Endpoints = [new Endpoint("ep.1", "obj.1", ObjectiveLevel.Primary, "Change in systolic blood pressure at week 12.", sbp)],
        Populations = [new Population("pop.itt", "Intention to treat", ["ic.1"])],
        EligibilityCriteria = [new EligibilityCriterion("ic.1", CriterionCategory.Inclusion, "Systolic blood pressure of 140 mmHg or more.", [sbp])],
        Arms = [new Arm("arm.A", "Active", ArmType.Experimental, []), new Arm("arm.B", "Placebo", ArmType.PlaceboComparator, [])],
        Epochs =
        [
            new Epoch("epoch.treatment", "Treatment", 2),
            new Epoch("epoch.screening", "Screening", 1),
            new Epoch("epoch.followup", "Follow-up", 3),
        ],
        PlannedActivities =
        [
            new PlannedActivity("visit.week12", "Week 12", "epoch.treatment", [ActivityLocation.Site], ["cs.vitals", "cs.labs", "cs.ecg"], new ActivityTiming(85, 3, 3)),
            new PlannedActivity("visit.et", "Early termination", "epoch.treatment", [ActivityLocation.Site], ["cs.vitals", "cs.labs"]),
            new PlannedActivity("visit.week4", "Week 4", "epoch.treatment", [ActivityLocation.Site, ActivityLocation.Home], ["cs.vitals"], new ActivityTiming(29, 3, 3)),
            new PlannedActivity("visit.baseline", "Baseline", "epoch.treatment", [ActivityLocation.Site], ["cs.vitals", "cs.labs", "cs.ecg"], new ActivityTiming(1)),
            new PlannedActivity("visit.screening", "Screening", "epoch.screening", [ActivityLocation.Telehealth], ["cs.consent", "cs.vitals"], new ActivityTiming(-14, 7, 0)),
            new PlannedActivity("visit.week8", "Week 8", "epoch.treatment", [ActivityLocation.Site], ["cs.vitals", "cs.labs"], new ActivityTiming(57, 3, 3)),
        ],
        CollectionSpecs =
        [
            new CollectionSpec("cs.consent", "Informed consent", [new CollectionItem("item.consent", consentDate, Required: true)], new DataPurpose { OperationalNeed = "Documenting consent before any study procedure" }),
            new CollectionSpec("cs.vitals", "Vital signs", [new CollectionItem("item.sbp", sbp, "mm[Hg]", Required: true, Purpose: new DataPurpose { EndpointIds = ["ep.1"] })]),
            new CollectionSpec("cs.labs", "Laboratory", [new CollectionItem("item.hba1c", hba1c, "%")]),
            new CollectionSpec("cs.ecg", "12-lead ECG", [new CollectionItem("item.qt", qtInterval, "ms")]),
            new CollectionSpec("cs.pk", "Pharmacokinetics", [new CollectionItem("item.pk", hba1c, "ng/mL")]),
        ],
    };

    /// <summary>The full path of a file in <c>tests/fixtures/soa</c>.</summary>
    public static string PathOf(string fileName) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(UsdmSamples.PathOf("x"))!, "..", "soa", fileName));
}
