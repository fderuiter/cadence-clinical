/**
 * Which canvas actions to offer the signed-in user. This is presentation only: Execution decides, and the
 * canvas reports its refusal (UX-6). Execution checks query management and source data verification
 * (#4920) and participant changes (#4943); all move to kernel Grants with the command catalogue (#4847, #4781).
 */
interface RoleHolder {
  hasRole: (role: string) => boolean;
}

/** The realm roles and active persona/site claims as signed by the gateway to Execution. */
interface RawRoleHolder {
  rawRoles?: readonly string[] | null;
  currentPersona?: string | null;
  assignedSiteIds?: readonly string[] | null;
  siteIds?: readonly string[] | null;
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

/** Whether the caller's token carries a raw coordinator/investigator role accepted by Execution. */
export function holdsParticipantRole(auth: RawRoleHolder): boolean {
  return (auth.rawRoles ?? []).some((role) =>
    PARTICIPANT_ROLES.has(role.trim().toLowerCase())
  );
}

/**
 * Add participants and change their state. Sponsor lenses (`super_admin`, `sponsor_designer`, `data_manager`)
 * stay read-only for participant records; when a workspace persona is active, switching to the Site Coordinator
 * lens (`site_crc`) reveals participant mutations for authorized site staff.
 */
export function canManageParticipants(auth: RawRoleHolder): boolean {
  if (!holdsParticipantRole(auth)) return false;
  if (auth.currentPersona && auth.currentPersona !== "site_crc") {
    return false;
  }
  return true;
}

/**
 * Filters candidate site codes to those authorized by the caller's explicit `site_id` claims (`SiteAccessEvaluator`).
 * When the caller has no explicit `site_id` restriction, returns `availableSiteCodes` unchanged.
 */
export function authorizedParticipantSiteCodes(
  auth: RawRoleHolder,
  availableSiteCodes: readonly string[]
): string[] {
  const assigned = (auth.assignedSiteIds ?? auth.siteIds ?? [])
    .map((s) => s.trim())
    .filter((s) => s.length > 0 && s !== "*");
  if (assigned.length === 0) {
    return [...availableSiteCodes];
  }
  const assignedLower = new Set(assigned.map((s) => s.toLowerCase()));
  if (availableSiteCodes.length === 0) {
    return assigned;
  }
  const matched = availableSiteCodes.filter((code) =>
    assignedLower.has(code.trim().toLowerCase())
  );
  return matched.length > 0 ? matched : assigned;
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

// Roles granted observation.correct in the kernel (site coordinators, investigators, and data managers;
// monitors and auditors inspect observations read-only per KernelObservationEndpoints.cs).
const OBSERVATION_CORRECTION_ROLES = [
  "site_investigator",
  "crc",
  "data_manager",
];

/** Whether the caller holds a role permitted to submit observation value corrections. */
export function canCorrectObservations(
  auth: RoleHolder & RawRoleHolder & { isAuthenticated?: boolean }
): boolean {
  if (!auth.isAuthenticated) return true;
  if (holdsParticipantRole(auth)) return true;
  return OBSERVATION_CORRECTION_ROLES.some((role) => auth.hasRole(role));
}
