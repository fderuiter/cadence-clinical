export interface UsdmScheduleArm {
  arm_id: string;
  arm_name: string;
}

export interface UsdmScheduleEpoch {
  epoch_id: string;
  epoch_name: string;
  sequence: number;
  arm_id?: string;
}

export interface UsdmScheduleEncounter {
  encounter_id: string;
  encounter_name: string;
  epoch_id: string;
  sequence: number;
}

export interface UsdmScheduleCell {
  encounter_id: string;
  is_applicable: boolean;
  details?: string;
}

export interface UsdmScheduleRow {
  activity_id: string;
  activity_name: string;
  cells: UsdmScheduleCell[];
}

export interface UsdmSoaSchedule {
  arms: UsdmScheduleArm[];
  epochs: UsdmScheduleEpoch[];
  encounters: UsdmScheduleEncounter[];
  rows: UsdmScheduleRow[];
}

const CANONICAL_ARMS: readonly UsdmScheduleArm[] = [
  { arm_id: "ARM-A", arm_name: "Arm A: Active 10mg daily" },
  { arm_id: "ARM-B", arm_name: "Arm B: Placebo Control" },
];

const CANONICAL_EPOCHS: readonly UsdmScheduleEpoch[] = [
  { epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 },
  {
    epoch_id: "EP-TRT-A",
    epoch_name: "Treatment Phase",
    sequence: 2,
    arm_id: "ARM-A",
  },
  {
    epoch_id: "EP-TRT-B",
    epoch_name: "Treatment Phase",
    sequence: 2,
    arm_id: "ARM-B",
  },
];

const CANONICAL_ENCOUNTERS: readonly UsdmScheduleEncounter[] = [
  {
    encounter_id: "V-SCR",
    encounter_name: "Day -7 to -1",
    epoch_id: "EP-SCR",
    sequence: 1,
  },
  {
    encounter_id: "V-TRT-A1",
    encounter_name: "Week 2",
    epoch_id: "EP-TRT-A",
    sequence: 2,
  },
  {
    encounter_id: "V-TRT-A2",
    encounter_name: "Week 4",
    epoch_id: "EP-TRT-A",
    sequence: 3,
  },
  {
    encounter_id: "V-TRT-B1",
    encounter_name: "Week 2",
    epoch_id: "EP-TRT-B",
    sequence: 4,
  },
];

const CANONICAL_ROWS: readonly UsdmScheduleRow[] = [
  {
    activity_id: "ACT-DEM",
    activity_name: "Informed Consent & Demographics",
    cells: [
      {
        encounter_id: "V-SCR",
        is_applicable: true,
        details: "Mandatory",
      },
      { encounter_id: "V-TRT-A1", is_applicable: false },
      { encounter_id: "V-TRT-A2", is_applicable: false },
      { encounter_id: "V-TRT-B1", is_applicable: false },
    ],
  },
  {
    activity_id: "ACT-VS",
    activity_name: "Vital Signs (BP & Pulse)",
    cells: [
      {
        encounter_id: "V-SCR",
        is_applicable: true,
        details: "Day -7",
      },
      {
        encounter_id: "V-TRT-A1",
        is_applicable: true,
        details: "Within 10 mins",
      },
      {
        encounter_id: "V-TRT-A2",
        is_applicable: true,
        details: "Conditional",
      },
      {
        encounter_id: "V-TRT-B1",
        is_applicable: true,
        details: "Within 10 mins",
      },
    ],
  },
  {
    activity_id: "ACT-AE",
    activity_name: "Adverse Events Check",
    cells: [
      { encounter_id: "V-SCR", is_applicable: false },
      {
        encounter_id: "V-TRT-A1",
        is_applicable: true,
        details: "Continuous",
      },
      {
        encounter_id: "V-TRT-A2",
        is_applicable: true,
        details: "Continuous",
      },
      {
        encounter_id: "V-TRT-B1",
        is_applicable: true,
        details: "Optional",
      },
    ],
  },
  {
    activity_id: "ACT-MED",
    activity_name: "Study Medication Log",
    cells: [
      { encounter_id: "V-SCR", is_applicable: false },
      {
        encounter_id: "V-TRT-A1",
        is_applicable: true,
        details: "Daily entry",
      },
      {
        encounter_id: "V-TRT-A2",
        is_applicable: true,
        details: "Daily entry",
      },
      {
        encounter_id: "V-TRT-B1",
        is_applicable: true,
        details: "Daily entry",
      },
    ],
  },
];

export function buildUsdmSoaSchedule(
  activityIds?: readonly string[]
): UsdmSoaSchedule {
  const selectedRows = activityIds
    ? CANONICAL_ROWS.filter((row) => activityIds.includes(row.activity_id))
    : CANONICAL_ROWS;

  return {
    arms: CANONICAL_ARMS.map((arm) => ({ ...arm })),
    epochs: CANONICAL_EPOCHS.map((epoch) => ({ ...epoch })),
    encounters: CANONICAL_ENCOUNTERS.map((encounter) => ({ ...encounter })),
    rows: selectedRows.map((row) => ({
      activity_id: row.activity_id,
      activity_name: row.activity_name,
      cells: row.cells.map((cell) => ({ ...cell })),
    })),
  };
}
