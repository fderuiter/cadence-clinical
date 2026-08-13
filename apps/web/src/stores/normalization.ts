import {
  SoAMatrixView,
  SoAHeaderEpoch,
  SoAHeaderEncounter,
  SoAHeaderArm,
  SoARowView,
  SoACellView,
} from "../types/usdm";

export interface NormalizedUsdm extends SoAMatrixView {
  studyId: string;
  studyTitle: string;
  objectives: any[];
}

export function normalizeUsdm(rawData: any): NormalizedUsdm {
  const data = rawData || {};

  const studyId =
    typeof data.studyId === "string" ? data.studyId : "STUDY-USDM-001";
  const studyTitle = typeof data.studyTitle === "string" ? data.studyTitle : "";
  const objectives = Array.isArray(data.objectives) ? data.objectives : [];

  const rawEpochs = Array.isArray(data.epochs) ? data.epochs : [];
  const epochs: SoAHeaderEpoch[] = rawEpochs.map((ep: any): SoAHeaderEpoch => {
    return {
      epoch_id: typeof ep.epoch_id === "string" ? ep.epoch_id : "",
      epoch_name: typeof ep.epoch_name === "string" ? ep.epoch_name : "",
      sequence: typeof ep.sequence === "number" ? ep.sequence : 0,
      arm_id: ep.arm_id || null,
    };
  });

  const rawEncounters = Array.isArray(data.encounters) ? data.encounters : [];
  const encounters: SoAHeaderEncounter[] = rawEncounters.map(
    (enc: any): SoAHeaderEncounter => {
      return {
        encounter_id:
          typeof enc.encounter_id === "string" ? enc.encounter_id : "",
        encounter_name:
          typeof enc.encounter_name === "string" ? enc.encounter_name : "",
        epoch_id: typeof enc.epoch_id === "string" ? enc.epoch_id : "",
        sequence: typeof enc.sequence === "number" ? enc.sequence : 0,
        arm_id: enc.arm_id || null,
      };
    }
  );

  const rawArms = Array.isArray(data.arms) ? data.arms : [];
  const arms: SoAHeaderArm[] = rawArms.map((arm: any): SoAHeaderArm => {
    return {
      arm_id: typeof arm.arm_id === "string" ? arm.arm_id : "",
      arm_name: typeof arm.arm_name === "string" ? arm.arm_name : "",
      sequence: typeof arm.sequence === "number" ? arm.sequence : 0,
    };
  });

  const rawRows = Array.isArray(data.rows) ? data.rows : [];
  const rows: SoARowView[] = rawRows.map((row: any): SoARowView => {
    const actId = typeof row.activity_id === "string" ? row.activity_id : "";
    const rawCells = Array.isArray(row.cells) ? row.cells : [];
    const cells: SoACellView[] = rawCells.map((cell: any): SoACellView => {
      return {
        activity_id:
          typeof cell.activity_id === "string" ? cell.activity_id : actId,
        encounter_id:
          typeof cell.encounter_id === "string" ? cell.encounter_id : "",
        epoch_id: typeof cell.epoch_id === "string" ? cell.epoch_id : "",
        is_applicable:
          typeof cell.is_applicable === "boolean" ? cell.is_applicable : false,
        details: typeof cell.details === "string" ? cell.details : "",
        arm_id: cell.arm_id || null,
        derived_from_soa:
          typeof cell.derived_from_soa === "boolean"
            ? cell.derived_from_soa
            : false,
      };
    });

    return {
      activity_id: actId,
      activity_name:
        typeof row.activity_name === "string" ? row.activity_name : "",
      cells,
      derived_from_soa:
        typeof row.derived_from_soa === "boolean"
          ? row.derived_from_soa
          : false,
    };
  });

  return {
    studyId,
    studyTitle,
    objectives,
    epochs,
    encounters,
    arms,
    rows,
  };
}
