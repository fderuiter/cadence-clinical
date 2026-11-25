import type { Section, StageId } from "../stages/types";
import type { CanvasSectionDescriptor } from "./types";

const sectionModules = import.meta.glob<{ default: CanvasSectionDescriptor }>(
  "./*.section.ts",
  { eager: true }
);

export const SECTION_DESCRIPTORS: readonly CanvasSectionDescriptor[] =
  Object.freeze(
    Object.values(sectionModules)
      .map((mod) => mod.default)
      .filter((descriptor): descriptor is CanvasSectionDescriptor =>
        Boolean(descriptor?.id && descriptor?.stageId)
      )
      .sort((a, b) => {
        const orderDiff = (a.order ?? 9999) - (b.order ?? 9999);
        if (orderDiff !== 0) return orderDiff;
        return a.id.localeCompare(b.id);
      })
  );

const descriptorById = new Map(
  SECTION_DESCRIPTORS.map((descriptor) => [descriptor.id, descriptor])
);

export function sectionsForStage(stageId: StageId): Section[] {
  return SECTION_DESCRIPTORS.filter(
    (descriptor) => descriptor.stageId === stageId
  ).map((descriptor) => ({
    id: descriptor.id,
    label: descriptor.label,
    description: descriptor.description,
  }));
}

export function findSectionDescriptor(
  id: unknown
): CanvasSectionDescriptor | undefined {
  if (typeof id !== "string") return undefined;
  return descriptorById.get(id);
}
