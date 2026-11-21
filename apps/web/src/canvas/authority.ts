/**
 * Which canvas and operational actions to offer the signed-in user. This is presentation only:
 * backend services enforce permissions via PermissionMatrix, and the UI surfaces any refusal (UX-6).
 *
 * IMPORTANT: Never delegate backend role splits to `authStore.hasRole()`, because `stores/auth.js`
 * aliases `cra <-> monitor` and expands `sponsor_admin` to every workspace persona. Instead, normalize
 * `auth.rawRoles` with the exact rules of `Cadence.Security.Rbac.RoleNormalization`.
 */
interface RoleHolder {
  hasRole: (role: string) => boolean;
}

/** The realm roles exactly as the gateway signs them to downstream .NET services. */
export interface RawRoleHolder {
  rawRoles?: readonly string[] | null;
}

const BACKEND_ROLE_ALIASES: Record<string, string> = {
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
  subject: "subject",
  patient: "subject",
  epro: "subject",
  auditor: "auditor",
  inspector: "auditor",
  regulatory_inspector: "auditor",
  finance_officer: "finance_officer",
  "finance officer": "finance_officer",
  "finance-officer": "finance_officer",
  financeofficer: "finance_officer",
  "grants manager": "finance_officer",
  grants_manager: "finance_officer",
  "grants-manager": "finance_officer",
  grantsmanager: "finance_officer",
};

/** Normalizes a raw realm role string using the same rules as Cadence.Security.Rbac.RoleNormalization. */
export function normalizeBackendRole(role: string): string {
  const norm = (role || "").trim().toLowerCase();
  if (!norm) return "";
  return BACKEND_ROLE_ALIASES[norm] ?? norm;
}

function hasAnyCanonicalRole(
  auth: RawRoleHolder | null | undefined,
  allowedRoles: ReadonlySet<string>
): boolean {
  if (!auth?.rawRoles) return false;
  return auth.rawRoles.some((role) =>
    allowedRoles.has(normalizeBackendRole(role))
  );
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

const SPONSOR_CERTIFICATION_ROLES = new Set(["sysadmin", "sponsor_dm"]);
const SITE_MILESTONE_WRITE_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
]);
const MONITORING_VISIT_EXECUTION_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
]);
const MONITORING_VISIT_SIGNOFF_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "monitor",
]);
const DOA_MANAGE_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
  "crc",
  "investigator",
  "principal_investigator",
  "authorized_er_physician",
  "lead_investigator",
]);
const DOA_SIGNOFF_ROLES = new Set([
  "sysadmin",
  "investigator",
  "principal_investigator",
  "authorized_er_physician",
  "lead_investigator",
]);
const ETMF_STAGE_QC_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
  "crc",
]);
const ETMF_REVIEW_QC_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
]);
const TRAINING_CURRICULUM_ASSIGN_ROLES = new Set(["sysadmin", "sponsor_dm"]);
const TRAINING_DELEGATION_SIGN_ROLES = new Set([
  "principal_investigator",
  "authorized_er_physician",
  "lead_investigator",
]);

export const SITE_ACTIVATION_ROLE_REASON =
  "Requires Sponsor Data Manager or System Administrator role (site_compliance:activate).";
export const GREENLIGHT_CERTIFY_ROLE_REASON =
  "Requires Sponsor Data Manager or System Administrator role (ctms_greenlight:certify).";
export const SITE_MILESTONE_ROLE_REASON =
  "Requires CRA, Monitor, Sponsor Data Manager, or System Administrator role (ctms_site_milestone:update).";
export const MONITORING_VISIT_COMPLETE_ROLE_REASON =
  "Requires CRA, Sponsor Data Manager, or System Administrator role (ctms_monitoring_visit:update). Monitors perform supervisory sign-off only.";
export const MONITORING_LETTER_COMPILE_ROLE_REASON =
  "Requires CRA, Sponsor Data Manager, or System Administrator role (ctms_monitoring_visit:create).";
export const MONITORING_VISIT_SIGNOFF_ROLE_REASON =
  "Requires Monitor, Sponsor Data Manager, or System Administrator role (ctms_monitoring_visit:sign_off). CRAs complete visits and compile letters; Monitors sign off.";
export const DOA_SIGNOFF_ROLE_REASON =
  "Requires Investigator or System Administrator role (ctms_doa:sign_off).";
export const ETMF_QC_REVIEW_ROLE_REASON =
  "Requires CRA, Monitor, Sponsor Data Manager, or System Administrator role (etmf_document:transition_clinical_qc).";
export const TRAINING_ASSIGN_ROLE_REASON =
  "Requires Sponsor Data Manager or System Administrator role (training_curriculum:assign).";
export const TRAINING_DELEGATION_SIGN_ROLE_REASON =
  "Requires Principal Investigator role (delegation_log:sign).";

/** PermissionMatrix.Execution: `site_compliance:activate` (`sysadmin`, `sponsor_dm`). */
export function canActivateSite(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, SPONSOR_CERTIFICATION_ROLES);
}

/** PermissionMatrix.Ctms: `ctms_greenlight:certify` (`sysadmin`, `sponsor_dm`). */
export function canCertifyGreenlight(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, SPONSOR_CERTIFICATION_ROLES);
}

/** PermissionMatrix: `ctms_site_milestone:create` / `update` (`sysadmin`, `sponsor_dm`, `cra`, `monitor`). */
export function canManageSiteMilestones(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, SITE_MILESTONE_WRITE_ROLES);
}

/** PermissionMatrix: `ctms_monitoring_visit:create` / `update` (`sysadmin`, `sponsor_dm`, `cra` — NOT `monitor`). */
export function canManageMonitoringVisits(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, MONITORING_VISIT_EXECUTION_ROLES);
}

/** PermissionMatrix: `ctms_monitoring_visit:sign_off` (`sysadmin`, `sponsor_dm`, `monitor` — NOT `cra`). */
export function canSignOffMonitoringVisit(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, MONITORING_VISIT_SIGNOFF_ROLES);
}

/** PermissionMatrix.Ctms: `ctms_doa:assign` / `revoke`. */
export function canManageDoa(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, DOA_MANAGE_ROLES);
}

/** PermissionMatrix.Ctms: `ctms_doa:sign_off` (`sysadmin`, `investigator`, PI variants). */
export function canSignOffDoa(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, DOA_SIGNOFF_ROLES);
}

/** PermissionMatrix.Etmf: `etmf_document:create` (`sysadmin`, `sponsor_dm`, `cra`, `monitor`, `crc`). */
export function canStageEtmfQc(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, ETMF_STAGE_QC_ROLES);
}

/** PermissionMatrix: `etmf_document:transition_clinical_qc` (`sysadmin`, `sponsor_dm`, `cra`, `monitor`). */
export function canReviewEtmfQc(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, ETMF_REVIEW_QC_ROLES);
}

/** PermissionMatrix.Knowledge: `training_curriculum:assign` (`sysadmin`, `sponsor_dm`). */
export function canAssignTrainingCurriculum(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, TRAINING_CURRICULUM_ASSIGN_ROLES);
}

/** PermissionMatrix: `delegation_log:sign` (`principal_investigator`, `authorized_er_physician`, `lead_investigator`). */
export function canSignTrainingDelegations(auth: RawRoleHolder): boolean {
  return hasAnyCanonicalRole(auth, TRAINING_DELEGATION_SIGN_ROLES);
}
