import { computed, getCurrentInstance, ref, watch } from "vue";
import { getActivePinia } from "pinia";
import { useRoute, type RouteLocationNormalizedLoaded } from "vue-router";
import {
  clearActiveStudyScope,
  ensureStudyScopeBridge,
  normalizeStudyCode,
  syncActiveStudyScope,
} from "../stores/studyScopeBridge";

export { clearActiveStudyScope, syncActiveStudyScope };

export function extractRouteStudyCode(
  route?:
    | Pick<RouteLocationNormalizedLoaded, "params" | "query">
    | Record<string, unknown>
    | null
): string {
  if (!route || typeof route !== "object") return "";
  const r = route as {
    params?: Record<string, unknown>;
    query?: Record<string, unknown>;
  };
  return (
    normalizeStudyCode(r.params?.studyCode) ||
    normalizeStudyCode(r.query?.studyId)
  );
}

function resolveOptionalRoute() {
  const inst = getCurrentInstance();
  if (
    !inst ||
    typeof useRoute !== "function" ||
    !("$route" in inst.appContext.config.globalProperties)
  ) {
    return undefined;
  }
  try {
    return useRoute();
  } catch {
    return undefined;
  }
}

export function useActiveStudyScope() {
  const route = resolveOptionalRoute();
  const pinia = getActivePinia();
  const bridgeRef = ensureStudyScopeBridge(pinia);
  const localCode = ref("");

  const routeCode = computed(() => extractRouteStudyCode(route));
  const activeStudyCode = computed(
    () =>
      routeCode.value ||
      normalizeStudyCode(bridgeRef?.value) ||
      normalizeStudyCode(localCode.value)
  );

  function setActiveStudyCode(nextStudyCode: unknown): string {
    const normalized = normalizeStudyCode(nextStudyCode);
    localCode.value = normalized;
    return syncActiveStudyScope(normalized, pinia);
  }

  function isOwnPiniaActive(): boolean {
    if (!pinia) return true;
    const current = getActivePinia();
    return !current || current === pinia;
  }

  watch(
    routeCode,
    (code) => {
      if (code && isOwnPiniaActive()) setActiveStudyCode(code);
    },
    { immediate: true }
  );

  if (bridgeRef) {
    watch(
      () => normalizeStudyCode(bridgeRef.value),
      (code) => {
        if (code && isOwnPiniaActive()) syncActiveStudyScope(code, pinia);
      },
      { immediate: true }
    );
  }

  return { activeStudyCode, setActiveStudyCode };
}
