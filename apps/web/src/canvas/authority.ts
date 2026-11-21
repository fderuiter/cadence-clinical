/**
 * Which canvas actions to offer the signed-in user. This is presentation only: Execution and the Kernel decide, and
 * the canvas reports any refusal (UX-6).
 */
interface RoleHolder {
  hasRole: (role: string) => boolean;
}

/** The realm roles exactly as the gateway signs them to Execution. */
interface RawRoleHolder {
  rawRoles?: readonly string[] | null;
}

const PARTICIPANT_ROLES = new Set([
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
]);

const SIGNING_RAW_ROLES = new Set([
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
]);

/** Add participants and change their state. */
export function canManageParticipants(auth: RawRoleHolder): boolean {
  return (auth.rawRoles ?? []).some((role) =>
    PARTICIPANT_ROLES.has(role.trim().toLowerCase())
  );
}

// Execution's query lifecycle roles (ClinicalQueryApplicationService.IsAuthorizedCraOrDm).
const MONITORING_ROLES = ["cra", "data_manager"];

/** Close, reopen and cancel queries, and raise them on observations. */
export function canManageQueries(auth: RoleHolder): boolean {
  return MONITORING_ROLES.some((role) => auth.hasRole(role));
}

/** Answer open queries (`execution.query.answer`: site coordinators and investigators, not monitors or data managers). */
export function canAnswerQueries(
  auth: RoleHolder & RawRoleHolder & { isAuthenticated?: boolean }
): boolean {
  if (!auth.isAuthenticated) return true;
  if (canManageParticipants(auth)) return true;
  if (canManageQueries(auth)) return false;
  return [
    "site_investigator",
    "investigator",
    "principal_investigator",
    "crc",
  ].some((role) => auth.hasRole(role));
}

/** Sign eCRF forms (`collection.sign`: investigators only). */
export function canSignForms(
  auth: RoleHolder & RawRoleHolder & { isAuthenticated?: boolean }
): boolean {
  if (!auth.isAuthenticated) return true;
  if (
    (auth.rawRoles ?? []).some((role) =>
      SIGNING_RAW_ROLES.has(role.trim().toLowerCase())
    )
  ) {
    return true;
  }
  return ["site_investigator", "investigator", "principal_investigator"].some(
    (role) => auth.hasRole(role)
  );
}

/** Draft, approve, and pin study edit-check rule packs. */
export function canManageRulePacks(
  auth: RoleHolder & RawRoleHolder & { isAuthenticated?: boolean }
): boolean {
  if (!auth.isAuthenticated) return true;
  if (
    (auth.rawRoles ?? []).some((role) => {
      const normalized = role.trim().toLowerCase();
      return (
        normalized.includes("designer") ||
        normalized.includes("data_manager") ||
        normalized.includes("data manager") ||
        normalized === "admin" ||
        normalized === "system_admin"
      );
    })
  ) {
    return true;
  }
  return ["study_designer", "designer", "data_manager", "admin"].some((role) =>
    auth.hasRole(role)
  );
}

/** Verify observations against source, or flag them for re-verification. */
export function canVerifySource(auth: RoleHolder): boolean {
  return MONITORING_ROLES.some((role) => auth.hasRole(role));
}

// Roles granted observation.correct in the kernel (site coordinators, investigators, and data managers;
// monitors and auditors inspect observations read-only per KernelObservationEndpoints.cs).
const OBSERVATION_CORRECTION_ROLES = [
  "site_investigator",
  "investigator",
  "principal_investigator",
  "crc",
  "data_manager",
];

/** Whether the caller holds a role permitted to submit observation value corrections. */
export function canCorrectObservations(
  auth: RoleHolder & RawRoleHolder & { isAuthenticated?: boolean }
): boolean {
  if (!auth.isAuthenticated) return true;
  if (canManageParticipants(auth)) return true;
  return OBSERVATION_CORRECTION_ROLES.some((role) => auth.hasRole(role));
}
