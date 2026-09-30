import { describe, it, expect } from "vitest";
import { normalizeCell, normalizeUsdm } from "../../src/stores/normalization";

// #4529: the eCRF reads the study design through normalizeUsdm, which accepts both the camelCase and the
// snake_case shape of each USDM node and exposes both spellings on the result.
describe("normalizeUsdm", () => {
  const design = {
    study_id: "STUDY-001",
    studyTitle: "A study",
    objectives: [{ id: "OBJ-1", type: "PRIMARY", description: "Efficacy" }],
    arms: [{ arm_id: "ARM-A", armName: "Active", sequence: 1 }],
    epochs: [
      {
        epochId: "EP-1",
        epoch_name: "Screening",
        sequence: 1,
        arm_id: "ARM-A",
      },
    ],
    encounters: [
      {
        encounter_id: "V1",
        encounterName: "Visit 1",
        epoch_id: "EP-1",
        sequence: 1,
      },
    ],
    rows: [
      {
        activity_id: "ACT-VS",
        activityName: "Vital signs",
        cells: [
          { encounterId: "V1", epoch_id: "EP-1", is_applicable: true },
          { activityId: "ACT-OTHER", encounter_id: "V2", isApplicable: false },
        ],
      },
    ],
  };

  it("reads camelCase and snake_case fields alike", () => {
    const usdm = normalizeUsdm(design);

    expect(usdm.studyId).toBe("STUDY-001");
    expect(usdm.studyTitle).toBe("A study");
    expect(usdm.arms[0]).toMatchObject({ arm_id: "ARM-A", arm_name: "Active" });
    expect(usdm.epochs[0]).toMatchObject({
      epoch_id: "EP-1",
      epoch_name: "Screening",
      arm_id: "ARM-A",
    });
    expect(usdm.encounters[0]).toMatchObject({
      encounter_id: "V1",
      encounter_name: "Visit 1",
      epoch_id: "EP-1",
    });
    expect(usdm.rows[0]).toMatchObject({
      activity_id: "ACT-VS",
      activity_name: "Vital signs",
    });
  });

  it("gives each cell its row's activity unless the cell names its own", () => {
    const [row] = normalizeUsdm(design).rows;

    expect(row.cells.map((c) => c.activity_id)).toEqual([
      "ACT-VS",
      "ACT-OTHER",
    ]);
    expect(row.cells.map((c) => c.is_applicable)).toEqual([true, false]);
  });

  it("exposes camelCase aliases that read and write the snake_case field, and serializes snake_case only", () => {
    const [epoch] = normalizeUsdm(design).epochs;

    expect(epoch.epochId).toBe("EP-1");
    epoch.epochName = "Run-in";
    expect(epoch.epoch_name).toBe("Run-in");
    expect(Object.keys(JSON.parse(JSON.stringify(epoch))).sort()).toEqual([
      "arm_id",
      "epoch_id",
      "epoch_name",
      "sequence",
    ]);
  });

  it("tolerates missing and malformed collections without throwing", () => {
    const usdm = normalizeUsdm({
      study_id: "STUDY-002",
      epochs: "nope",
      rows: [null, 7],
    });

    expect(usdm.epochs).toEqual([]);
    expect(usdm.rows).toHaveLength(2);
    expect(usdm.rows[0]).toMatchObject({ activity_id: "", cells: [] });
    expect(usdm.arms).toEqual([]);
  });

  it("defaults booleans to false and a missing arm to null", () => {
    const cell = normalizeCell({ encounter_id: "V1" }, "ACT-1");

    expect(cell).toMatchObject({
      activity_id: "ACT-1",
      is_applicable: false,
      derived_from_soa: false,
      arm_id: null,
      details: "",
    });
  });
});
