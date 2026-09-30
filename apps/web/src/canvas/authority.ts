/**
 * Which canvas actions to offer the signed-in user. This is presentation only: Execution decides, and the
 * canvas reports its refusal (UX-6). Execution checks both query management and source data verification
 * (#4920); both move to kernel Grants with the command catalogue (#4847, #4781).
 */
interface RoleHolder {
  hasRole: (role: string) => boolean;
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
