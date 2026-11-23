/**
 * Generated from apps/execution/src/Domain/Subjects/{SubjectStateCodec,SubjectTransitionGraph}.cs and Application/Subjects/SubjectAuthority.cs by scripts/generate-canvas-contracts.js.
 * Do not edit manually; run `pnpm --filter web generate:api` to regenerate.
 */

export const PARTICIPANT_STATE_CODES = [
  "SCREENING",
  "SCREEN_FAILED",
  "ENROLLED",
  "RANDOMIZED",
  "ACTIVE",
  "UNBLINDED",
  "COMPLETED",
  "WITHDRAWN",
  "RECONSENT_REQUIRED",
] as const;

export type ParticipantStateCode = (typeof PARTICIPANT_STATE_CODES)[number];

export const PARTICIPANT_TRANSITIONS: Readonly<
  Record<string, readonly string[]>
> = Object.freeze({
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
});

export const PARTICIPANT_AUTHORITY_ROLES = [
  "investigator",
  "site_investigator",
  "site investigator",
  "site-investigator",
  "principal_investigator",
  "principal investigator",
  "pi",
  "sub_investigator",
  "sub investigator",
  "sub-investigator",
  "lead_investigator",
  "lead investigator",
  "crc",
  "site coordinator",
  "site_coordinator",
  "clinical research coordinator",
  "trial_coordinator",
  "trial coordinator",
  "trial-coordinator",
  "trialcoordinator",
] as const;

export const UNBLIND_AUTHORITY_ROLES = [
  "principal_investigator",
  "principal investigator",
  "pi",
  "authorized_er_physician",
  "authorized er physician",
] as const;
