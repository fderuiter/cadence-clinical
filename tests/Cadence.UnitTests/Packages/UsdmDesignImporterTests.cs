using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Ddf;
using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Importing a CDISC USDM 4 document into the kernel's protocol design (ADR-2473): the CDISC example study against a
/// checked-in expected design, and each mapping and refusal on a small study built for the purpose.
/// </summary>
public sealed class UsdmDesignImporterTests
{
    /// <summary>A USDM 4 study with one of everything the importer maps.</summary>
    private const string Mini = """
    {
      "usdmVersion": "4.0.0",
      "study": {
        "id": "Study_1",
        "name": "MINI-1",
        "versions": [
          {
            "id": "StudyVersion_1",
            "titles": [
              { "id": "Title_1", "text": "Short", "type": { "code": "C207615", "decode": "Brief Study Title" } },
              { "id": "Title_2", "text": "A study of drug X in adults with hypertension", "type": { "code": "C207616", "decode": "Official Study Title" } }
            ],
            "biomedicalConcepts": [
              { "id": "BC_1", "name": "Systolic Blood Pressure", "code": { "id": "AliasCode_1", "standardCode": { "code": "C25298", "decode": "Systolic Blood Pressure" } } }
            ],
            "eligibilityCriterionItems": [ { "id": "ECI_1", "text": "Adults aged 18 or over." } ],
            "studyInterventions": [
              {
                "id": "SI_1",
                "name": "Drug X",
                "administrations": [
                  {
                    "id": "Admin_1",
                    "dose": { "id": "Quantity_1", "value": 10, "unit": { "id": "AliasCode_2", "standardCode": { "code": "C28253", "decode": "Milligram" } } },
                    "route": { "id": "AliasCode_3", "standardCode": { "code": "C38288", "decode": "Oral Route of Administration" } },
                    "frequency": { "id": "AliasCode_4", "standardCode": { "code": "C25473", "decode": "Daily" } }
                  }
                ]
              }
            ],
            "studyDesigns": [
              {
                "id": "StudyDesign_1",
                "studyInterventionIds": [ "SI_1" ],
                "objectives": [
                  {
                    "id": "Objective_1",
                    "text": "Show that drug X lowers systolic blood pressure.",
                    "level": { "code": "C85826", "decode": "Primary Objective" },
                    "endpoints": [
                      { "id": "Endpoint_1", "text": "Change in systolic blood pressure at week 2.", "level": { "code": "C94496", "decode": "Primary Endpoint" } }
                    ]
                  }
                ],
                "estimands": [
                  {
                    "id": "Estimand_1",
                    "variableOfInterestId": "Endpoint_1",
                    "analysisPopulationId": "AnalysisPopulation_1",
                    "interventionIds": [ "SI_1" ],
                    "populationSummary": "Difference in means",
                    "intercurrentEvents": [ { "id": "ICE_1", "name": "Rescue", "text": "Rescue medication taken", "strategy": "Treatment Policy" } ]
                  }
                ],
                "population": {
                  "id": "Population_1",
                  "name": "Adults",
                  "description": " ",
                  "criterionIds": [ "EC_1" ],
                  "cohorts": [ { "id": "Cohort_1", "name": "Main", "description": "" } ]
                },
                "analysisPopulations": [ { "id": "AnalysisPopulation_1", "name": "ITT", "text": "All randomised participants." } ],
                "eligibilityCriteria": [ { "id": "EC_1", "category": { "code": "C25532", "decode": "Inclusion Criteria" }, "criterionItemId": "ECI_1" } ],
                "arms": [ { "id": "Arm_1", "name": "Drug X", "type": { "code": "C174266", "decode": "Experimental Arm" }, "populationIds": [ "Cohort_1" ] } ],
                "epochs": [
                  { "id": "Epoch_2", "name": "Treatment", "previousId": "Epoch_1" },
                  { "id": "Epoch_1", "name": "Screening", "nextId": "Epoch_2" }
                ],
                "elements": [ { "id": "Element_1", "studyInterventionIds": [ "SI_1" ] } ],
                "studyCells": [ { "id": "Cell_1", "armId": "Arm_1", "epochId": "Epoch_2", "elementIds": [ "Element_1" ] } ],
                "encounters": [
                  {
                    "id": "Encounter_1",
                    "name": "Screening visit",
                    "environmentalSettings": [ { "code": "C211570", "decode": "Clinic" } ],
                    "contactModes": [ { "code": "C175574", "decode": "In Person" } ]
                  },
                  {
                    "id": "Encounter_2",
                    "name": "Week 2 call",
                    "environmentalSettings": [ { "code": "C18002", "decode": "Home" } ],
                    "contactModes": [ { "code": "C171537", "decode": "Telephone Call" } ]
                  }
                ],
                "activities": [ { "id": "Activity_1", "name": "Vital signs", "biomedicalConceptIds": [ "BC_1" ] } ],
                "scheduleTimelines": [
                  {
                    "id": "ScheduleTimeline_1",
                    "mainTimeline": true,
                    "instances": [
                      { "id": "SAI_1", "instanceType": "ScheduledActivityInstance", "epochId": "Epoch_1", "encounterId": "Encounter_1", "activityIds": [ "Activity_1" ] },
                      { "id": "SAI_2", "instanceType": "ScheduledActivityInstance", "epochId": "Epoch_2", "encounterId": "Encounter_2", "activityIds": [ "Activity_1" ] }
                    ],
                    "timings": [
                      { "id": "Timing_1", "type": { "code": "C201358", "decode": "Fixed Reference" }, "value": "P1D", "relativeFromScheduledInstanceId": "SAI_1" },
                      {
                        "id": "Timing_2",
                        "type": { "code": "C201357", "decode": "After" },
                        "value": "P2W",
                        "relativeFromScheduledInstanceId": "SAI_2",
                        "relativeToScheduledInstanceId": "SAI_1",
                        "windowLower": "P2D",
                        "windowUpper": "P3D"
                      }
                    ]
                  }
                ]
              }
            ]
          }
        ]
      }
    }
    """;

    private static readonly JsonSerializerOptions GoldenJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The CDISC example study imports to the checked-in design, with the same notes. Set <c>CADENCE_UPDATE_GOLDEN=1</c>
    /// to rewrite the expected file after an intended change, then review its diff.
    /// </summary>
    [Fact(DisplayName = "Import_CdiscExample_MatchesTheExpectedDesignAndNotes")]
    public async Task ImportCdiscExampleMatchesTheExpectedDesignAndNotes()
    {
        var result = await ImportAsync(UsdmSamples.CdiscExampleJson());

        result.Problems.Should().BeEmpty();
        var actual = new JsonObject
        {
            ["design"] = JsonNode.Parse(ProtocolDesignDocument.Write(result.Design!)),
            ["notes"] = new JsonArray([.. result.Notes.Select(n => (JsonNode)new JsonObject { ["source"] = n.Source, ["message"] = n.Message })]),
        }.ToJsonString(GoldenJson).ReplaceLineEndings("\n") + "\n";
        var expectedPath = UsdmSamples.PathOf($"{UsdmSamples.CdiscExample}.design.json");
        if (Environment.GetEnvironmentVariable("CADENCE_UPDATE_GOLDEN") == "1")
        {
            await File.WriteAllTextAsync(expectedPath, actual, TestContext.Current.CancellationToken);
        }

        actual.Should().Be((await File.ReadAllTextAsync(expectedPath, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// The example's visits land on study days from its timings: a fixed day 1, a visit two days before it (there is
    /// no day 0), visits 14, 28 and 42 days after it with their windows, and home and telephone locations.
    /// </summary>
    [Fact(DisplayName = "Import_CdiscExample_PlacesVisitsOnStudyDays")]
    public async Task ImportCdiscExamplePlacesVisitsOnStudyDays()
    {
        var design = (await ImportAsync(UsdmSamples.CdiscExampleJson())).Design!;

        design.Title.Should().Be("Something Very Official");
        design.PlannedActivities.Select(a => a.Timing).Should().Equal(
            new ActivityTiming(-2),
            new ActivityTiming(1),
            new ActivityTiming(1),
            new ActivityTiming(15, 1, 1),
            new ActivityTiming(29, 1, 1),
            new ActivityTiming(43, 3, 3));
        design.PlannedActivities.Should().Contain(a => a.Locations.SequenceEqual(new[] { ActivityLocation.Home, ActivityLocation.Telehealth }));
        ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(design)).IsValid.Should().BeTrue();
    }

    /// <summary>Every element kind of the small study maps to the design as ADR-2473 §4.2 says.</summary>
    [Fact(DisplayName = "Import_MapsEachElementKind")]
    public async Task ImportMapsEachElementKind()
    {
        var result = await ImportAsync(Mini);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Problems));
        var concept = ConceptFor("C25298");
        result.Design.Should().BeEquivalentTo(new ProtocolDesign
        {
            Title = "A study of drug X in adults with hypertension",
            Objectives = [new Objective("Objective_1", ObjectiveLevel.Primary, "Show that drug X lowers systolic blood pressure.")],
            Endpoints = [new Endpoint("Endpoint_1", "Objective_1", ObjectiveLevel.Primary, "Change in systolic blood pressure at week 2.")],
            Estimands =
            [
                new Estimand(
                    "Estimand_1",
                    "Endpoint_1",
                    "AnalysisPopulation_1",
                    ["Arm_1"],
                    "Change in systolic blood pressure at week 2.",
                    [new IntercurrentEvent("Rescue medication taken", IntercurrentEventStrategy.TreatmentPolicy)],
                    "Difference in means",
                    ["SI_1"]),
            ],
            Populations =
            [
                new Population("Population_1", "Adults", ["EC_1"]),
                new Population("AnalysisPopulation_1", "ITT", [], "All randomised participants."),
            ],
            EligibilityCriteria = [new EligibilityCriterion("EC_1", CriterionCategory.Inclusion, "Adults aged 18 or over.", [])],
            Arms = [new Arm("Arm_1", "Drug X", ArmType.Experimental, ["SI_1"])],
            Epochs = [new Epoch("Epoch_1", "Screening", 1), new Epoch("Epoch_2", "Treatment", 2)],
            Cohorts = [new Cohort("Cohort_1", "Main", ["Arm_1"])],
            Interventions = [new Intervention("SI_1", "Drug X", Dose: new Dose(10, "mg"), Route: "Oral Route of Administration", Regimen: "Daily")],
            PlannedActivities =
            [
                new PlannedActivity("SAI_1", "Screening visit", "Epoch_1", [ActivityLocation.Site], ["Activity_1"], new ActivityTiming(1)),
                new PlannedActivity("SAI_2", "Week 2 call", "Epoch_2", [ActivityLocation.Home, ActivityLocation.Telehealth], ["Activity_1"], new ActivityTiming(15, 2, 3)),
            ],
            CollectionSpecs = [new CollectionSpec("Activity_1", "Vital signs", [new CollectionItem("Activity_1.BC_1", concept)])],
        });
        result.Notes.Should().ContainSingle().Which.Source.Should().Be("activities", "USDM does not say why data is collected");
    }

    /// <summary>A strategy USDM states in words maps to a known strategy when it names one, and otherwise keeps the words.</summary>
    [Theory(DisplayName = "Import_MapsIntercurrentEventStrategies")]
    [InlineData("Treatment Policy", IntercurrentEventStrategy.TreatmentPolicy)]
    [InlineData("treatment-policy strategy", IntercurrentEventStrategy.TreatmentPolicy)]
    [InlineData("Hypothetical", IntercurrentEventStrategy.Hypothetical)]
    [InlineData("Composite variable strategy", IntercurrentEventStrategy.Composite)]
    [InlineData("While on treatment", IntercurrentEventStrategy.WhileOnTreatment)]
    [InlineData("Principal stratum", IntercurrentEventStrategy.PrincipalStratum)]
    [InlineData("Participants who take rescue medication are excluded", IntercurrentEventStrategy.Other)]
    public async Task ImportMapsIntercurrentEventStrategies(string stated, IntercurrentEventStrategy expected)
    {
        var result = await ImportAsync(Edit(Mini, design => design["estimands"]![0]!["intercurrentEvents"]![0]!["strategy"] = stated));

        var ice = result.Design!.Estimands[0].IntercurrentEvents[0];
        ice.Strategy.Should().Be(expected);
        ice.StrategyText.Should().Be(expected == IntercurrentEventStrategy.Other ? stated : null);
    }

    /// <summary>
    /// A unit with a certain UCUM form is converted; any other keeps its CDISC name, and tagged text, a setting with no
    /// activity location, an unknown arm type and a second administration are noted.
    /// </summary>
    [Fact(DisplayName = "Import_NotesWhatItApproximates")]
    public async Task ImportNotesWhatItApproximates()
    {
        var json = Edit(Mini, design =>
        {
            design["arms"]![0]!["type"] = new JsonObject { ["code"] = "C99999", ["decode"] = "Sham Arm" };
            design["objectives"]![0]!["text"] = "Show that drug X lowers systolic blood pressure in adults over <usdm:tag name=\"min_age\"/>.";
            design["encounters"]![1]!["environmentalSettings"]!.AsArray().Add(new JsonObject { ["code"] = "C99998", ["decode"] = "Space Station" });
        });
        json = EditVersion(json, version =>
        {
            var administrations = version["studyInterventions"]![0]!["administrations"]!.AsArray();
            administrations[0]!["dose"]!["unit"] = new JsonObject { ["standardCode"] = new JsonObject { ["code"] = "C48542", ["decode"] = "Tablet Dosing Unit" } };
            administrations.Add(administrations[0]!.DeepClone());
        });

        var result = await ImportAsync(json);

        result.Design!.Interventions[0].Dose.Should().Be(new Dose(10, "Tablet Dosing Unit"));
        result.Design.Arms[0].Type.Should().Be(ArmType.Other);
        result.Design.PlannedActivities[1].Locations.Should().Equal(ActivityLocation.Home, ActivityLocation.Telehealth);
        result.Notes.Select(n => $"{n.Source}: {n.Message}").Should().Contain(
        [
            "Objective_1: the text holds USDM tags, kept as written; their dictionary values are not substituted.",
            "Arm_1: arm type 'Sham Arm' is read as Other.",
            "Encounter_2: setting or contact mode 'Space Station' has no activity location.",
            "SI_1: 2 administrations; the design keeps the first.",
            "Admin_1: dose unit 'Tablet Dosing Unit' has no UCUM equivalent here; its CDISC name is kept.",
        ]);
    }

    /// <summary>
    /// A Biomedical Concept the tenant's Concept Registry does not have fails the import with every such concept named,
    /// and each concept code is resolved once however many activities use it.
    /// </summary>
    [Fact(DisplayName = "Import_UnresolvedBiomedicalConcept_FailsTheImport")]
    public async Task ImportUnresolvedBiomedicalConceptFailsTheImport()
    {
        var asked = new List<string>();
        var result = await UsdmDesignImporter.ImportAsync(
            UsdmSamples.CdiscExampleJson(),
            (code, _) =>
            {
                asked.Add(code);
                return Task.FromResult<ConceptRef?>(code == "C25298" ? null : ConceptFor(code));
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Design.Should().BeNull();
        result.Problems.Should().Equal("BiomedicalConcept_6 (Systolic Blood Pressure (6), CDISC C25298) is not in the tenant's Concept Registry.");
        asked.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(UsdmSamples.CdiscExampleConceptCodes);
    }

    /// <summary>The registry overload resolves each code as the tenant's current version of that CDISC Biomedical Concept.</summary>
    [Fact(DisplayName = "Import_ThroughTheConceptRegistry_UsesTheTenantsCurrentVersion")]
    public async Task ImportThroughTheConceptRegistryUsesTheTenantsCurrentVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var concept = new ConceptRef(ConceptId.New(), 3);
        var registry = Substitute.For<IConceptRegistry>();
        registry.FindBiomedicalConceptAsync(tenant, "C25298", Arg.Any<CancellationToken>()).Returns(View(concept));

        var result = await UsdmDesignImporter.ImportAsync(Mini, registry, tenant, cancellationToken: ct);

        result.Design!.CollectionSpecs[0].Items[0].Concept.Should().Be(concept);
        (await UsdmDesignImporter.ImportAsync(Mini, registry, TenantId.New(), cancellationToken: ct)).Problems
            .Should().Equal("BC_1 (Systolic Blood Pressure, CDISC C25298) is not in the tenant's Concept Registry.");
    }

    /// <summary>A document that is not JSON, not USDM 4, or has no study is refused before anything is mapped.</summary>
    [Theory(DisplayName = "Import_RefusesDocumentsThatAreNotUsdm4")]
    [InlineData("{ not json", "The document is not JSON")]
    [InlineData("[]", "The document is not a USDM Wrapper object.")]
    [InlineData("""{ "usdmVersion": "3.0.0", "study": {} }""", "usdmVersion '3.0.0' is not supported; the importer reads USDM 4.x.")]
    [InlineData("""{ "study": {} }""", "usdmVersion '' is not supported; the importer reads USDM 4.x.")]
    [InlineData("""{ "usdmVersion": "4.0.0" }""", "The document has no study.")]
    public async Task ImportRefusesDocumentsThatAreNotUsdm4(string json, string problem)
    {
        var result = await ImportAsync(json);

        result.IsSuccess.Should().BeFalse();
        result.Problems.Should().ContainSingle().Which.Should().StartWith(problem);
    }

    /// <summary>With several study versions the caller names one; an id that is not there is refused with the ids that are.</summary>
    [Fact(DisplayName = "Import_SeveralVersions_NeedsTheVersionNamed")]
    public async Task ImportSeveralVersionsNeedsTheVersionNamed()
    {
        var root = JsonNode.Parse(Mini)!;
        var versions = root["study"]!["versions"]!.AsArray();
        var second = versions[0]!.DeepClone();
        second["id"] = "StudyVersion_2";
        second["titles"]![1]!["text"] = "Amended title";
        versions.Add(second);
        var json = root.ToJsonString();

        (await ImportAsync(json)).Problems.Should().Equal("The document has 2 study versions (StudyVersion_1, StudyVersion_2); name the one to import.");
        (await ImportAsync(json, new UsdmImportOptions { StudyVersionId = "StudyVersion_9" })).Problems
            .Should().Equal("There is no study version 'StudyVersion_9' (found: StudyVersion_1, StudyVersion_2).");
        (await ImportAsync(json, new UsdmImportOptions { StudyVersionId = "StudyVersion_2" })).Design!.Title.Should().Be("Amended title");
    }

    /// <summary>Elements the design cannot hold are problems, all reported at once.</summary>
    [Fact(DisplayName = "Import_ReportsEveryProblem")]
    public async Task ImportReportsEveryProblem()
    {
        var json = Edit(Mini, design =>
        {
            design["objectives"]![0]!["level"] = new JsonObject { ["code"] = "C1", ["decode"] = "Tertiary Objective" };
            design["eligibilityCriteria"]![0]!["criterionItemId"] = "ECI_9";
            design["activities"]![0]!["biomedicalConceptIds"] = new JsonArray("BC_9");
        });

        var result = await ImportAsync(json);

        result.Problems.Should().BeEquivalentTo(
            "Objective_1: level 'Tertiary Objective' is not primary, secondary or exploratory.",
            "EC_1: criterion item 'ECI_9' is not in the study version.",
            "Activity_1: Biomedical Concept 'BC_9' is not in the study version.");
    }

    /// <summary>ISO 8601 durations read as whole days; years and months have no fixed length and are refused.</summary>
    [Theory(DisplayName = "UsdmDuration_ReadsWholeDaysAndFlagsShorterParts")]
    [InlineData("P1D", 1, false)]
    [InlineData("P2W", 14, false)]
    [InlineData("P1W3D", 10, false)]
    [InlineData("PT15M", 0, true)]
    [InlineData("PT24H", 1, false)]
    [InlineData("P1DT36H", 2, true)]
    public void UsdmDurationReadsWholeDaysAndFlagsShorterParts(string value, int days, bool hasTimePart) =>
        UsdmDuration.Parse(value).Should().Be(new UsdmDuration(days, hasTimePart));

    /// <summary>Durations with years or months, and text that is not a duration, do not read.</summary>
    [Theory(DisplayName = "UsdmDuration_RefusesYearsMonthsAndNonDurations")]
    [InlineData("P1Y")]
    [InlineData("P6M")]
    [InlineData("14 days")]
    [InlineData("")]
    [InlineData(null)]
    public void UsdmDurationRefusesYearsMonthsAndNonDurations(string? value) => UsdmDuration.Parse(value).Should().BeNull();

    /// <summary>Moving across study days skips day 0: the day before day 1 is day -1.</summary>
    [Theory(DisplayName = "UsdmSchedule_Shift_SkipsDayZero")]
    [InlineData(1, 14, 15)]
    [InlineData(1, -1, -1)]
    [InlineData(1, -2, -2)]
    [InlineData(-1, 1, 1)]
    [InlineData(-2, 3, 2)]
    [InlineData(2, -3, -2)]
    [InlineData(-3, -1, -4)]
    [InlineData(5, 0, 5)]
    public void UsdmScheduleShiftSkipsDayZero(int day, int offset, int expected) => UsdmSchedule.Shift(day, offset).Should().Be(expected);

    private static Task<UsdmImportResult> ImportAsync(string json, UsdmImportOptions? options = null) =>
        UsdmDesignImporter.ImportAsync(json, (code, _) => Task.FromResult<ConceptRef?>(ConceptFor(code)), options, TestContext.Current.CancellationToken);

    /// <summary>A fixed concept reference per CDISC code, so imports are deterministic.</summary>
    private static ConceptRef ConceptFor(string code) => new(new ConceptId(Guid.Parse($"00000000-0000-7000-8000-{code[1..].PadLeft(12, '0')}")), 1);

    private static ConceptVersionView View(ConceptRef concept) => new(
        concept,
        ConceptOrigin.CdiscBiomedicalConcept,
        "C25298",
        ConceptKind.BiomedicalConcept,
        "Systolic Blood Pressure",
        "Systolic blood pressure.",
        ConceptStatus.Active,
        concept.Version,
        ConceptMappingState.Unmapped,
        DateTimeOffset.UnixEpoch);

    private static string Edit(string json, Action<JsonNode> editDesign) =>
        EditVersion(json, version => editDesign(version["studyDesigns"]![0]!));

    private static string EditVersion(string json, Action<JsonNode> editVersion)
    {
        var root = JsonNode.Parse(json)!;
        editVersion(root["study"]!["versions"]![0]!);
        return root.ToJsonString();
    }
}
