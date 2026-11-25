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

const stageModules = import.meta.glob<{ default?: Stage }>(
  ["./*.ts", "!./index.ts"],
  { eager: true }
);

export const STAGES: readonly Stage[] = Object.freeze(
  Object.values(stageModules)
    .map((mod) => mod.default)
    .filter((stage): stage is Stage =>
      Boolean(stage?.id && Array.isArray(stage.sections))
    )
    .sort((a, b) => {
      const orderDiff = (a.order ?? 9999) - (b.order ?? 9999);
      if (orderDiff !== 0) return orderDiff;
      return a.id.localeCompare(b.id);
    })
);

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
