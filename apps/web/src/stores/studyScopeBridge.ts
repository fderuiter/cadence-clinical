import { ref, watch, type Ref } from "vue";
import {
  getActivePinia,
  setActivePinia,
  type Pinia,
  type StoreGeneric,
} from "pinia";

const scopeByPinia = new WeakMap<Pinia, Ref<string>>();
const installedPinias = new WeakSet<Pinia>();
const watchedStores = new WeakSet<object>();
let syncing = false;

export function normalizeStudyCode(value: unknown): string {
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === "string" ? item.trim() : "";
}

function getRegistry(pinia: Pinia): Map<string, StoreGeneric> | undefined {
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

const STORE_PROP_BY_ID: Record<string, string> = {
  studyCanvas: "studyCode",
  clinicalScope: "activeStudyId",
  clinical: "activeStudyId",
  etmf: "currentStudyId",
};

function attachWatcher(pinia: Pinia, store: StoreGeneric): void {
  const prop =
    store && typeof store === "object" && STORE_PROP_BY_ID[store.$id];
  if (!prop || watchedStores.has(store)) return;
  watchedStores.add(store);

  const read = () =>
    normalizeStudyCode((store as Record<string, unknown>)[prop]);
  const scopeRef = getPiniaStudyScopeRef(pinia);
  const initial = read();
  if (scopeRef && !scopeRef.value && initial) {
    scopeRef.value = initial;
  }
  watch(read, (code) => {
    if (code) syncActiveStudyScope(code, pinia);
  });
}

export function ensureStudyScopeBridge(
  pinia: Pinia | undefined = getActivePinia()
): Ref<string> | null {
  if (!pinia) return null;
  const scopeRef = getPiniaStudyScopeRef(pinia);
  if (!installedPinias.has(pinia)) {
    installedPinias.add(pinia);
    pinia.use(({ store }) => attachWatcher(pinia, store));
  }
  getRegistry(pinia)?.forEach((store) => attachWatcher(pinia, store));
  return scopeRef;
}

export function getSyncedStudyScope(
  pinia: Pinia | undefined = getActivePinia()
): string {
  return normalizeStudyCode(ensureStudyScopeBridge(pinia)?.value);
}

function applyScopeToStores(
  pinia: Pinia,
  code: string,
  forceClear = false
): void {
  const prevPinia = getActivePinia();
  syncing = true;
  try {
    const registry = getRegistry(pinia);
    const canvas = registry?.get("studyCanvas") as
      { studyCode?: string; openStudy?: (next: string) => void } | undefined;
    if (canvas && (forceClear || canvas.studyCode !== code)) {
      if (typeof canvas.openStudy === "function") {
        canvas.openStudy(code);
      } else {
        canvas.studyCode = code;
      }
    }
    for (const [id, prop] of [
      ["clinicalScope", "activeStudyId"],
      ["clinical", "activeStudyId"],
      ["etmf", "currentStudyId"],
    ] as const) {
      const store = registry?.get(id) as Record<string, unknown> | undefined;
      if (!store) continue;
      const previousStudy = normalizeStudyCode(store[prop]);
      const switchingFromAnotherStudy = Boolean(
        previousStudy && previousStudy !== code
      );
      if (forceClear || previousStudy !== code) {
        store[prop] = code;
      }
      if (forceClear || switchingFromAnotherStudy) {
        if (id === "clinicalScope" || id === "clinical") {
          store.activeSiteId = "";
          store.activeSubjectId = "";
        } else if (id === "etmf") {
          store.currentSiteId = "";
        }
      }
    }
  } finally {
    syncing = false;
    if (prevPinia && getActivePinia() !== prevPinia) {
      setActivePinia(prevPinia);
    }
  }
}

export function syncActiveStudyScope(
  rawStudyCode: unknown,
  explicitPinia?: Pinia
): string {
  const code = normalizeStudyCode(rawStudyCode);
  const pinia = explicitPinia ?? getActivePinia();
  if (!code || !pinia) return code;
  const scopeRef = ensureStudyScopeBridge(pinia);
  if (scopeRef) scopeRef.value = code;
  if (!syncing) applyScopeToStores(pinia, code);
  return code;
}

export function clearActiveStudyScope(): void {
  const pinia = getActivePinia();
  if (!pinia || syncing) return;
  const scopeRef = getPiniaStudyScopeRef(pinia);
  if (scopeRef) scopeRef.value = "";
  applyScopeToStores(pinia, "", true);
}
