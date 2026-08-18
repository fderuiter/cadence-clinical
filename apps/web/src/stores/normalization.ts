export interface NormalizedEpoch {
  epochId: string;
  epochName: string;
  sequence: number;
  armId: string | null;
  epoch_id: string;
  epoch_name: string;
  arm_id: string | null;
}

export interface NormalizedEncounter {
  encounterId: string;
  encounterName: string;
  epochId: string;
  sequence: number;
  armId: string | null;
  encounter_id: string;
  encounter_name: string;
  epoch_id: string;
  arm_id: string | null;
}

export interface NormalizedArm {
  armId: string;
  armName: string;
  sequence: number;
  arm_id: string;
  arm_name: string;
}

export interface NormalizedCell {
  activityId: string;
  encounterId: string;
  epochId: string;
  isApplicable: boolean;
  details: string;
  armId: string | null;
  derivedFromSoa: boolean;
  activity_id: string;
  encounter_id: string;
  epoch_id: string;
  is_applicable: boolean;
  arm_id: string | null;
  derived_from_soa: boolean;
}

export interface NormalizedRow {
  activityId: string;
  activityName: string;
  cells: NormalizedCell[];
  derivedFromSoa: boolean;
  activity_id: string;
  activity_name: string;
  derived_from_soa: boolean;
}

export interface StudyObjective {
  id: string;
  type: string;
  description: string;
}

export interface NormalizedUsdm {
  studyId: string;
  studyTitle: string;
  objectives: StudyObjective[];
  epochs: NormalizedEpoch[];
  encounters: NormalizedEncounter[];
  arms: NormalizedArm[];
  rows: NormalizedRow[];
  forms: Record<string, unknown>[];
  crossovers: Record<string, unknown>[];
}

export function normalizeEpoch(
  ep: Record<string, unknown> | null | undefined
): NormalizedEpoch {
  const data = ep || {};
  const epoch_id =
    typeof data.epochId === "string"
      ? data.epochId
      : typeof data.epoch_id === "string"
      ? data.epoch_id
      : "";
  const epoch_name =
    typeof data.epochName === "string"
      ? data.epochName
      : typeof data.epoch_name === "string"
      ? data.epoch_name
      : "";
  const sequence = typeof data.sequence === "number" ? data.sequence : 0;
  const rawArmId = data.armId ?? data.arm_id;
  const arm_id = typeof rawArmId === "string" ? rawArmId : null;

  const result = {
    epoch_id,
    epoch_name,
    sequence,
    arm_id,
  } as NormalizedEpoch;

  Object.defineProperties(result, {
    epochId: {
      get(this: NormalizedEpoch) {
        return this.epoch_id;
      },
      set(this: NormalizedEpoch, v: string) {
        this.epoch_id = v;
      },
      enumerable: false,
      configurable: true,
    },
    epochName: {
      get(this: NormalizedEpoch) {
        return this.epoch_name;
      },
      set(this: NormalizedEpoch, v: string) {
        this.epoch_name = v;
      },
      enumerable: false,
      configurable: true,
    },
    armId: {
      get(this: NormalizedEpoch) {
        return this.arm_id;
      },
      set(this: NormalizedEpoch, v: string | null) {
        this.arm_id = v;
      },
      enumerable: false,
      configurable: true,
    },
  });

  return result;
}

export function normalizeEncounter(
  enc: Record<string, unknown> | null | undefined
): NormalizedEncounter {
  const data = enc || {};
  const encounter_id =
    typeof data.encounterId === "string"
      ? data.encounterId
      : typeof data.encounter_id === "string"
      ? data.encounter_id
      : "";
  const encounter_name =
    typeof data.encounterName === "string"
      ? data.encounterName
      : typeof data.encounter_name === "string"
      ? data.encounter_name
      : "";
  const epoch_id =
    typeof data.epochId === "string"
      ? data.epochId
      : typeof data.epoch_id === "string"
      ? data.epoch_id
      : "";
  const sequence = typeof data.sequence === "number" ? data.sequence : 0;
  const rawArmId = data.armId ?? data.arm_id;
  const arm_id = typeof rawArmId === "string" ? rawArmId : null;

  const result = {
    encounter_id,
    encounter_name,
    epoch_id,
    sequence,
    arm_id,
  } as NormalizedEncounter;

  Object.defineProperties(result, {
    encounterId: {
      get(this: NormalizedEncounter) {
        return this.encounter_id;
      },
      set(this: NormalizedEncounter, v: string) {
        this.encounter_id = v;
      },
      enumerable: false,
      configurable: true,
    },
    encounterName: {
      get(this: NormalizedEncounter) {
        return this.encounter_name;
      },
      set(this: NormalizedEncounter, v: string) {
        this.encounter_name = v;
      },
      enumerable: false,
      configurable: true,
    },
    epochId: {
      get(this: NormalizedEncounter) {
        return this.epoch_id;
      },
      set(this: NormalizedEncounter, v: string) {
        this.epoch_id = v;
      },
      enumerable: false,
      configurable: true,
    },
    armId: {
      get(this: NormalizedEncounter) {
        return this.arm_id;
      },
      set(this: NormalizedEncounter, v: string | null) {
        this.arm_id = v;
      },
      enumerable: false,
      configurable: true,
    },
  });

  return result;
}

export function normalizeArm(
  arm: Record<string, unknown> | null | undefined
): NormalizedArm {
  const data = arm || {};
  const arm_id =
    typeof data.armId === "string"
      ? data.armId
      : typeof data.arm_id === "string"
      ? data.arm_id
      : "";
  const arm_name =
    typeof data.armName === "string"
      ? data.armName
      : typeof data.arm_name === "string"
      ? data.arm_name
      : "";
  const sequence = typeof data.sequence === "number" ? data.sequence : 0;

  const result = {
    arm_id,
    arm_name,
    sequence,
  } as NormalizedArm;

  Object.defineProperties(result, {
    armId: {
      get(this: NormalizedArm) {
        return this.arm_id;
      },
      set(this: NormalizedArm, v: string) {
        this.arm_id = v;
      },
      enumerable: false,
      configurable: true,
    },
    armName: {
      get(this: NormalizedArm) {
        return this.arm_name;
      },
      set(this: NormalizedArm, v: string) {
        this.arm_name = v;
      },
      enumerable: false,
      configurable: true,
    },
  });

  return result;
}

export function normalizeCell(
  cell: Record<string, unknown> | null | undefined,
  fallbackActivityId: string = ""
): NormalizedCell {
  const data = cell || {};
  const activity_id =
    typeof data.activityId === "string"
      ? data.activityId
      : typeof data.activity_id === "string"
      ? data.activity_id
      : fallbackActivityId;
  const encounter_id =
    typeof data.encounterId === "string"
      ? data.encounterId
      : typeof data.encounter_id === "string"
      ? data.encounter_id
      : "";
  const epoch_id =
    typeof data.epochId === "string"
      ? data.epochId
      : typeof data.epoch_id === "string"
      ? data.epoch_id
      : "";
  const is_applicable =
    typeof data.isApplicable === "boolean"
      ? data.isApplicable
      : typeof data.is_applicable === "boolean"
      ? data.is_applicable
      : false;
  const details = typeof data.details === "string" ? data.details : "";
  const rawArmId = data.armId ?? data.arm_id;
  const arm_id = typeof rawArmId === "string" ? rawArmId : null;
  const derived_from_soa =
    typeof data.derivedFromSoa === "boolean"
      ? data.derivedFromSoa
      : typeof data.derived_from_soa === "boolean"
      ? data.derived_from_soa
      : false;

  const result = {
    activity_id,
    encounter_id,
    epoch_id,
    is_applicable,
    details,
    arm_id,
    derived_from_soa,
  } as NormalizedCell;

  Object.defineProperties(result, {
    activityId: {
      get(this: NormalizedCell) {
        return this.activity_id;
      },
      set(this: NormalizedCell, v: string) {
        this.activity_id = v;
      },
      enumerable: false,
      configurable: true,
    },
    encounterId: {
      get(this: NormalizedCell) {
        return this.encounter_id;
      },
      set(this: NormalizedCell, v: string) {
        this.encounter_id = v;
      },
      enumerable: false,
      configurable: true,
    },
    epochId: {
      get(this: NormalizedCell) {
        return this.epoch_id;
      },
      set(this: NormalizedCell, v: string) {
        this.epoch_id = v;
      },
      enumerable: false,
      configurable: true,
    },
    isApplicable: {
      get(this: NormalizedCell) {
        return this.is_applicable;
      },
      set(this: NormalizedCell, v: boolean) {
        this.is_applicable = v;
      },
      enumerable: false,
      configurable: true,
    },
    armId: {
      get(this: NormalizedCell) {
        return this.arm_id;
      },
      set(this: NormalizedCell, v: string | null) {
        this.arm_id = v;
      },
      enumerable: false,
      configurable: true,
    },
    derivedFromSoa: {
      get(this: NormalizedCell) {
        return this.derived_from_soa;
      },
      set(this: NormalizedCell, v: boolean) {
        this.derived_from_soa = v;
      },
      enumerable: false,
      configurable: true,
    },
  });

  return result;
}

export function normalizeRow(
  row: Record<string, unknown> | null | undefined
): NormalizedRow {
  const data = row || {};
  const activity_id =
    typeof data.activityId === "string"
      ? data.activityId
      : typeof data.activity_id === "string"
      ? data.activity_id
      : "";
  const activity_name =
    typeof data.activityName === "string"
      ? data.activityName
      : typeof data.activity_name === "string"
      ? data.activity_name
      : "";
  const derived_from_soa =
    typeof data.derivedFromSoa === "boolean"
      ? data.derivedFromSoa
      : typeof data.derived_from_soa === "boolean"
      ? data.derived_from_soa
      : false;

  const rawCells = Array.isArray(data.cells) ? data.cells : [];
  const cells: NormalizedCell[] = rawCells.map((c) =>
    normalizeCell(
      typeof c === "object" && c !== null
        ? (c as Record<string, unknown>)
        : undefined,
      activity_id
    )
  );

  const result = {
    activity_id,
    activity_name,
    cells,
    derived_from_soa,
  } as NormalizedRow;

  Object.defineProperties(result, {
    activityId: {
      get(this: NormalizedRow) {
        return this.activity_id;
      },
      set(this: NormalizedRow, v: string) {
        this.activity_id = v;
      },
      enumerable: false,
      configurable: true,
    },
    activityName: {
      get(this: NormalizedRow) {
        return this.activity_name;
      },
      set(this: NormalizedRow, v: string) {
        this.activity_name = v;
      },
      enumerable: false,
      configurable: true,
    },
    derivedFromSoa: {
      get(this: NormalizedRow) {
        return this.derived_from_soa;
      },
      set(this: NormalizedRow, v: boolean) {
        this.derived_from_soa = v;
      },
      enumerable: false,
      configurable: true,
    },
  });

  return result;
}

export function normalizeUsdm(rawData: unknown): NormalizedUsdm {
  const data = (
    typeof rawData === "object" && rawData !== null ? rawData : {}
  ) as Record<string, unknown>;

  const rawStudyId = data.studyId ?? data.study_id;
  const studyId =
    typeof rawStudyId === "string" && rawStudyId.length > 0
      ? rawStudyId
      : "STUDY-USDM-001";

  const rawStudyTitle = data.studyTitle ?? data.study_title;
  const studyTitle = typeof rawStudyTitle === "string" ? rawStudyTitle : "";

  const rawObjectives = Array.isArray(data.objectives) ? data.objectives : [];
  const objectives: StudyObjective[] = rawObjectives.map((obj) => {
    const o = (
      typeof obj === "object" && obj !== null ? obj : {}
    ) as Record<string, unknown>;
    return {
      id: typeof o.id === "string" ? o.id : "",
      type: typeof o.type === "string" ? o.type : "",
      description: typeof o.description === "string" ? o.description : "",
    };
  });

  const rawEpochs = Array.isArray(data.epochs) ? data.epochs : [];
  const epochs: NormalizedEpoch[] = rawEpochs.map((ep) =>
    normalizeEpoch(
      typeof ep === "object" && ep !== null
        ? (ep as Record<string, unknown>)
        : undefined
    )
  );

  const rawEncounters = Array.isArray(data.encounters) ? data.encounters : [];
  const encounters: NormalizedEncounter[] = rawEncounters.map((enc) =>
    normalizeEncounter(
      typeof enc === "object" && enc !== null
        ? (enc as Record<string, unknown>)
        : undefined
    )
  );

  const rawArms = Array.isArray(data.arms) ? data.arms : [];
  const arms: NormalizedArm[] = rawArms.map((arm) =>
    normalizeArm(
      typeof arm === "object" && arm !== null
        ? (arm as Record<string, unknown>)
        : undefined
    )
  );

  const rawRows = Array.isArray(data.rows) ? data.rows : [];
  const rows: NormalizedRow[] = rawRows.map((row) =>
    normalizeRow(
      typeof row === "object" && row !== null
        ? (row as Record<string, unknown>)
        : undefined
    )
  );

  const rawForms = Array.isArray(data.forms) ? data.forms : [];
  const forms: Record<string, unknown>[] = rawForms.map((f) =>
    typeof f === "object" && f !== null ? (f as Record<string, unknown>) : {}
  );

  const rawCrossovers = Array.isArray(data.crossovers) ? data.crossovers : [];
  const crossovers: Record<string, unknown>[] = rawCrossovers.map((c) =>
    typeof c === "object" && c !== null ? (c as Record<string, unknown>) : {}
  );

  return {
    studyId,
    studyTitle,
    objectives,
    epochs,
    encounters,
    arms,
    rows,
    forms,
    crossovers,
  };
}
