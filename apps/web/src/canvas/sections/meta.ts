import type { Section, SectionId, StageId } from "../stages/types";

export interface CanvasSectionMeta {
  id: SectionId;
  stageId: StageId;
  order: number;
  label: string;
  description: string;
}

/**
 * Pure section metadata discovered at build/dev/test time from `./*.section.ts`
 * by `createCanvasSectionMetaPlugin` so cold-boot stage & palette consumers
 * never bundle section `.vue` components or route-only API modules.
 */
export const SECTION_METADATA: readonly CanvasSectionMeta[] = Object.freeze([]);

export function sectionsForStage(stageId: StageId): Section[] {
  return SECTION_METADATA.filter((item) => item.stageId === stageId).map(
    (item) => ({
      id: item.id,
      label: item.label,
      description: item.description,
    })
  );
}
