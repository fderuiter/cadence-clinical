import { ANALYZE_STAGE } from "./analyze";
import { CONDUCT_STAGE } from "./conduct";
import { DATA_STAGE } from "./data";
import { DESIGN_STAGE } from "./design";
import { START_STAGE } from "./start";
import { SUBMIT_STAGE } from "./submit";
import type { Section, SectionId, Stage, StageId } from "./types";

export type { Section, SectionId, Stage, StageId };
export {
  ANALYZE_STAGE,
  CONDUCT_STAGE,
  DATA_STAGE,
  DESIGN_STAGE,
  START_STAGE,
  SUBMIT_STAGE,
};

export const STAGES: readonly Stage[] = [
  DESIGN_STAGE,
  START_STAGE,
  CONDUCT_STAGE,
  DATA_STAGE,
  ANALYZE_STAGE,
  SUBMIT_STAGE,
];

export function findStage(id: unknown): Stage | undefined {
  return STAGES.find((stage) => stage.id === id);
}

/** The stage and section for a section id, when the section exists. */
export function findSection(
  id: unknown
): { stage: Stage; section: Section } | undefined {
  for (const stage of STAGES) {
    const section = stage.sections.find((s) => s.id === id);
    if (section) return { stage, section };
  }
  return undefined;
}

export * from "./participants";
