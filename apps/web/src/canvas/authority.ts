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
 * Execution's SubjectAuthority.CanManageParticipants: investigators and coordinators. Compared with Execution's
 * own normalisation (trimmed, lower case) and without the web app's role aliases, because Execution does not
 * apply them: a data manager (#4981), a sponsor admin, a monitor or an auditor gets a 403.
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
]);

/** Add participants and change their state. */
export function canManageParticipants(auth: RawRoleHolder): boolean {
  return (auth.rawRoles ?? []).some((role) =>
    PARTICIPANT_ROLES.has(role.trim().toLowerCase())
  );
}

/**
 * Execution's SubjectAuthority.CanUnblind: only a Principal Investigator or Authorized ER Physician may
 * perform emergency unblinding (#4943, PRD-SUB-005).
 */
const UNBLINDING_ROLES = new Set([
  "principal_investigator",
  "principal investigator",
  "pi",
  "authorized_er_physician",
  "authorized er physician",
  "authorized-er-physician",
  "er_physician",
  "er physician",
  "emergency_unblinder",
]);

export function canUnblindParticipant(auth: RawRoleHolder): boolean {
  return (auth.rawRoles ?? []).some((role) =>
    UNBLINDING_ROLES.has(role.trim().toLowerCase())
  );
}

/**
 * Execution's PermissionMatrix.Execution: only unblinded_statistician drafts and approves randomization schemes.
 */
const SCHEME_AUTHOR_ROLES = new Set([
  "unblinded_statistician",
  "unblinded statistician",
  "unblinded-statistician",
]);

export function canManageRandomizationSchemes(auth: RawRoleHolder): boolean {
  return (auth.rawRoles ?? []).some((role) =>
    SCHEME_AUTHOR_ROLES.has(role.trim().toLowerCase())
  );
}

/** Seed IP kit inventory at activated sites (PermissionMatrix.Execution: sponsor_dm, sysadmin). */
const KIT_SEEDING_ROLES = new Set([
  "sponsor_dm",
  "data_manager",
  "data manager",
  "dm",
  "sponsor_admin",
  "admin",
  "sysadmin",
]);

export function canSeedKitInventory(auth: RawRoleHolder): boolean {
  return (auth.rawRoles ?? []).some((role) =>
    KIT_SEEDING_ROLES.has(role.trim().toLowerCase())
  );
}

/** Reconcile returned IP kits (PermissionMatrix: pharmacist, sponsor_dm, sysadmin). */
const KIT_RECONCILE_ROLES = new Set([
  "pharmacist",
  "site_pharmacist",
  "site pharmacist",
  "sponsor_dm",
  "data_manager",
  "data manager",
  "sponsor_admin",
  "admin",
  "sysadmin",
]);

export function canReconcileKitInventory(auth: RawRoleHolder): boolean {
  return (auth.rawRoles ?? []).some((role) =>
    KIT_RECONCILE_ROLES.has(role.trim().toLowerCase())
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
