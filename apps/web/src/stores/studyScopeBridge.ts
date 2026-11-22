import { ref, watch, type Ref } from "vue";
import {
  getActivePinia,
  setActivePinia,
  type Pinia,
  type StoreGeneric,
} from "pinia";

const scopeByPinia = new WeakMap<Pinia, Ref<string>>();
const pluginInstalledOnPinia = new WeakSet<Pinia>();
const watchedStores = new WeakSet<object>();
let syncingStudyScope = false;

export function normalizeStudyCode(value: unknown): string {
  if (Array.isArray(value)) {
    return normalizeStudyCode(value[0]);
  }
  return typeof value === "string" ? value.trim() : "";
}

function getPiniaRegistry(pinia: Pinia): Map<string, StoreGeneric> | undefined {
  return (pinia as unknown as { _s?: Map<string, StoreGeneric> })._s;
}

export function getPiniaStudyScopeRef(
  pinia: Pinia | undefined = getActivePinia()
): Ref<string> | null {
  if (!pinia) return null;
  let scopeRef = scopeByPinia.get(pinia);
  if (!scopeRef) {
    scopeRef = ref("");
    scopeByPinia.set(pinia, scopeRef);
  }
  return scopeRef;
}

function attachStoreStudyWatcher(pinia: Pinia, store: StoreGeneric): void {
  if (!store || typeof store !== "object" || watchedStores.has(store)) {
    return;
  }
  const id = store.$id;
  if (id !== "studyCanvas" && id !== "clinical" && id !== "etmf") {
    return;
  }
  watchedStores.add(store);

  const readStoreStudy = (): string => {
    if (id === "studyCanvas") {
      return normalizeStudyCode(
        (store as unknown as { studyCode?: unknown }).studyCode
      );
    }
    if (id === "clinical") {
      return normalizeStudyCode(
        (store as unknown as { activeStudyId?: unknown }).activeStudyId
      );
    }
    return normalizeStudyCode(
      (store as unknown as { currentStudyId?: unknown }).currentStudyId
    );
  };

  const scopeRef = getPiniaStudyScopeRef(pinia);
  const initialCode = readStoreStudy();
  if (scopeRef && !scopeRef.value && initialCode) {
    scopeRef.value = initialCode;
  }

  watch(readStoreStudy, (code) => {
    if (code) {
      syncActiveStudyScope(code, pinia);
    }
  });
}

export function ensureStudyScopeBridge(
  pinia: Pinia | undefined = getActivePinia()
): Ref<string> | null {
  if (!pinia) return null;
  const scopeRef = getPiniaStudyScopeRef(pinia);
  if (!pluginInstalledOnPinia.has(pinia)) {
    pluginInstalledOnPinia.add(pinia);
    pinia.use(({ store }) => {
      attachStoreStudyWatcher(pinia, store);
    });
  }
  const registry = getPiniaRegistry(pinia);
  if (registry) {
    for (const store of registry.values()) {
      attachStoreStudyWatcher(pinia, store);
    }
  }
  return scopeRef;
}

export function getSyncedStudyScope(
  pinia: Pinia | undefined = getActivePinia()
): string {
  const scopeRef = ensureStudyScopeBridge(pinia);
  return scopeRef ? normalizeStudyCode(scopeRef.value) : "";
}

/**
 * Synchronizes a non-empty study code across active Pinia stores
 * (`studyCanvas.studyCode`, `clinical.activeStudyId`, `etmf.currentStudyId`)
 * without importing store modules (avoiding circular module graphs or eager chunk loads).
 */
export function syncActiveStudyScope(
  rawStudyCode: unknown,
  explicitPinia?: Pinia
): string {
  const code = normalizeStudyCode(rawStudyCode);
  const pinia = explicitPinia ?? getActivePinia();
  if (!code || !pinia) {
    return code;
  }
  const scopeRef = ensureStudyScopeBridge(pinia);
  if (scopeRef) {
    scopeRef.value = code;
  }
  if (syncingStudyScope) {
    return code;
  }

  const previousActivePinia = getActivePinia();
  syncingStudyScope = true;
  try {
    const registry = getPiniaRegistry(pinia);
    const canvasStore = registry?.get("studyCanvas") as
      { studyCode?: string; openStudy?: (next: string) => void } | undefined;
    if (canvasStore && canvasStore.studyCode !== code) {
      if (typeof canvasStore.openStudy === "function") {
        canvasStore.openStudy(code);
      } else {
        canvasStore.studyCode = code;
      }
    }

    const clinicalStore = registry?.get("clinical") as
      { activeStudyId?: string } | undefined;
    if (clinicalStore && clinicalStore.activeStudyId !== code) {
      clinicalStore.activeStudyId = code;
    }

    const etmfStore = registry?.get("etmf") as
      { currentStudyId?: string } | undefined;
    if (etmfStore && etmfStore.currentStudyId !== code) {
      etmfStore.currentStudyId = code;
    }
  } finally {
    syncingStudyScope = false;
    if (previousActivePinia && getActivePinia() !== previousActivePinia) {
      setActivePinia(previousActivePinia);
    }
  }

  return code;
}

/**
 * Clears active study scope across active Pinia stores (e.g., on sign-out).
 */
export function clearActiveStudyScope(): void {
  const pinia = getActivePinia();
  if (!pinia || syncingStudyScope) return;
  const scopeRef = getPiniaStudyScopeRef(pinia);
  if (scopeRef) {
    scopeRef.value = "";
  }

  syncingStudyScope = true;
  try {
    const registry = getPiniaRegistry(pinia);
    const canvasStore = registry?.get("studyCanvas") as
      { studyCode?: string; openStudy?: (next: string) => void } | undefined;
    if (canvasStore) {
      if (typeof canvasStore.openStudy === "function") {
        canvasStore.openStudy("");
      } else {
        canvasStore.studyCode = "";
      }
    }

    const clinicalStore = registry?.get("clinical") as
      { activeStudyId?: string } | undefined;
    if (clinicalStore) {
      clinicalStore.activeStudyId = "";
    }

    const etmfStore = registry?.get("etmf") as
      { currentStudyId?: string } | undefined;
    if (etmfStore) {
      etmfStore.currentStudyId = "";
    }
  } finally {
    syncingStudyScope = false;
  }
}
