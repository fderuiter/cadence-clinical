import { DEMO_STUDY } from "demo-fixtures";
import { isDemoBuild } from "../utils/demoMode.js";

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

function extractRawUsdmEnvelope(
  data: Record<string, unknown>
): Record<string, unknown> {
  const wrapperStudy =
    typeof data.study === "object" && data.study !== null
      ? (data.study as Record<string, unknown>)
      : null;
  if (wrapperStudy) {
    const versions = Array.isArray(wrapperStudy.versions)
      ? wrapperStudy.versions
      : [];
    const version = (
      typeof versions[0] === "object" && versions[0] !== null ? versions[0] : {}
    ) as Record<string, unknown>;
    const studyDesigns = Array.isArray(version.studyDesigns)
      ? version.studyDesigns
      : [];
    const design = (
      typeof studyDesigns[0] === "object" && studyDesigns[0] !== null
        ? studyDesigns[0]
        : {}
    ) as Record<string, unknown>;
    const titles = Array.isArray(version.titles) ? version.titles : [];
    const officialTitleObj = titles.find((t) => {
      if (!t || typeof t !== "object") return false;
      const typeObj = (t as Record<string, unknown>).type as
        Record<string, unknown> | undefined;
      const decode =
        typeof typeObj?.decode === "string"
          ? typeObj.decode
          : typeof (
                typeObj?.standardCode as Record<string, unknown> | undefined
              )?.decode === "string"
            ? ((typeObj?.standardCode as Record<string, unknown>)
                .decode as string)
            : "";
      return decode.toLowerCase().includes("official");
    }) as Record<string, unknown> | undefined;
    const fallbackTitleObj =
      typeof titles[0] === "object" && titles[0] !== null
        ? (titles[0] as Record<string, unknown>)
        : undefined;
    const resolvedTitle =
      (typeof officialTitleObj?.text === "string"
        ? officialTitleObj.text
        : "") ||
      (typeof fallbackTitleObj?.text === "string"
        ? fallbackTitleObj.text
        : "") ||
      (typeof wrapperStudy.label === "string" ? wrapperStudy.label : "") ||
      (typeof wrapperStudy.name === "string" ? wrapperStudy.name : "");

    const rawEncounters = Array.isArray(design.encounters)
      ? design.encounters
      : [];
    const rawEpochs = Array.isArray(design.epochs) ? design.epochs : [];
    const rawArms = Array.isArray(design.arms) ? design.arms : [];
    const rawActivities = Array.isArray(design.activities)
      ? design.activities
      : [];
    const timelines = Array.isArray(design.scheduleTimelines)
      ? design.scheduleTimelines
      : [];
    const mainTimeline = (timelines.find(
      (tl) =>
        typeof tl === "object" &&
        tl !== null &&
        (tl as Record<string, unknown>).mainTimeline === true
    ) ||
      timelines[0] ||
      {}) as Record<string, unknown>;
    const instances = Array.isArray(mainTimeline.instances)
      ? mainTimeline.instances
      : [];

    const encounterEpochMap = new Map<string, string>();
    const activityEncountersMap = new Map<string, Set<string>>();
    for (const inst of instances) {
      if (!inst || typeof inst !== "object") continue;
      const sai = inst as Record<string, unknown>;
      const encId = typeof sai.encounterId === "string" ? sai.encounterId : "";
      const epId = typeof sai.epochId === "string" ? sai.epochId : "";
      if (encId && epId && !encounterEpochMap.has(encId)) {
        encounterEpochMap.set(encId, epId);
      }
      const actIds = Array.isArray(sai.activityIds) ? sai.activityIds : [];
      for (const aId of actIds) {
        if (typeof aId === "string" && encId) {
          if (!activityEncountersMap.has(aId)) {
            activityEncountersMap.set(aId, new Set());
          }
          activityEncountersMap.get(aId)!.add(encId);
        }
      }
    }

    const mappedEpochs = rawEpochs.map((ep, idx) => {
      const e = (typeof ep === "object" && ep !== null ? ep : {}) as Record<
        string,
        unknown
      >;
      return {
        epoch_id:
          (typeof e.epoch_id === "string" ? e.epoch_id : "") ||
          (typeof e.id === "string" ? e.id : `EP-${idx + 1}`),
        epoch_name:
          (typeof e.epoch_name === "string" ? e.epoch_name : "") ||
          (typeof e.label === "string" && e.label ? e.label : "") ||
          (typeof e.name === "string" ? e.name : `Epoch ${idx + 1}`),
        sequence:
          typeof e.sequence === "number"
            ? e.sequence
            : typeof e.sequenceNumber === "number"
              ? e.sequenceNumber
              : idx + 1,
      };
    });

    const mappedEncounters = rawEncounters.map((enc, idx) => {
      const en = (typeof enc === "object" && enc !== null ? enc : {}) as Record<
        string,
        unknown
      >;
      const encId =
        (typeof en.encounter_id === "string" ? en.encounter_id : "") ||
        (typeof en.id === "string" ? en.id : `V-${idx + 1}`);
      return {
        encounter_id: encId,
        encounter_name:
          (typeof en.encounter_name === "string" ? en.encounter_name : "") ||
          (typeof en.label === "string" && en.label ? en.label : "") ||
          (typeof en.name === "string" ? en.name : `Visit ${idx + 1}`),
        epoch_id:
          (typeof en.epoch_id === "string" ? en.epoch_id : "") ||
          (typeof en.epochId === "string" ? en.epochId : "") ||
          encounterEpochMap.get(encId) ||
          mappedEpochs[0]?.epoch_id ||
          "",
        sequence: typeof en.sequence === "number" ? en.sequence : idx + 1,
      };
    });

    const mappedArms = rawArms.map((arm, idx) => {
      const a = (typeof arm === "object" && arm !== null ? arm : {}) as Record<
        string,
        unknown
      >;
      return {
        arm_id:
          (typeof a.arm_id === "string" ? a.arm_id : "") ||
          (typeof a.id === "string" ? a.id : `ARM-${idx + 1}`),
        arm_name:
          (typeof a.arm_name === "string" ? a.arm_name : "") ||
          (typeof a.label === "string" && a.label ? a.label : "") ||
          (typeof a.name === "string" ? a.name : `Arm ${idx + 1}`),
        sequence: typeof a.sequence === "number" ? a.sequence : idx + 1,
      };
    });

    const mappedRows = rawActivities.map((act, idx) => {
      const ac = (typeof act === "object" && act !== null ? act : {}) as Record<
        string,
        unknown
      >;
      const actId =
        (typeof ac.activity_id === "string" ? ac.activity_id : "") ||
        (typeof ac.id === "string" ? ac.id : `ACT-${idx + 1}`);
      const scheduledEncs = activityEncountersMap.get(actId);
      const assignedEncs = Array.isArray(ac.assignedEncounterIds)
        ? new Set(ac.assignedEncounterIds.filter((x) => typeof x === "string"))
        : null;
      const cells = Array.isArray(ac.cells)
        ? ac.cells
        : mappedEncounters.map((en) => ({
            activity_id: actId,
            encounter_id: en.encounter_id,
            epoch_id: en.epoch_id,
            is_applicable: scheduledEncs
              ? scheduledEncs.has(en.encounter_id)
              : assignedEncs
                ? assignedEncs.has(en.encounter_id)
                : true,
          }));
      return {
        activity_id: actId,
        activity_name:
          (typeof ac.activity_name === "string" ? ac.activity_name : "") ||
          (typeof ac.label === "string" && ac.label ? ac.label : "") ||
          (typeof ac.name === "string" ? ac.name : `Activity ${idx + 1}`),
        cells,
      };
    });

    const rawDesignObjectives = Array.isArray(design.objectives)
      ? design.objectives
      : [];
    const mappedObjectives = rawDesignObjectives.map((obj, idx) => {
      const o = (typeof obj === "object" && obj !== null ? obj : {}) as Record<
        string,
        unknown
      >;
      const levelObj = o.level as Record<string, unknown> | undefined;
      return {
        id: typeof o.id === "string" ? o.id : `OBJ-${idx + 1}`,
        type:
          (typeof o.type === "string" ? o.type : "") ||
          (typeof levelObj?.decode === "string" ? levelObj.decode : "Primary"),
        description:
          (typeof o.description === "string" && o.description
            ? o.description
            : "") || (typeof o.text === "string" ? o.text : ""),
      };
    });

    return {
      studyId:
        (typeof wrapperStudy.id === "string" && wrapperStudy.id
          ? wrapperStudy.id
          : "") ||
        (typeof wrapperStudy.name === "string" ? wrapperStudy.name : ""),
      studyTitle: resolvedTitle,
      objectives: mappedObjectives,
      epochs: mappedEpochs,
      encounters: mappedEncounters,
      arms: mappedArms,
      rows: mappedRows,
      forms: Array.isArray(data.forms) ? data.forms : [],
      crossovers: Array.isArray(data.crossovers) ? data.crossovers : [],
    };
  }

  if (
    !Array.isArray(data.rows) &&
    ((Array.isArray(data.studyDesigns) && data.studyDesigns.length > 0) ||
      typeof data.id === "string" ||
      Array.isArray(data.activities))
  ) {
    const design = (
      Array.isArray(data.studyDesigns) &&
      data.studyDesigns.length > 0 &&
      typeof data.studyDesigns[0] === "object" &&
      data.studyDesigns[0] !== null
        ? data.studyDesigns[0]
        : data
    ) as Record<string, unknown>;
    const rawEpochs = Array.isArray(design.epochs) ? design.epochs : [];
    const rawEncounters = Array.isArray(design.encounters)
      ? design.encounters
      : [];
    const rawArms = Array.isArray(design.arms) ? design.arms : [];
    const rawActivities = Array.isArray(design.activities)
      ? design.activities
      : [];

    const mappedEpochs = rawEpochs.map((ep, idx) => {
      const e = (typeof ep === "object" && ep !== null ? ep : {}) as Record<
        string,
        unknown
      >;
      return {
        epoch_id:
          (typeof e.epoch_id === "string" ? e.epoch_id : "") ||
          (typeof e.id === "string" ? e.id : `EP-${idx + 1}`),
        epoch_name:
          (typeof e.epoch_name === "string" ? e.epoch_name : "") ||
          (typeof e.name === "string" ? e.name : `Epoch ${idx + 1}`),
        sequence:
          typeof e.sequenceNumber === "number" ? e.sequenceNumber : idx + 1,
      };
    });

    const mappedEncounters = rawEncounters.map((enc, idx) => {
      const en = (typeof enc === "object" && enc !== null ? enc : {}) as Record<
        string,
        unknown
      >;
      return {
        encounter_id:
          (typeof en.encounter_id === "string" ? en.encounter_id : "") ||
          (typeof en.id === "string" ? en.id : `V-${idx + 1}`),
        encounter_name:
          (typeof en.encounter_name === "string" ? en.encounter_name : "") ||
          (typeof en.name === "string" ? en.name : `Visit ${idx + 1}`),
        epoch_id:
          (typeof en.epochId === "string" ? en.epochId : "") ||
          mappedEpochs[0]?.epoch_id ||
          "",
        sequence: idx + 1,
      };
    });

    const mappedArms = rawArms.map((arm, idx) => {
      const a = (typeof arm === "object" && arm !== null ? arm : {}) as Record<
        string,
        unknown
      >;
      return {
        arm_id:
          (typeof a.arm_id === "string" ? a.arm_id : "") ||
          (typeof a.id === "string" ? a.id : `ARM-${idx + 1}`),
        arm_name:
          (typeof a.arm_name === "string" ? a.arm_name : "") ||
          (typeof a.name === "string" ? a.name : `Arm ${idx + 1}`),
        sequence: idx + 1,
      };
    });

    const mappedRows = rawActivities.map((act, idx) => {
      const ac = (typeof act === "object" && act !== null ? act : {}) as Record<
        string,
        unknown
      >;
      const actId = typeof ac.id === "string" ? ac.id : `ACT-${idx + 1}`;
      const assignedEncs = Array.isArray(ac.assignedEncounterIds)
        ? new Set(ac.assignedEncounterIds.filter((x) => typeof x === "string"))
        : null;
      return {
        activity_id: actId,
        activity_name:
          typeof ac.name === "string" ? ac.name : `Activity ${idx + 1}`,
        cells: mappedEncounters.map((en) => ({
          activity_id: actId,
          encounter_id: en.encounter_id,
          epoch_id: en.epoch_id,
          is_applicable: assignedEncs
            ? assignedEncs.has(en.encounter_id)
            : true,
        })),
      };
    });

    return {
      studyId:
        (typeof data.studyId === "string" ? data.studyId : "") ||
        (typeof data.id === "string" ? data.id : "") ||
        (typeof data.name === "string" ? data.name : ""),
      studyTitle:
        (typeof data.studyTitle === "string" ? data.studyTitle : "") ||
        (typeof data.protocolTitle === "string" ? data.protocolTitle : "") ||
        (typeof data.name === "string" ? data.name : ""),
      objectives: Array.isArray(data.objectives) ? data.objectives : [],
      epochs: mappedEpochs,
      encounters: mappedEncounters,
      arms: mappedArms,
      rows: mappedRows,
      forms: Array.isArray(data.forms) ? data.forms : [],
      crossovers: Array.isArray(data.crossovers) ? data.crossovers : [],
    };
  }

  return data;
}

export function normalizeUsdm(rawData: unknown): NormalizedUsdm {
  const data = extractRawUsdmEnvelope(
    (typeof rawData === "object" && rawData !== null ? rawData : {}) as Record<
      string,
      unknown
    >
  );

  const rawStudyId = data.studyId ?? data.study_id;
  const fallbackStudyId =
    isDemoBuild() && DEMO_STUDY.code ? DEMO_STUDY.code : "STUDY-USDM-001";
  const studyId =
    typeof rawStudyId === "string" && rawStudyId.length > 0
      ? rawStudyId
      : fallbackStudyId;

  const rawStudyTitle = data.studyTitle ?? data.study_title;
  const studyTitle = typeof rawStudyTitle === "string" ? rawStudyTitle : "";

  const rawObjectives = Array.isArray(data.objectives) ? data.objectives : [];
  const objectives: StudyObjective[] = rawObjectives.map((obj) => {
    const o = (typeof obj === "object" && obj !== null ? obj : {}) as Record<
      string,
      unknown
    >;
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
