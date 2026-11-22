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

const PARTICIPANT_MUTATION_PERSONAS = new Set([
  "site_crc",
  "site_investigator",
]);

/**
 * Add participants and change their state. Sponsor lenses (`super_admin`, `sponsor_designer`, `data_manager`)
 * stay read-only for participant records; when a workspace persona is active, switching to a site clinical
 * lens (`site_crc` or `site_investigator`) reveals participant mutations for authorized site staff.
 */
export function canManageParticipants(auth: RawRoleHolder): boolean {
  if (!holdsParticipantRole(auth)) return false;
  if (
    auth.currentPersona &&
    !PARTICIPANT_MUTATION_PERSONAS.has(auth.currentPersona)
  ) {
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

type AuthorityAuth = Partial<RoleHolder> & RawRoleHolder;

/**
 * Mirrors packages/security/src/Rbac/RoleNormalization.cs so workspace action gating matches backend
 * PermissionMatrix grants without web-only role alias inflation.
 */
const BACKEND_ROLE_ALIASES: Readonly<Record<string, string>> = {
  "unblinded statistician": "unblinded_statistician",
  "lead unblinded statistician": "unblinded_statistician",
  unblinded_statistician: "unblinded_statistician",
  idmc: "idmc",
  dsmb: "idmc",
  pharmacist: "pharmacist",
  "unblinded pharmacist": "pharmacist",
  unblinded_pharmacist: "pharmacist",
  "emergency unblinder": "emergency_unblinder",
  emergency_unblinder: "emergency_unblinder",
  protocol_reviewer: "protocol_reviewer",
  "protocol reviewer": "protocol_reviewer",
  "protocol-reviewer": "protocol_reviewer",
  reviewer: "protocol_reviewer",
  "external monitor": "external_monitor",
  external_monitor: "external_monitor",
  "external-monitor": "external_monitor",
  "cro monitor": "external_monitor",
  cro_monitor: "external_monitor",
  "cro-monitor": "external_monitor",
  sysadmin: "sysadmin",
  "system administrator": "sysadmin",
  system_admin: "sysadmin",
  "system-admin": "sysadmin",
  super_admin: "sysadmin",
  "sponsor study designer": "sponsor_designer",
  sponsor_designer: "sponsor_designer",
  "sponsor-designer": "sponsor_designer",
  designer: "sponsor_designer",
  study_designer: "sponsor_designer",
  "study-designer": "sponsor_designer",
  "study designer": "sponsor_designer",
  "sponsor designer": "sponsor_designer",
  "sponsor data manager": "sponsor_dm",
  sponsor_dm: "sponsor_dm",
  "sponsor-dm": "sponsor_dm",
  "sponsor dm": "sponsor_dm",
  "data manager": "sponsor_dm",
  data_manager: "sponsor_dm",
  "data-manager": "sponsor_dm",
  dm: "sponsor_dm",
  admin: "sponsor_dm",
  "sponsor admin": "sponsor_dm",
  sponsor_admin: "sponsor_dm",
  "sponsor medical monitor": "sponsor_mm",
  sponsor_mm: "sponsor_mm",
  "sponsor-mm": "sponsor_mm",
  "sponsor mm": "sponsor_mm",
  "medical monitor": "sponsor_mm",
  medical_monitor: "sponsor_mm",
  mm: "sponsor_mm",
  "sponsor statistician": "sponsor_statistician",
  sponsor_statistician: "sponsor_statistician",
  "sponsor-statistician": "sponsor_statistician",
  statistician: "sponsor_statistician",
  investigator: "investigator",
  "site investigator": "investigator",
  site_investigator: "investigator",
  "site-investigator": "investigator",
  "principal investigator": "principal_investigator",
  pi: "principal_investigator",
  principal_investigator: "principal_investigator",
  principalinvestigator: "principal_investigator",
  "authorized er physician": "authorized_er_physician",
  authorized_er_physician: "authorized_er_physician",
  "authorized-er-physician": "authorized_er_physician",
  authorederphysician: "authorized_er_physician",
  "er physician": "authorized_er_physician",
  er_physician: "authorized_er_physician",
  "lead investigator": "lead_investigator",
  lead_investigator: "lead_investigator",
  "lead-investigator": "lead_investigator",
  leadinvestigator: "lead_investigator",
  investigator_user: "investigator",
  sub_investigator: "investigator",
  "sub investigator": "investigator",
  "sub-investigator": "investigator",
  crc: "crc",
  "site crc": "crc",
  site_crc: "crc",
  "site-crc": "crc",
  "clinical research coordinator": "crc",
  "study coordinator": "crc",
  study_coordinator: "crc",
  "study-coordinator": "crc",
  coordinator: "crc",
  "site coordinator": "crc",
  site_coordinator: "crc",
  "site-coordinator": "crc",
  cra: "cra",
  "clinical research associate": "cra",
  monitor: "monitor",
  "cra/monitor": "cra",
  cra_monitor: "cra",
  "cra-monitor": "cra",
  auditor: "auditor",
  inspector: "auditor",
  regulatory_inspector: "auditor",
  tmf_auditor: "auditor",
};

function canonicalBackendRoles(
  auth: AuthorityAuth | null | undefined
): string[] {
  if (!auth) return [];
  const raw = auth.rawRoles ?? [];
  if (raw.length > 0) {
    return raw
      .map((r) => r.trim().toLowerCase())
      .filter(Boolean)
      .map((norm) => BACKEND_ROLE_ALIASES[norm] ?? norm);
  }
  if (typeof auth.hasRole === "function") {
    const inferred: string[] = [];
    if (auth.hasRole("sponsor_admin")) inferred.push("sponsor_dm");
    if (auth.hasRole("data_manager")) inferred.push("sponsor_dm");
    return inferred;
  }
  return [];
}

function holdsBackendRole(
  auth: AuthorityAuth | null | undefined,
  allowed: ReadonlySet<string>
): boolean {
  return canonicalBackendRoles(auth).some((role) => allowed.has(role));
}

const SITE_ACTIVATION_ROLES = new Set(["sysadmin", "sponsor_dm"]);
const MILESTONE_WRITE_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
]);
const VISIT_SCHEDULE_ROLES = new Set(["sysadmin", "sponsor_dm", "cra"]);
const VISIT_COMPLETE_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
]);
const VISIT_SIGN_OFF_ROLES = new Set(["sysadmin", "sponsor_dm", "monitor"]);
const DOA_MANAGE_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "investigator",
  "principal_investigator",
  "authorized_er_physician",
  "lead_investigator",
  "crc",
  "cra",
  "monitor",
]);
const DOA_SIGN_OFF_ROLES = new Set([
  "sysadmin",
  "investigator",
  "principal_investigator",
  "authorized_er_physician",
  "lead_investigator",
]);
const PHARMACIST_ROLES = new Set(["pharmacist"]);
const CGT_WRITE_ROLES = new Set([
  "investigator",
  "principal_investigator",
  "authorized_er_physician",
  "lead_investigator",
  "crc",
  "pharmacist",
]);
const RANDOMIZATION_SCHEME_READ_ROLES = new Set([
  "unblinded_statistician",
  "sponsor_dm",
  "auditor",
]);

/** Activate a compliant site in Execution (site_compliance:activate — sysadmin & sponsor_dm only). */
export function canActivateSite(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, SITE_ACTIVATION_ROLES);
}

/** Certify startup greenlight readiness in CTMS (ctms_greenlight:certify — sysadmin & sponsor_dm only). */
export function canCertifyGreenlight(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, SITE_ACTIVATION_ROLES);
}

/** Create and update site milestones in CTMS (ctms_site_milestone:create/update). */
export function canManageMilestones(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, MILESTONE_WRITE_ROLES);
}

/** Schedule monitoring visits in CTMS (ctms_monitoring_visit:create). */
export function canScheduleMonitoringVisits(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, VISIT_SCHEDULE_ROLES);
}

/** Complete scheduled monitoring visits in CTMS (ctms_monitoring_visit:update). */
export function canCompleteMonitoringVisits(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, VISIT_COMPLETE_ROLES);
}

/** Perform supervisory sign-off on completed monitoring visits (ctms_monitoring_visit:sign_off). */
export function canSignOffMonitoringVisits(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, VISIT_SIGN_OFF_ROLES);
}

/** Delegate or revoke site trial tasks in CTMS (ctms_doa:delegate/revoke). */
export function canManageDoa(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, DOA_MANAGE_ROLES);
}

/** Sign off Delegation of Authority records as Principal Investigator (ctms_doa:sign_off). */
export function canSignOffDoa(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, DOA_SIGN_OFF_ROLES);
}

/** Read and reconcile blinded site IP kit inventory (rtsm_supply:read/write — pharmacist only). */
export function canReconcileIpKits(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, PHARMACIST_ROLES);
}

/** Seed a site's IP kit inventory (ip_kit_inventory:seed — sysadmin & sponsor_dm). */
export function canSeedIpKits(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, SITE_ACTIVATION_ROLES);
}

/** Record cell and gene therapy chain-of-custody handovers (cgt_chain_of_custody:write). */
export function canRecordChainOfCustody(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, CGT_WRITE_ROLES);
}

/** Read blinded randomization scheme configuration (randomization_scheme:read). */
export function canReadRandomizationSchemes(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, RANDOMIZATION_SCHEME_READ_ROLES);
}
