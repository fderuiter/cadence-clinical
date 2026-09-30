/**
 * Which canvas actions to offer the signed-in user. This is presentation only: Execution decides, and the
 * canvas reports its refusal (UX-6). Execution checks query management and source data verification
 * (#4920) and participant changes (#4943); all move to kernel Grants with the command catalogue (#4847, #4781).
 */
interface RoleHolder {
  hasRole: (role: string) => boolean;
}

/** The realm roles exactly as the gateway signs them to Execution. */
interface RawRoleHolder {
  rawRoles?: readonly string[] | null;
}

/**
 * Execution's SubjectAuthority.CanManageParticipants: investigators, coordinators and data managers. Compared
 * with Execution's own normalisation (trimmed, lower case) and without the web app's role aliases, because
 * Execution does not apply them: a sponsor admin, a monitor or an auditor gets a 403.
 */
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
  "sponsor_dm",
  "data_manager",
  "data manager",
  "data-manager",
  "dm",
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

/** Verify observations against source, or flag them for re-verification. */
export function canVerifySource(auth: RoleHolder): boolean {
  return MONITORING_ROLES.some((role) => auth.hasRole(role));
}
