import { toRefs } from "vue";
import { defineStore, getActivePinia, type Pinia, type StateTree } from "pinia";
import "pinia";
import { stateTrackingPlugin } from "./plugins.js";
import { registerClinicalSessionBridge } from "../auth/sessionCoordinator.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { normalizeUsdm } from "./normalization";
import {
  applyLocalReconsentUnlock,
  getCurrentPersonaKey,
  getStorageKey,
  readStorageItem,
  shouldUseDemoDefaults,
  subjectFromExecution,
  useClinicalScopeStore,
  writeStorageItem,
  type ClinicalQuery,
  type ClinicalScopeState,
  type ClinicalState,
  type LedgerBlock,
  type SubjectFixture,
} from "./clinicalScope";
import {
  DEFAULT_CLINICAL_USDM,
  buildMutationValidationPayload,
  cloneDefaultClinicalUsdm,
  createClinicalUsdmActions,
  createClinicalUsdmState,
  ensureUsdmSchemas,
  formatZodIssues,
  registerUsdmSchemas,
} from "./demo/clinicalUsdm";
import { createClinicalCtmsState } from "./demo/clinicalCtms";
import {
  DEFAULT_DEMO_SUBJECTS,
  DEFAULT_FORM_VALUES,
  createClinicalEcrfActions,
  createClinicalEcrfState,
} from "./demo/clinicalEcrf";
import {
  createClinicalLedgerActions,
  createClinicalLedgerState,
} from "./demo/clinicalLedger";
import {
  createClinicalIngestionActions,
  createClinicalIngestionState,
} from "./demo/clinicalIngestion";

declare module "pinia" {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  export interface DefineStoreOptionsBase<S extends StateTree, Store> {
    trackActions?: Record<string, { loading?: string; error?: string }>;
  }
}

export {
  DEFAULT_CLINICAL_USDM,
  DEFAULT_DEMO_SUBJECTS,
  DEFAULT_FORM_VALUES,
  buildMutationValidationPayload,
  cloneDefaultClinicalUsdm,
  ensureUsdmSchemas,
  formatZodIssues,
  getCurrentPersonaKey,
  getStorageKey,
  readStorageItem,
  registerUsdmSchemas,
  subjectFromExecution,
  useClinicalScopeStore,
};

export type {
  CandidateDraft,
  ClinicalField,
  ClinicalFieldOption,
  ClinicalFieldValidation,
  ClinicalQuery,
  ClinicalScopeState,
  ClinicalState,
  CtmsAllocation,
  CtmsData,
  CtmsMilestone,
  CtmsRecruitment,
  CtmsVisit,
  IngestionItem,
  IngestionJob,
  LedgerBlock,
  MutationValidationInput,
  SubjectFixture,
  ZodErrorLike,
  ZodIssueLike,
} from "./clinicalScope";

function readPersonaStorage(personaKey?: string) {
  const persona = personaKey || getCurrentPersonaKey();
  let savedSubjects: SubjectFixture[] | null = null;
  let savedFormValues: Record<
    string,
    string | number | boolean | null | undefined
  > | null = null;
  let savedFormQueries: Record<string, ClinicalQuery> | null = null;
  let savedLedgerBlocks: LedgerBlock[] | null = null;
  let savedUsdm: unknown = null;

  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const storedSubjects = readStorageItem("subjects", persona);
      if (storedSubjects && isDemoBuild()) {
        savedSubjects = JSON.parse(storedSubjects);
      }
      const storedFormValues = readStorageItem("formValues", persona);
      if (storedFormValues) savedFormValues = JSON.parse(storedFormValues);
      const storedFormQueries = readStorageItem("formQueries", persona);
      if (storedFormQueries) savedFormQueries = JSON.parse(storedFormQueries);
      const storedLedgerBlocks = readStorageItem("ledgerBlocks", persona);
      if (storedLedgerBlocks)
        savedLedgerBlocks = JSON.parse(storedLedgerBlocks);
      const storedUsdm = readStorageItem("currentUsdm", persona);
      if (storedUsdm) savedUsdm = JSON.parse(storedUsdm);
    } catch (e) {
      console.error("Failed to parse saved state from localStorage", e);
    }
  }

  return {
    savedSubjects,
    savedFormValues,
    savedFormQueries,
    savedLedgerBlocks,
    savedUsdm,
  };
}

function hydrateDemoSubjects(savedSubjects: SubjectFixture[] | null) {
  if (!isDemoBuild()) return [];
  return savedSubjects || JSON.parse(JSON.stringify(DEFAULT_DEMO_SUBJECTS));
}

const useClinicalStoreInner = defineStore("clinical", {
  state: (): ClinicalState => {
    const scope = useClinicalScopeStore();
    const initial = readPersonaStorage();
    const demo = isDemoBuild();
    const useDemoDefaults = shouldUseDemoDefaults();
    if (demo) {
      scope.subjects = hydrateDemoSubjects(initial.savedSubjects);
    }
    return {
      ...(toRefs(scope.$state) as unknown as ClinicalScopeState),
      ...createClinicalUsdmState(initial.savedUsdm, useDemoDefaults),
      ...createClinicalCtmsState(useDemoDefaults),
      ...createClinicalEcrfState(
        initial.savedFormValues,
        initial.savedFormQueries
      ),
      ...createClinicalLedgerState(initial.savedLedgerBlocks),
      ...createClinicalIngestionState(),
    };
  },
  trackActions: {
    fetchSoAProjection: { loading: "soaLoading", error: "soaError" },
    pushSoAMutation: { loading: "soaLoading", error: "soaError" },
    pushSoALink: { loading: "soaLoading", error: "soaError" },
    uploadProtocolDocument: {
      loading: "ingestionLoading",
      error: "ingestionError",
    },
  },
  getters: {
    user: () => useClinicalScopeStore().user,
    canManageQueries: () => useClinicalScopeStore().canManageQueries,
    getQueryLabel: () => useClinicalScopeStore().getQueryLabel,
    gatedSubjectIds: (state) =>
      state.subjects.filter((s) => s.isGated).map((s) => s.id),
    subjectIds: (state) => state.subjects.map((s) => s.id),
    isSubjectGated: (state) => (subjectId: string) => {
      const sub = state.subjects.find((s) => s.id === subjectId);
      return sub ? sub.isGated : false;
    },
  },
  actions: {
    async loadSubjects(studyId: string, limit = 200): Promise<boolean> {
      return useClinicalScopeStore().loadSubjects(studyId, limit);
    },
    screenSubject(): null {
      return useClinicalScopeStore().screenSubject();
    },
    loadPersonaState(personaKey?: string) {
      const loaded = readPersonaStorage(personaKey || getCurrentPersonaKey());
      const useDemoDefaults = shouldUseDemoDefaults();
      this.subjects = hydrateDemoSubjects(loaded.savedSubjects);
      this.formValues = loaded.savedFormValues || { ...DEFAULT_FORM_VALUES };
      this.formQueries = loaded.savedFormQueries || {};
      this.ledgerBlocks = loaded.savedLedgerBlocks || [];
      this.currentUsdm = loaded.savedUsdm
        ? normalizeUsdm(loaded.savedUsdm)
        : cloneDefaultClinicalUsdm(useDemoDefaults);
      this.evaluateRules();
    },
    async clearReconsentGate(
      subjectId: string,
      method: string = "ECONSENT",
      reason?: string
    ) {
      applyLocalReconsentUnlock(this.subjects, subjectId);
      const customReason =
        reason ||
        `Subject ${subjectId} re-consent recorded via ${method}. Gating unlocked.`;
      await this.addLedgerBlock(
        "RECONSENT_COMPLETED",
        { subject_id: subjectId, protocol_version: "2.0.0", method },
        customReason
      );
      if (typeof window !== "undefined" && window.localStorage) {
        writeStorageItem("subjects", this.subjects);
      }
    },
    ...createClinicalEcrfActions(),
    ...createClinicalLedgerActions(),
    ...createClinicalUsdmActions(),
    ...createClinicalIngestionActions(),
  },
});

const mountedClinicalStores = new WeakMap<
  object,
  ReturnType<typeof useClinicalStoreInner>
>();

export const useClinicalStore = (pinia?: unknown) => {
  const activePinia = ((pinia as Pinia | undefined) || getActivePinia()) as
    | (Pinia & {
        _hasStateTrackingPlugin?: boolean;
        _a?: unknown;
        install?: (app: unknown) => void;
      })
    | undefined;
  if (activePinia && !activePinia._hasStateTrackingPlugin) {
    activePinia.use(stateTrackingPlugin);
    if (!activePinia._a && typeof activePinia.install === "function") {
      activePinia.install({ provide() {}, config: { globalProperties: {} } });
    }
    activePinia._hasStateTrackingPlugin = true;
  }
  const scopeStore = useClinicalScopeStore(activePinia);
  const store = useClinicalStoreInner(activePinia) as ReturnType<
    typeof useClinicalStoreInner
  > & { _wrappedScopeReset?: boolean };
  if (!store._wrappedScopeReset) {
    const origReset = store.$reset.bind(store);
    store.$reset = () => {
      scopeStore.$reset();
      origReset();
    };
    store._wrappedScopeReset = true;
  }
  if (activePinia && typeof activePinia === "object") {
    mountedClinicalStores.set(activePinia, store);
  }
  return store;
};

registerClinicalSessionBridge({
  onRehydratePersona: (personaKey: string, pinia?: unknown) => {
    const targetPinia = (pinia as object) || getActivePinia();
    const store =
      targetPinia && typeof targetPinia === "object"
        ? mountedClinicalStores.get(targetPinia)
        : undefined;
    if (store && typeof store.loadPersonaState === "function") {
      store.loadPersonaState(personaKey);
    }
  },
  onReset: (pinia?: unknown) => {
    const targetPinia = (pinia as object) || getActivePinia();
    const store =
      targetPinia && typeof targetPinia === "object"
        ? mountedClinicalStores.get(targetPinia)
        : undefined;
    if (store && typeof store.$reset === "function") {
      store.$reset();
      return;
    }
    const mountedScope = (
      targetPinia as { _s?: Map<string, { $reset?: () => void }> } | undefined
    )?._s?.get("clinicalScope");
    mountedScope?.$reset?.();
  },
});
