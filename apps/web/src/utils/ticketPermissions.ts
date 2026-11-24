import { computed, type ComputedRef } from "vue";
import { normalizeRole } from "../stores/auth.js";

export const AUDITOR_ROLES = new Set(["auditor", "tmf_auditor"]);

export const OPERATIONAL_MUTATION_ROLES = new Set([
  "sponsor_admin",
  "super_admin",
  "system_admin",
  "admin",
  "sponsor_designer",
  "study_designer",
  "data_manager",
  "cra",
  "cra_monitor",
  "monitor",
  "safety_reviewer",
  "safety_officer",
  "safety_physician",
  "pharmacovigilance",
  "sponsor_mm",
  "medical_monitor",
  "biostatistician",
  "statistician",
  "clinical_scientist",
  "medical_coder",
  "crc",
  "site_crc",
  "site_investigator",
  "principal_investigator",
  "investigator",
  "pi",
]);

export const AUDITOR_TICKET_READONLY_EXPLANATION =
  "Read-only auditor access: logging or modifying clinical issues is disabled to preserve inspection independence.";

export interface TicketAuthStateLike {
  currentPersona?: string | null;
  rawRoles?: string[] | null;
  isDemoMode?: boolean;
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
    .map((role) => normalizeRole(role))
    .filter(Boolean);
  const hasAuditorRole = roles.some((role) => AUDITOR_ROLES.has(role));
  if (hasAuditorRole && !authStore.isDemoMode) {
    return false;
  }
  if (
    roles.length > 0 &&
    !roles.some((role) => OPERATIONAL_MUTATION_ROLES.has(role))
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
