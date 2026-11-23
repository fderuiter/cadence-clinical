import { holdsBackendPermission, type AuthorityAuth } from "./roles";

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
  "Requires Principal Investigator or System Administrator role (ctms_doa:sign_off).";

/** Activate a compliant site in Execution (site_compliance:activate — sysadmin & sponsor_dm only). */
export function canActivateSite(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "site_compliance:activate");
}

/** Certify startup greenlight readiness in CTMS (ctms_greenlight:certify — sysadmin & sponsor_dm only). */
export function canCertifyGreenlight(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "ctms_greenlight:certify");
}

/** Create and update site milestones in CTMS (ctms_site_milestone:create/update). */
export function canManageMilestones(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "ctms_site_milestone:update");
}

/** Alias for `canManageMilestones` used across CTMS views. */
export function canManageSiteMilestones(auth: AuthorityAuth): boolean {
  return canManageMilestones(auth);
}

/** Schedule monitoring visits and compile visit letters in CTMS (ctms_monitoring_visit:create). */
export function canScheduleMonitoringVisits(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "ctms_monitoring_visit:create");
}

/** Alias for `canScheduleMonitoringVisits` (`ctms_monitoring_visit:create`). */
export function canManageMonitoringVisits(auth: AuthorityAuth): boolean {
  return canScheduleMonitoringVisits(auth);
}

/** Complete scheduled monitoring visits in CTMS (ctms_monitoring_visit:update). */
export function canCompleteMonitoringVisits(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "ctms_monitoring_visit:update");
}

/** Perform supervisory sign-off on completed monitoring visits (ctms_monitoring_visit:sign_off). */
export function canSignOffMonitoringVisits(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "ctms_monitoring_visit:sign_off");
}

/** Alias for `canSignOffMonitoringVisits`. */
export function canSignOffMonitoringVisit(auth: AuthorityAuth): boolean {
  return canSignOffMonitoringVisits(auth);
}

/** Delegate or revoke site trial tasks in CTMS (ctms_doa:assign/revoke). */
export function canManageDoa(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "ctms_doa:assign");
}

/** Sign off Delegation of Authority records as Principal Investigator (ctms_doa:sign_off). */
export function canSignOffDoa(auth: AuthorityAuth): boolean {
  return holdsBackendPermission(auth, "ctms_doa:sign_off");
}
