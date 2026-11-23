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
 * Unblinding breaks the blind for a participant, so it gets its own workflow rather than a menu entry here,
 * even though Execution accepts it as a state change from an unblinding authority (#4943).
 */
const WITHHELD_STATE_CHANGES = /* @__PURE__ */ new Set(["UNBLINDED"]);

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
    unblindingWithheld: [...allowed].some((t) => WITHHELD_STATE_CHANGES.has(t)),
  };
}
