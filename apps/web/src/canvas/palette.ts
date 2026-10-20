import type { SubjectSummary } from "../api/executionApi";

/** Where a ⌘K palette choice takes the Clinical Canvas. */
export interface PaletteTarget {
  studyCode: string;
  stage?: string;
  section?: string;
  participant?: SubjectSummary;
  /** A question for the inspector's Assist pane (ADR-2635). */
  ask?: string;
}
