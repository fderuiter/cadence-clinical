import { PARTICIPANT_AUTHORITY_ROLES } from "../generated/participantContracts";
import {
  holdsBackendPermission,
  holdsBackendRole,
  isUnauthenticatedFallback,
  type AuthorityAuth,
  type RawRoleHolder,
} from "./roles";

const PARTICIPANT_ROLES: ReadonlySet<string> = new Set(
  PARTICIPANT_AUTHORITY_ROLES
);

const QUERY_MANAGEMENT_BACKEND_ROLES: ReadonlySet<string> = new Set([
  "cra",
  "monitor",
  "sponsor_dm",
]);

const QUERY_ANSWER_BACKEND_ROLES: ReadonlySet<string> = new Set([
  "crc",
  "investigator",
  "principal_investigator",
  "lead_investigator",
  "authorized_er_physician",
]);

const RULE_PACK_BACKEND_ROLES: ReadonlySet<string> = new Set([
  "sponsor_designer",
  "sponsor_dm",
  "sysadmin",
]);

const OBSERVATION_CORRECTION_BACKEND_ROLES: ReadonlySet<string> = new Set([
  "crc",
  "investigator",
  "principal_investigator",
  "lead_investigator",
  "authorized_er_physician",
  "sponsor_dm",
]);

const MONITORING_FALLBACK_ROLES = ["cra", "data_manager"] as const;

/** Add participants and change their state (`SubjectAuthority.CanManageParticipants`). */
export function canManageParticipants(auth: RawRoleHolder): boolean {
  return (auth?.rawRoles ?? []).some((role) =>
    PARTICIPANT_ROLES.has(role.trim().toLowerCase())
  );
}

/** Close, reopen and cancel queries, and raise them on observations (`execution.query.raise/manage`). */
export function canManageQueries(auth: AuthorityAuth): boolean {
  if (!auth) return false;
  if ((auth.rawRoles?.length ?? 0) > 0) {
    return holdsBackendRole(auth, QUERY_MANAGEMENT_BACKEND_ROLES);
  }
  if (typeof auth.hasRole === "function") {
    return MONITORING_FALLBACK_ROLES.some((role) => auth.hasRole!(role));
  }
  return false;
}

/** Answer open queries (`execution.query.answer`: site coordinators and investigators, not monitors or data managers). */
export function canAnswerQueries(auth: AuthorityAuth): boolean {
  if (!auth) return false;
  if (isUnauthenticatedFallback(auth)) return true;
  if (canManageParticipants(auth)) return true;
  if ((auth.rawRoles?.length ?? 0) > 0) {
    return holdsBackendRole(auth, QUERY_ANSWER_BACKEND_ROLES);
  }
  if (canManageQueries(auth)) return false;
  if (typeof auth.hasRole === "function") {
    return [
      "site_investigator",
      "investigator",
      "principal_investigator",
      "crc",
    ].some((role) => auth.hasRole!(role));
  }
  return false;
}

/** Sign eCRF forms (`ecrf_form:sign` / `collection.sign`: investigators only). */
export function canSignForms(auth: AuthorityAuth): boolean {
  if (!auth) return false;
  if (isUnauthenticatedFallback(auth)) return true;
  if ((auth.rawRoles?.length ?? 0) > 0) {
    return holdsBackendPermission(auth, "ecrf_form:sign");
  }
  if (typeof auth.hasRole === "function") {
    return ["site_investigator", "investigator", "principal_investigator"].some(
      (role) => auth.hasRole!(role)
    );
  }
  return false;
}

/** Draft, approve, and pin study edit-check rule packs (`library_object:approve` / `study_design:approve`). */
export function canManageRulePacks(auth: AuthorityAuth): boolean {
  if (!auth) return false;
  if (isUnauthenticatedFallback(auth)) return true;
  if ((auth.rawRoles?.length ?? 0) > 0) {
    return holdsBackendRole(auth, RULE_PACK_BACKEND_ROLES);
  }
  if (typeof auth.hasRole === "function") {
    return ["study_designer", "designer", "data_manager", "admin"].some(
      (role) => auth.hasRole!(role)
    );
  }
  return false;
}

/** Verify observations against source, or flag them for re-verification (`sdv:update`). */
export function canVerifySource(auth: AuthorityAuth): boolean {
  if (!auth) return false;
  if ((auth.rawRoles?.length ?? 0) > 0) {
    return holdsBackendPermission(auth, "sdv:update");
  }
  if (typeof auth.hasRole === "function") {
    return MONITORING_FALLBACK_ROLES.some((role) => auth.hasRole!(role));
  }
  return false;
}

/** Whether the caller holds a role permitted to submit observation value corrections (`observation.correct`). */
export function canCorrectObservations(auth: AuthorityAuth): boolean {
  if (!auth) return false;
  if (isUnauthenticatedFallback(auth)) return true;
  if (canManageParticipants(auth)) return true;
  if ((auth.rawRoles?.length ?? 0) > 0) {
    return holdsBackendRole(auth, OBSERVATION_CORRECTION_BACKEND_ROLES);
  }
  if (typeof auth.hasRole === "function") {
    return [
      "site_investigator",
      "investigator",
      "principal_investigator",
      "crc",
      "data_manager",
    ].some((role) => auth.hasRole!(role));
  }
  return false;
}

const PROTOCOL_GOVERNANCE_ROLES: ReadonlySet<string> = new Set([
  "sysadmin",
  "sponsor_designer",
  "sponsor_dm",
  "protocol_reviewer",
]);

const KERNEL_SITE_GOVERNANCE_ROLES: ReadonlySet<string> = new Set([
  "sysadmin",
  "sponsor_designer",
  "sponsor_dm",
]);

/** Submit protocol drafts for review, return to draft, approve via Part 11 signature, register evidence artifacts, and manage protocol effectivities. */
export function canGovernProtocols(
  auth: AuthorityAuth | null | undefined
): boolean {
  if (!auth || auth.isAuthenticated === false) return false;
  if (holdsBackendRole(auth, PROTOCOL_GOVERNANCE_ROLES)) return true;
  if (typeof auth.hasRole === "function") {
    return ["study_designer", "designer", "data_manager", "sponsor_admin"].some(
      (role) => auth.hasRole!(role)
    );
  }
  return false;
}

/** Link and activate kernel study sites once a governing protocol version is effective. */
export function canManageKernelStudySites(
  auth: AuthorityAuth | null | undefined
): boolean {
  if (!auth || auth.isAuthenticated === false) return false;
  if (holdsBackendRole(auth, KERNEL_SITE_GOVERNANCE_ROLES)) return true;
  if (typeof auth.hasRole === "function") {
    return ["study_designer", "designer", "data_manager", "sponsor_admin"].some(
      (role) => auth.hasRole!(role)
    );
  }
  return false;
}
