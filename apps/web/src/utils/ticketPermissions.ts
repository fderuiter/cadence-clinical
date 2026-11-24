import { computed, type ComputedRef } from "vue";
import { normalizeBackendRole } from "../canvas/authority/roles";

export const VERIFIED_TRIAL_TENANT_RE = /^trial-[0-9a-f]{12}$/;

export const AUDITOR_ROLES = new Set(["auditor", "tmf_auditor"]);

export const OPERATIONAL_MUTATION_ROLES = new Set([
  "sysadmin",
  "sponsor_admin",
  "super_admin",
  "system_admin",
  "admin",
  "sponsor_designer",
  "study_designer",
  "sponsor_dm",
  "data_manager",
  "cra",
  "cra_monitor",
  "monitor",
  "external_monitor",
  "safety_reviewer",
  "safety_officer",
  "safety_physician",
  "pharmacovigilance",
  "sponsor_mm",
  "medical_monitor",
  "sponsor_statistician",
  "biostatistician",
  "statistician",
  "unblinded_statistician",
  "idmc",
  "protocol_reviewer",
  "finance_officer",
  "terminology_manager",
  "clinical_scientist",
  "medical_coder",
  "crc",
  "site_crc",
  "site_investigator",
  "principal_investigator",
  "authorized_er_physician",
  "lead_investigator",
  "pharmacist",
  "investigator",
  "pi",
]);

export const AUDITOR_TICKET_READONLY_EXPLANATION =
  "Read-only auditor access: logging or modifying clinical issues is disabled to preserve inspection independence.";

export interface TicketAuthStateLike {
  currentPersona?: string | null;
  rawRoles?: string[] | null;
  isDemoMode?: boolean;
  user?: {
    tenantId?: string | null;
  } | null;
}

export function isVerifiedTrialTenantId(tenantId?: string | null): boolean {
  return Boolean(tenantId && VERIFIED_TRIAL_TENANT_RE.test(tenantId));
}

export function evaluateCanMutateTicket(
  authStore?: TicketAuthStateLike | null
): boolean {
  if (!authStore) {
    return true;
  }
  if (authStore.currentPersona === "auditor") {
    return false;
  }
  const roles = (authStore.rawRoles || [])
    .map((role) => normalizeBackendRole(role))
    .filter(Boolean);
  const hasAuditorRole = roles.some((role) => AUDITOR_ROLES.has(role));
  if (hasAuditorRole && !authStore.isDemoMode) {
    return false;
  }
  const verifiedTrialTenant = isVerifiedTrialTenantId(authStore.user?.tenantId);
  if (
    roles.length > 0 &&
    !roles.some(
      (role) =>
        OPERATIONAL_MUTATION_ROLES.has(role) ||
        (role === "trial_coordinator" && verifiedTrialTenant)
    )
  ) {
    return false;
  }
  return true;
}

export function useTicketMutationPermission(authStore: TicketAuthStateLike): {
  canMutateTicket: ComputedRef<boolean>;
  isReadOnlyTicketViewer: ComputedRef<boolean>;
  auditorReadOnlyExplanation: string;
} {
  const canMutateTicket = computed(() => evaluateCanMutateTicket(authStore));
  const isReadOnlyTicketViewer = computed(() => !canMutateTicket.value);
  return {
    canMutateTicket,
    isReadOnlyTicketViewer,
    auditorReadOnlyExplanation: AUDITOR_TICKET_READONLY_EXPLANATION,
  };
}
