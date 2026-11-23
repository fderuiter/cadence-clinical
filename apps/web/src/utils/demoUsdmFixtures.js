/**
 * Canonical USDM Schedule-of-Activities demo fixtures shared between
 * `stores/clinical.ts` (`DEFAULT_CLINICAL_USDM`) and `mocks/demoHandlers.js`.
 */
export const DEMO_USDM_SOA = Object.freeze({
  arms: Object.freeze([
    Object.freeze({ arm_id: "ARM-A", arm_name: "Arm A: Active 10mg daily" }),
    Object.freeze({ arm_id: "ARM-B", arm_name: "Arm B: Placebo Control" }),
  ]),
  epochs: Object.freeze([
    Object.freeze({ epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 }),
    Object.freeze({
      epoch_id: "EP-TRT-A",
      epoch_name: "Treatment Phase",
      sequence: 2,
      arm_id: "ARM-A",
    }),
    Object.freeze({
      epoch_id: "EP-TRT-B",
      epoch_name: "Treatment Phase",
      sequence: 2,
      arm_id: "ARM-B",
    }),
  ]),
  encounters: Object.freeze([
    Object.freeze({
      encounter_id: "V-SCR",
      encounter_name: "Day -7 to -1",
      epoch_id: "EP-SCR",
      sequence: 1,
    }),
    Object.freeze({
      encounter_id: "V-TRT-A1",
      encounter_name: "Week 2",
      epoch_id: "EP-TRT-A",
      sequence: 2,
    }),
    Object.freeze({
      encounter_id: "V-TRT-A2",
      encounter_name: "Week 4",
      epoch_id: "EP-TRT-A",
      sequence: 3,
    }),
    Object.freeze({
      encounter_id: "V-TRT-B1",
      encounter_name: "Week 2",
      epoch_id: "EP-TRT-B",
      sequence: 4,
    }),
  ]),
  rows: Object.freeze([
    Object.freeze({
      activity_id: "ACT-DEM",
      activity_name: "Informed Consent & Demographics",
      cells: Object.freeze([
        Object.freeze({
          encounter_id: "V-SCR",
          is_applicable: true,
          details: "Mandatory",
        }),
        Object.freeze({ encounter_id: "V-TRT-A1", is_applicable: false }),
        Object.freeze({ encounter_id: "V-TRT-A2", is_applicable: false }),
        Object.freeze({ encounter_id: "V-TRT-B1", is_applicable: false }),
      ]),
    }),
    Object.freeze({
      activity_id: "ACT-VS",
      activity_name: "Vital Signs (BP & Pulse)",
      cells: Object.freeze([
        Object.freeze({
          encounter_id: "V-SCR",
          is_applicable: true,
          details: "Day -7",
        }),
        Object.freeze({
          encounter_id: "V-TRT-A1",
          is_applicable: true,
          details: "Within 10 mins",
        }),
        Object.freeze({
          encounter_id: "V-TRT-A2",
          is_applicable: true,
          details: "Conditional",
        }),
        Object.freeze({
          encounter_id: "V-TRT-B1",
          is_applicable: true,
          details: "Within 10 mins",
        }),
      ]),
    }),
    Object.freeze({
      activity_id: "ACT-AE",
      activity_name: "Adverse Events Check",
      cells: Object.freeze([
        Object.freeze({ encounter_id: "V-SCR", is_applicable: false }),
        Object.freeze({
          encounter_id: "V-TRT-A1",
          is_applicable: true,
          details: "Continuous",
        }),
        Object.freeze({
          encounter_id: "V-TRT-A2",
          is_applicable: true,
          details: "Continuous",
        }),
        Object.freeze({
          encounter_id: "V-TRT-B1",
          is_applicable: true,
          details: "Optional",
        }),
      ]),
    }),
    Object.freeze({
      activity_id: "ACT-MED",
      activity_name: "Study Medication Log",
      cells: Object.freeze([
        Object.freeze({ encounter_id: "V-SCR", is_applicable: false }),
        Object.freeze({
          encounter_id: "V-TRT-A1",
          is_applicable: true,
          details: "Daily entry",
        }),
        Object.freeze({
          encounter_id: "V-TRT-A2",
          is_applicable: true,
          details: "Daily entry",
        }),
        Object.freeze({
          encounter_id: "V-TRT-B1",
          is_applicable: true,
          details: "Daily entry",
        }),
      ]),
    }),
  ]),
});

/**
 * Returns a fresh mutable clone of the demo USDM SoA fixture for stateful demo projections.
 *
 * @param {{ maxRows?: number }} [options]
 */
export function cloneDemoUsdmSoa(options = {}) {
  const sourceRows =
    typeof options.maxRows === "number"
      ? DEMO_USDM_SOA.rows.slice(0, options.maxRows)
      : DEMO_USDM_SOA.rows;
  return {
    arms: DEMO_USDM_SOA.arms.map((arm) => ({ ...arm })),
    epochs: DEMO_USDM_SOA.epochs.map((epoch) => ({ ...epoch })),
    encounters: DEMO_USDM_SOA.encounters.map((enc) => ({ ...enc })),
    rows: sourceRows.map((row) => ({
      activity_id: row.activity_id,
      activity_name: row.activity_name,
      cells: row.cells.map((cell) => ({ ...cell })),
    })),
  };
}
