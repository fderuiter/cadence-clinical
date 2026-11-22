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
  const params = (route as { params?: Record<string, unknown> }).params;
  const fromParams = normalizeStudyCode(params?.studyCode);
  if (fromParams) return fromParams;

  const query = (route as { query?: Record<string, unknown> }).query;
  const fromQuery = normalizeStudyCode(query?.studyId);
  if (fromQuery) return fromQuery;

  return "";
}

function resolveOptionalRoute():
  Pick<RouteLocationNormalizedLoaded, "params" | "query"> | undefined {
  const instance = getCurrentInstance();
  if (
    !instance ||
    typeof useRoute !== "function" ||
    !("$route" in instance.appContext.config.globalProperties)
  ) {
    return undefined;
  }
  try {
    return useRoute();
  } catch {
    return undefined;
  }
}

/**
 * Reactive composable keeping study scope unified across `/studies/:studyCode`,
 * `/ctms`, `/ssu`, `/ecrf`, `/etmf`, `/training`, `/overview`, `AppShell.vue`,
 * and `CommandPaletteOverlay.vue` in both directions.
 */
export function useActiveStudyScope() {
  const route = resolveOptionalRoute();
  const pinia = getActivePinia();
  const bridgeRef = ensureStudyScopeBridge(pinia);
  const localStudyCode = ref("");

  const routeStudyCode = computed(() => extractRouteStudyCode(route));

  const storeStudyCode = computed(
    () =>
      normalizeStudyCode(bridgeRef?.value) ||
      normalizeStudyCode(localStudyCode.value)
  );

  const activeStudyCode = computed(
    () => routeStudyCode.value || storeStudyCode.value
  );

  function setActiveStudyCode(nextStudyCode: unknown): string {
    const normalized = normalizeStudyCode(nextStudyCode);
    localStudyCode.value = normalized;
    return syncActiveStudyScope(normalized, pinia);
  }

  watch(
    routeStudyCode,
    (code) => {
      if (code) {
        localStudyCode.value = code;
        syncActiveStudyScope(code, pinia);
      }
    },
    { immediate: true }
  );

  if (bridgeRef) {
    watch(
      () => normalizeStudyCode(bridgeRef.value),
      (code) => {
        if (code) {
          syncActiveStudyScope(code, pinia);
        }
      },
      { immediate: true }
    );
  }

  return {
    activeStudyCode,
    setActiveStudyCode,
  };
}
