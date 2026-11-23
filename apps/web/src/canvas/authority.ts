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
  subject: "subject",
  patient: "subject",
  epro: "subject",
  auditor: "auditor",
  inspector: "auditor",
  regulatory_inspector: "auditor",
  tmf_auditor: "auditor",
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

function canonicalBackendRoles(
  auth: AuthorityAuth | null | undefined
): string[] {
  if (!auth) return [];
  const raw = auth.rawRoles ?? [];
  if (raw.length > 0) {
    return raw.map((r) => normalizeBackendRole(r)).filter(Boolean);
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
const RANDOMIZATION_SCHEME_WRITE_ROLES = new Set(["unblinded_statistician"]);
const UNBLIND_PARTICIPANT_ROLES = new Set([
  "principal_investigator",
  "principal investigator",
  "pi",
  "authorized_er_physician",
  "authorized er physician",
]);
const ETMF_READ_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "investigator",
  "principal_investigator",
  "authorized_er_physician",
  "lead_investigator",
  "crc",
  "cra",
  "monitor",
  "auditor",
  "external_monitor",
]);
const ETMF_STAGE_QC_ROLES = new Set([
  "sysadmin",
  "sponsor_dm",
  "cra",
  "monitor",
  "crc",
]);
const ETMF_TAG_ROLES = new Set([
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
const ETMF_EXPORT_ROLES = new Set(["sponsor_dm", "cra", "monitor", "auditor"]);
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
export const MONITORING_VISIT_SCHEDULE_ROLE_REASON =
  "Requires CRA, Sponsor Data Manager, or System Administrator role (ctms_monitoring_visit:create).";
export const MONITORING_VISIT_COMPLETE_ROLE_REASON =
  "Requires CRA, Monitor, Sponsor Data Manager, or System Administrator role (ctms_monitoring_visit:update).";
export const MONITORING_LETTER_COMPILE_ROLE_REASON =
  "Requires CRA, Sponsor Data Manager, or System Administrator role (ctms_monitoring_visit:create). Monitors perform supervisory sign-off rather than scheduling visits or compiling letters.";
export const MONITORING_VISIT_SIGNOFF_ROLE_REASON =
  "Requires Monitor, Sponsor Data Manager, or System Administrator role (ctms_monitoring_visit:sign_off). CRAs schedule/complete visits and compile letters; Monitors sign off.";
export const DOA_SIGNOFF_ROLE_REASON =
  "Requires Investigator or System Administrator role (ctms_doa:sign_off).";
export const ETMF_READ_ROLE_REASON =
  "Requires Clinical Research Coordinator, Investigator, CRA, Monitor, Sponsor Data Manager, Auditor, External Monitor, or System Administrator role (etmf_document:read).";
export const ETMF_STAGE_ROLE_REASON =
  "Requires Clinical Research Coordinator, CRA, Monitor, Sponsor Data Manager, or System Administrator role (etmf_document:create).";
export const ETMF_TAG_ROLE_REASON =
  "Requires Clinical Research Coordinator, CRA, Monitor, Sponsor Data Manager, or System Administrator role (etmf_document:tag).";
export const ETMF_QC_REVIEW_ROLE_REASON =
  "Requires CRA, Monitor, Sponsor Data Manager, or System Administrator role (etmf_document:transition_clinical_qc).";
export const ETMF_EXPORT_ROLE_REASON =
  "Requires Sponsor Data Manager, CRA, Monitor, or Auditor role (etmf_document:export).";
export const TRAINING_ASSIGN_ROLE_REASON =
  "Requires Sponsor Data Manager or System Administrator role (training_curriculum:assign).";
export const TRAINING_DELEGATION_SIGN_ROLE_REASON =
  "Requires Principal Investigator role (delegation_log:sign).";
export const RANDOMIZATION_SCHEME_WRITE_ROLE_REASON =
  "Requires Unblinded Statistician role (randomization_scheme:draft/approve).";
export const EMERGENCY_UNBLIND_ROLE_REASON =
  "Requires Principal Investigator or Authorized ER Physician role.";

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

/** Alias for `canManageMilestones` used across CTMS views. */
export function canManageSiteMilestones(auth: AuthorityAuth): boolean {
  return canManageMilestones(auth);
}

/** Schedule monitoring visits and compile visit letters in CTMS (ctms_monitoring_visit:create). */
export function canScheduleMonitoringVisits(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, VISIT_SCHEDULE_ROLES);
}

/** Alias for `canScheduleMonitoringVisits` (`ctms_monitoring_visit:create`). */
export function canManageMonitoringVisits(auth: AuthorityAuth): boolean {
  return canScheduleMonitoringVisits(auth);
}

/** Complete scheduled monitoring visits in CTMS (ctms_monitoring_visit:update). */
export function canCompleteMonitoringVisits(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, VISIT_COMPLETE_ROLES);
}

/** Perform supervisory sign-off on completed monitoring visits (ctms_monitoring_visit:sign_off). */
export function canSignOffMonitoringVisits(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, VISIT_SIGN_OFF_ROLES);
}

/** Alias for `canSignOffMonitoringVisits`. */
export function canSignOffMonitoringVisit(auth: AuthorityAuth): boolean {
  return canSignOffMonitoringVisits(auth);
}

/** Delegate or revoke site trial tasks in CTMS (ctms_doa:assign/revoke). */
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

/** Draft and approve study randomization schemes (randomization_scheme:draft/approve — unblinded_statistician only). */
export function canManageRandomizationSchemes(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, RANDOMIZATION_SCHEME_WRITE_ROLES);
}

/** Perform emergency participant unblinding (SubjectAuthority.CanUnblind — principal_investigator & authorized_er_physician). */
export function canUnblindParticipant(
  auth: RawRoleHolder | null | undefined
): boolean {
  return (auth?.rawRoles ?? []).some((role) =>
    UNBLIND_PARTICIPANT_ROLES.has(role.trim().toLowerCase())
  );
}

/** Read eTMF documents, taxonomy, QC queues, and binder metadata (etmf_document:read). */
export function canReadEtmf(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, ETMF_READ_ROLES);
}

/** Stage site startup / study documents into eTMF intelligence QC (etmf_document:create). */
export function canStageEtmfQc(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, ETMF_STAGE_QC_ROLES);
}

/** Update eTMF document taxonomy classifications (etmf_document:tag). */
export function canTagEtmf(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, ETMF_TAG_ROLES);
}

/** Transition staged eTMF documents through clinical QC review (etmf_document:transition_clinical_qc). */
export function canReviewEtmfQc(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, ETMF_REVIEW_QC_ROLES);
}

/** Export study binder documents or archives (etmf_document:export). */
export function canExportEtmf(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, ETMF_EXPORT_ROLES);
}

/** Assign protocol training curricula to personnel (training_curriculum:assign). */
export function canAssignTrainingCurriculum(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, TRAINING_CURRICULUM_ASSIGN_ROLES);
}

/** Sign Knowledge site delegation logs as Principal Investigator (delegation_log:sign). */
export function canSignTrainingDelegations(auth: AuthorityAuth): boolean {
  return holdsBackendRole(auth, TRAINING_DELEGATION_SIGN_ROLES);
}
