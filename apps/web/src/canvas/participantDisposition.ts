/** Terminal participant lifecycle states where new form capture is closed. */
export const TERMINAL_PARTICIPANT_STATES: ReadonlySet<string> =
  /* @__PURE__ */ new Set(["SCREEN_FAILED", "COMPLETED", "WITHDRAWN"]);

export function isTerminalParticipantState(code: unknown): boolean {
  return typeof code === "string" && TERMINAL_PARTICIPANT_STATES.has(code);
}

/** Disposition categories matching Kernel WithdrawalKind for participant withdrawal. */
export const WITHDRAWAL_KINDS: ReadonlyArray<{
  code: string;
  label: string;
}> = [
  { code: "WithdrawalBySubject", label: "Withdrawal by participant" },
  { code: "PhysicianDecision", label: "Physician decision" },
  { code: "AdverseEvent", label: "Adverse event" },
  { code: "LostToFollowUp", label: "Lost to follow-up" },
  { code: "Death", label: "Death" },
  { code: "ProtocolDeviation", label: "Protocol deviation" },
  {
    code: "StudyTerminatedBySponsor",
    label: "Study terminated by sponsor",
  },
  { code: "Other", label: "Other" },
];

/** Gateway step-up target URL and semantic action for signed participant withdrawal. */
export const WITHDRAW_ENROLLMENT_ACTION = "/api/v1/kernel/enrollments/withdraw";
export const WITHDRAW_ENROLLMENT_SEMANTIC_ACTION = "kernel.enrollment.withdraw";
