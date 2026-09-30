/**
 * The Clinical Canvas lifecycle (docs/ux/CLINICAL_CANVAS.md §2.2): six stages, each with the sections a
 * service serves today. A section appears only when its data is real (§6.1: absent, not mocked); the rest
 * of each stage's sections arrive with their services, tracked in #4829.
 */

export type StageId =
  "design" | "start" | "conduct" | "data" | "analyze" | "submit";

export type SectionId = "site-activation" | "participants";

export interface Section {
  id: SectionId;
  label: string;
  description: string;
}

export interface Stage {
  id: StageId;
  label: string;
  /** What the stage covers once all of its sections exist (§2.2). */
  scope: string;
  sections: Section[];
}

export const STAGES: readonly Stage[] = [
  {
    id: "design",
    label: "Design",
    scope:
      "Protocol, objectives and estimands, schedule of activities, collection specs, standards and mappings",
    sections: [],
  },
  {
    id: "start",
    label: "Start",
    scope: "Countries, site activation, essential documents, training",
    sections: [
      {
        id: "site-activation",
        label: "Site activation",
        description:
          "A site's activation documents and activating the site once they are complete",
      },
    ],
  },
  {
    id: "conduct",
    label: "Conduct",
    scope: "Participants, sites, visits, safety, supply, monitoring",
    sections: [
      {
        id: "participants",
        label: "Participants",
        description: "The study's participants in enrollment order",
      },
    ],
  },
  {
    id: "data",
    label: "Data",
    scope:
      "Collection, queries, external data, reconciliation, standards, validation",
    sections: [],
  },
  {
    id: "analyze",
    label: "Analyze",
    scope: "ADaM, derivations, analysis, TLFs, traceability",
    sections: [],
  },
  {
    id: "submit",
    label: "Submit",
    scope: "Datasets, Define-XML, reviewer guides, package",
    sections: [],
  },
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

/** Participant states as Execution reports them, in lifecycle order, with their labels. */
export const PARTICIPANT_STATES: ReadonlyArray<{
  code: string;
  label: string;
}> = [
  { code: "SCREENING", label: "Screening" },
  { code: "SCREEN_FAILED", label: "Screen failed" },
  { code: "ENROLLED", label: "Enrolled" },
  { code: "RANDOMIZED", label: "Randomized" },
  { code: "ACTIVE", label: "Active" },
  { code: "RECONSENT_REQUIRED", label: "Re-consent required" },
  { code: "UNBLINDED", label: "Unblinded" },
  { code: "COMPLETED", label: "Completed" },
  { code: "WITHDRAWN", label: "Withdrawn" },
];

export function participantStateLabel(code: string): string {
  return PARTICIPANT_STATES.find((s) => s.code === code)?.label ?? code;
}

export function isParticipantState(code: unknown): code is string {
  return PARTICIPANT_STATES.some((s) => s.code === code);
}
