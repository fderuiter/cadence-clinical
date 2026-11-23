import { defineStore } from "pinia";
import {
  DEFAULT_DEMO_PARTICIPANT,
  DEFAULT_DEMO_SITE,
  DEMO_STUDY,
} from "demo-fixtures";
import { useAuthStore } from "./auth.js";
import { useToastStore } from "./toasts.js";
import { refuseUnbackedAction } from "../utils/unbackedActions.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { scopedKey, storageScopeOf } from "../utils/userScopedStorage.js";
import { executionApi, type SubjectSummary } from "../api/executionApi";
import type { NormalizedUsdm } from "./normalization";

export interface ClinicalFieldOption {
  value: string;
  label: string;
}

export interface ClinicalFieldValidation {
  required?: boolean;
  pattern?: string;
  message?: string;
  min?: number;
  max?: number;
}

export interface ClinicalField {
  id: string;
  label: string;
  type: string;
  gridSpan: number;
  cdash?: string;
  value?: string | number | boolean | null;
  validation?: ClinicalFieldValidation;
  relevant?: Record<string, unknown>;
  constraint?: Record<string, unknown>;
  options?: ClinicalFieldOption[];
}

export interface CtmsMilestone {
  id: string;
  type: string;
  plannedDate: string;
  actualDate: string;
  status: string;
}

export interface CtmsVisit {
  id: string;
  type: string;
  scheduledDate: string;
  actualDate: string;
  status: string;
  cra: string;
}

export interface CtmsAllocation {
  cra: string;
  activeAllocations: number;
  sites: string[];
  studies: string[];
}

export interface CtmsRecruitment {
  siteId: string;
  screened: number;
  enrolled: number;
  target: number;
}

export interface CtmsData {
  milestones: CtmsMilestone[];
  visits: CtmsVisit[];
  allocations: CtmsAllocation[];
  recruitment: CtmsRecruitment[];
}

export interface ClinicalQuery {
  id?: string;
  fieldId?: string;
  field_id?: string;
  status?: string;
  text?: string;
  query_text?: string;
  author?: string;
  created_at?: string;
  response?: string;
  response_text?: string;
  responded_by?: string;
  responded_at?: string;
  closed_by?: string;
  closed_at?: string;
}

export interface LedgerBlock {
  index: number;
  timestamp: string;
  action: string;
  details: Record<string, unknown>;
  reason: string;
  prevHash: string;
  hash: string;
  synced: boolean;
}

export interface IngestionItem {
  id: string;
  status: string;
  name?: string;
  cdash_domain?: string;
}

export interface CandidateDraft {
  id: string;
  status: string;
  items?: IngestionItem[];
  created_at?: string;
  updated_at?: string;
}

export interface IngestionJob {
  job_id: string;
  status: string;
  candidate_id: string;
  errors: string[] | null;
}

export interface SubjectFixture {
  /** The subject code shown to users (Execution's `subject_id`). */
  id: string;
  /** Execution's record id for the subject; absent on demo sample subjects. */
  recordId?: string;
  siteId?: string;
  site_id?: string;
  consentDate?: string;
  armId?: string;
  status: string;
  active_protocol_version: string;
  consentText?: string;
  consentColor?: string;
  category?: string;
  isGated: boolean;
  label?: string;
}

export interface ZodIssueLike {
  path?: ReadonlyArray<PropertyKey>;
  message: string;
}

export interface ZodErrorLike {
  issues?: ReadonlyArray<ZodIssueLike>;
  errors?: ReadonlyArray<ZodIssueLike>;
}

export interface MutationValidationInput {
  type?: string;
  id?: string;
  properties?: Record<string, unknown>;
}

export interface ClinicalScopeState {
  subjects: SubjectFixture[];
  /** Subjects in the active study on the server; the list holds the first page (#4828). */
  subjectsTotal: number;
  subjectsLoading: boolean;
  subjectsError: string | null;
  activeStudyId: string;
  activeSiteId: string;
  activeSubjectId: string;
  activeVisitId: string;
}

export interface ClinicalState extends ClinicalScopeState {
  currentUsdm: NormalizedUsdm;
  currentCtmsData: CtmsData;
  ecrfFields: ClinicalField[];
  formValues: Record<string, string | number | boolean | null | undefined>;
  fieldVisibility: Record<string, boolean>;
  formQueries: Record<string, ClinicalQuery>;
  ledgerBlocks: LedgerBlock[];

  // --- SoA State ---
  activeStudyVersionId: string;
  soaLoading: boolean;
  soaError: string | null;

  // --- Ingestion / Candidate Draft State ---
  candidateDraft: CandidateDraft | null;
  ingestionJobs: IngestionJob[];
  ingestionLoading: boolean;
  ingestionError: string | null;

  debouncedEvaluateRules?: () => void;
}

/** Maps an Execution subject summary onto the eCRF's subject shape. */
export function subjectFromExecution(summary: SubjectSummary): SubjectFixture {
  const isGated = summary.status === "RECONSENT_REQUIRED";
  return {
    id: summary.subject_id,
    recordId: summary.id,
    siteId: summary.site_id ?? undefined,
    status: summary.status,
    active_protocol_version: summary.active_protocol_version,
    isGated,
    consentText: isGated ? "Re-consent required" : undefined,
    consentColor: isGated ? "yellow" : undefined,
  };
}

export function getCurrentPersonaKey(): string {
  if (typeof window !== "undefined") {
    try {
      const authStore = useAuthStore();
      if (authStore && authStore.currentPersona) {
        return authStore.currentPersona;
      }
    } catch {
      // Pinia or auth store may not be initialized yet
    }
  }
  return "super_admin";
}

function currentStorageScope() {
  try {
    return storageScopeOf(useAuthStore());
  } catch {
    // Pinia or auth store may not be initialized yet
    return null;
  }
}

/**
 * The browser storage key for one persisted clinical field of the signed-in
 * account: `cadence_user:<tenant>:<user>:clinical:<persona>:<baseKey>`. Null
 * when no account with a tenant and user id is signed in (#5369), so callers
 * neither read nor write.
 */
export function getStorageKey(
  baseKey: string,
  personaKey?: string
): string | null {
  const persona = personaKey || getCurrentPersonaKey();
  return scopedKey(currentStorageScope(), "clinical", persona, baseKey);
}

export function readStorageItem(
  baseKey: string,
  personaKey?: string
): string | null {
  if (typeof window === "undefined" || !window.localStorage) return null;
  const key = getStorageKey(baseKey, personaKey);
  return key ? window.localStorage.getItem(key) : null;
}

/** Writes one persisted clinical field for the signed-in account, if any. */
export function writeStorageItem(baseKey: string, value: unknown): void {
  if (typeof window === "undefined" || !window.localStorage) return;
  const key = getStorageKey(baseKey);
  if (key) window.localStorage.setItem(key, JSON.stringify(value));
}

export function applyLocalReconsentUnlock(
  subjects: SubjectFixture[],
  subjectId: string
): SubjectFixture {
  let sub = subjects.find((s) => s.id === subjectId);
  if (!sub) {
    sub = {
      id: subjectId,
      status: "ACTIVE",
      active_protocol_version: "2.0.0",
      consentText: "Signed ICF v2.0.0",
      consentColor: "green",
      category: "migrated",
      isGated: false,
      label: `${subjectId} (Re-Consented)`,
    };
    subjects.push(sub);
  } else {
    sub.isGated = false;
    sub.active_protocol_version = "2.0.0";
    sub.consentText = "Signed ICF v2.0.0";
    sub.consentColor = "green";
    sub.category = "migrated";
  }
  return sub;
}

export const useClinicalScopeStore = defineStore("clinicalScope", {
  state: (): ClinicalScopeState => {
    const demo = isDemoBuild();
    return {
      subjects: [],
      subjectsTotal: 0,
      subjectsLoading: false,
      subjectsError: null,
      activeStudyId: demo ? DEMO_STUDY.code : "",
      activeSiteId: demo ? DEFAULT_DEMO_SITE?.code || "" : "",
      activeSubjectId: demo
        ? DEFAULT_DEMO_PARTICIPANT?.subjectCode || ""
        : "SUBJ-001",
      activeVisitId: "Screening",
    };
  },
  getters: {
    user: () => {
      const authStore = useAuthStore();
      return {
        username: authStore.identity?.username || null,
        roles: authStore.normalizedRoles,
        authenticated: authStore.isAuthenticated,
      };
    },
    canManageQueries: () => {
      const authStore = useAuthStore();
      const roles = authStore ? authStore.normalizedRoles || [] : [];
      return roles.some((role: string) =>
        [
          "cra",
          "monitor",
          "data_manager",
          "sponsor_admin",
          "admin",
          "sponsor_designer",
        ].includes(role)
      );
    },
    getQueryLabel: () => {
      return (query: ClinicalQuery) => {
        const status =
          query && query.status ? query.status.toUpperCase() : "NONE";
        const authStore = useAuthStore();
        if (!authStore || !authStore.isAuthenticated) {
          return status;
        }
        const roles = authStore.normalizedRoles || [];
        if (roles.length === 0) {
          return status;
        }
        const isSite =
          roles.includes("site_investigator") || roles.includes("crc");
        const isMonitor = roles.includes("cra") || roles.includes("monitor");

        if (status === "OPEN" || status === "REOPENED") {
          if (isSite) return "Awaiting Site Action";
          if (isMonitor) return "Awaiting Site Response";
        } else if (status === "ANSWERED") {
          if (isSite) return "Submitted to CRA";
          if (isMonitor) return "Awaiting CRA Review";
        }
        return status;
      };
    },
    gatedSubjectIds: (state) => {
      return state.subjects.filter((s) => s.isGated).map((s) => s.id);
    },
    subjectIds: (state) => {
      return state.subjects.map((s) => s.id);
    },
    isSubjectGated: (state) => (subjectId: string) => {
      const sub = state.subjects.find((s) => s.id === subjectId);
      return sub ? sub.isGated : false;
    },
  },
  actions: {
    /**
     * Loads the first page of a study's subjects from Execution (GET /api/v1/execution/subjects).
     * A demo build keeps its sample subjects. On failure the list is left as it was and the error
     * is shown; sample subjects are never substituted.
     */
    async loadSubjects(studyId: string, limit = 200): Promise<boolean> {
      if (isDemoBuild()) return true;
      const study = studyId.trim();
      if (!study) {
        this.subjects = [];
        this.subjectsTotal = 0;
        this.subjectsError = null;
        return false;
      }
      this.subjectsLoading = true;
      this.subjectsError = null;
      try {
        const page = await executionApi.listSubjects({ studyId: study, limit });
        this.subjects = page.items.map(subjectFromExecution);
        this.subjectsTotal = Number(page.total);
        return true;
      } catch (err) {
        this.subjectsError =
          err instanceof Error ? err.message : "Could not load subjects.";
        useToastStore().apiError("Couldn't load the study's subjects", err);
        return false;
      } finally {
        this.subjectsLoading = false;
      }
    },
    /**
     * Screening evaluates a subject's eligibility against the protocol's criteria. No Cadence service serves
     * that evaluation yet, so this refuses in every build and changes nothing: an eligibility result is never
     * made up in the browser (#4523, #4828).
     */
    screenSubject(): null {
      refuseUnbackedAction("Screening a subject");
      return null;
    },
  },
});
