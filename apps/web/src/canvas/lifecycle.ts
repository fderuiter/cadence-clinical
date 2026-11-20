/**
 * The Clinical Canvas lifecycle (docs/ux/CLINICAL_CANVAS.md §2.2): six stages, each with the sections a
 * service serves today. A section appears only when its data is real (§6.1: absent, not mocked); the rest
 * of each stage's sections arrive with their services, tracked in #4829.
 */

export type StageId =
  "design" | "start" | "conduct" | "data" | "analyze" | "submit";

export type SectionId =
  | "protocol-versions"
  | "site-activation"
  | "randomization-schemes"
  | "participants"
  | "supply"
  | "queries";

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
    sections: [
      {
        id: "protocol-versions",
        label: "Protocol versions",
        description:
          "The protocol's versions, and what each amendment changes and affects",
      },
    ],
  },
  {
    id: "start",
    label: "Start",
    scope:
      "Countries, site activation, randomization schemes, essential documents, training",
    sections: [
      {
        id: "site-activation",
        label: "Site activation",
        description:
          "A site's activation documents and activating the site once they are complete",
      },
      {
        id: "randomization-schemes",
        label: "Randomization schemes",
        description:
          "Treatment arms, stratification factors, and two-person signed scheme approval before enrollment",
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
      {
        id: "supply",
        label: "Investigational supply",
        description:
          "Site kit inventory seeding and returned kit batch reconciliation",
      },
    ],
  },
  {
    id: "data",
    label: "Data",
    scope:
      "Collection, queries, external data, reconciliation, standards, validation",
    sections: [
      {
        id: "queries",
        label: "Queries",
        description: "Data queries across visible enrollments in this study",
      },
    ],
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

/**
 * The state changes Execution allows from each participant state (SubjectTransitionGraph.cs, spec Rule 9).
 * The canvas offers these and Execution decides; a test fails when this copy drifts from Execution's.
 */
export const PARTICIPANT_TRANSITIONS: Readonly<
  Record<string, readonly string[]>
> = {
  SCREENING: ["SCREEN_FAILED", "ENROLLED", "WITHDRAWN", "RECONSENT_REQUIRED"],
  ENROLLED: ["RANDOMIZED", "WITHDRAWN", "RECONSENT_REQUIRED"],
  RANDOMIZED: ["ACTIVE", "WITHDRAWN", "UNBLINDED", "RECONSENT_REQUIRED"],
  ACTIVE: ["COMPLETED", "WITHDRAWN", "UNBLINDED", "RECONSENT_REQUIRED"],
  UNBLINDED: ["WITHDRAWN", "COMPLETED", "RECONSENT_REQUIRED"],
  RECONSENT_REQUIRED: [
    "SCREENING",
    "ENROLLED",
    "RANDOMIZED",
    "ACTIVE",
    "UNBLINDED",
    "COMPLETED",
    "WITHDRAWN",
  ],
  SCREEN_FAILED: [],
  COMPLETED: [],
  WITHDRAWN: [],
};

/**
 * Randomization allocates from an approved encrypted scheme with a step-up signature (ADR-2640), and
 * unblinding breaks the blind with a clinical justification (#4943), so both get dedicated workflows
 * rather than generic state-change menu entries.
 */
const WITHHELD_STATE_CHANGES = new Set(["RANDOMIZED", "UNBLINDED"]);

/** Whether a state's randomization transition is handled via the allocation workflow instead of PATCH /state. */
export function isRandomizationWithheld(code: string): boolean {
  return (PARTICIPANT_TRANSITIONS[code] ?? []).includes("RANDOMIZED");
}

/** The state changes the canvas offers from a state, in lifecycle order, and whether unblinding was left out. */
export function participantStateChanges(code: string): {
  targets: string[];
  unblindingWithheld: boolean;
} {
  const allowed = new Set(PARTICIPANT_TRANSITIONS[code] ?? []);
  return {
    targets: PARTICIPANT_STATES.map((s) => s.code).filter(
      (target) => allowed.has(target) && !WITHHELD_STATE_CHANGES.has(target)
    ),
    unblindingWithheld: allowed.has("UNBLINDED"),
  };
}
