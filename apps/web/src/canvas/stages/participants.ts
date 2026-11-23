import {
  PARTICIPANT_STATE_CODES,
  PARTICIPANT_TRANSITIONS,
  type ParticipantStateCode,
} from "../generated/participantContracts";

export { PARTICIPANT_STATE_CODES, PARTICIPANT_TRANSITIONS };
export type { ParticipantStateCode };

const STATE_LABELS: Readonly<Record<ParticipantStateCode, string>> = {
  SCREENING: "Screening",
  SCREEN_FAILED: "Screen failed",
  ENROLLED: "Enrolled",
  RANDOMIZED: "Randomized",
  ACTIVE: "Active",
  RECONSENT_REQUIRED: "Re-consent required",
  UNBLINDED: "Unblinded",
  COMPLETED: "Completed",
  WITHDRAWN: "Withdrawn",
};

/** Lifecycle display ordering of Execution's canonical participant states. */
const LIFECYCLE_STATE_ORDER: readonly ParticipantStateCode[] = [
  "SCREENING",
  "SCREEN_FAILED",
  "ENROLLED",
  "RANDOMIZED",
  "ACTIVE",
  "RECONSENT_REQUIRED",
  "UNBLINDED",
  "COMPLETED",
  "WITHDRAWN",
];

/** Participant states as Execution reports them, in lifecycle order, with their labels. */
export const PARTICIPANT_STATES: ReadonlyArray<{
  code: string;
  label: string;
}> = LIFECYCLE_STATE_ORDER.filter((code) =>
  (PARTICIPANT_STATE_CODES as readonly string[]).includes(code)
).map((code) => ({
  code,
  label: STATE_LABELS[code],
}));

export function participantStateLabel(code: string): string {
  return PARTICIPANT_STATES.find((s) => s.code === code)?.label ?? code;
}

export function isParticipantState(code: unknown): code is string {
  return PARTICIPANT_STATES.some((s) => s.code === code);
}

/**
 * Randomization requires step-up signature and allocation via /randomize (`execution.subject.randomize`),
 * and unblinding breaks the blind via its own emergency unblinding workflow (#4943), so neither is offered
 * as a generic state-patch dropdown option.
 */
const WITHHELD_STATE_CHANGES = new Set(["RANDOMIZED", "UNBLINDED"]);

/** Whether the participant is eligible for step-up randomization from its current state. */
export function canRandomizeParticipant(code: string): boolean {
  return code === "ENROLLED";
}

export const APPROVE_RANDOMIZATION_SCHEME_SEMANTIC_ACTION =
  "execution.form.approve";
export const RANDOMIZE_SUBJECT_SEMANTIC_ACTION = "execution.subject.randomize";

export function approveRandomizationSchemeAction(schemeId: string): string {
  return `/api/v1/execution/randomization/schemes/${encodeURIComponent(schemeId.trim().toLowerCase())}/approve`;
}

export function randomizeSubjectAction(subjectId: string): string {
  return `/api/v1/execution/subjects/${encodeURIComponent(subjectId.trim())}/randomize`;
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
