import { getActivePinia, type Pinia } from "pinia";
import type {
  LocationQueryRaw,
  RouteLocationNormalizedLoaded,
  Router,
} from "vue-router";
import {
  registerLogoutCleanup,
  registerSessionCacheClearHandler,
} from "../auth/sessionCoordinator.js";
import { shouldUseDemoDefaults, useClinicalScopeStore } from "./clinicalScope";
import { useStudyCanvasStore } from "./studyCanvas";

function cleanScopeString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

export interface SyncWorkspaceScopeOptions {
  studyId?: string | null;
  siteId?: string | null;
  router?: Router | null;
  route?:
    | RouteLocationNormalizedLoaded
    | {
        query?: Record<string, unknown>;
        params?: Record<string, unknown>;
      }
    | null;
  syncRouteQuery?: boolean;
  siteQueryKey?: "siteId" | "site";
}

/**
 * Synchronizes active study and site scope across `studyCanvas`, `clinicalScope`
 * (plus any mounted `clinical` or `etmf` Pinia stores), and optionally updates the
 * active route's `studyId` / `siteId` query parameters without fabricating synthetic IDs.
 */
export function syncWorkspaceScope(options: SyncWorkspaceScopeOptions): {
  studyId: string;
  siteId: string;
} {
  const pinia = getActivePinia() as
    | (Pinia & {
        _s?: Map<
          string,
          {
            selectedStudyId?: string;
            selectedSiteId?: string;
            activeStudyId?: string;
            activeSiteId?: string;
          }
        >;
      })
    | undefined;
  const scopeStore = useClinicalScopeStore(pinia);
  const canvasStore = useStudyCanvasStore(pinia);
  const etmfInstance = pinia?._s?.get("etmf");
  const clinicalInstance = pinia?._s?.get("clinical");

  const prevStudy =
    cleanScopeString(scopeStore.activeStudyId) ||
    cleanScopeString(canvasStore.studyCode);

  const hasExplicitStudy = options.studyId !== undefined;
  const hasExplicitSite = options.siteId !== undefined;
  const nextStudy = hasExplicitStudy
    ? cleanScopeString(options.studyId)
    : prevStudy;
  const studyChanged = nextStudy !== prevStudy;

  let nextSite: string;
  if (hasExplicitSite) {
    nextSite = cleanScopeString(options.siteId);
  } else if (studyChanged) {
    nextSite = "";
  } else {
    nextSite = cleanScopeString(scopeStore.activeSiteId);
  }

  if (hasExplicitStudy) {
    scopeStore.activeStudyId = nextStudy;
    if (clinicalInstance) {
      clinicalInstance.activeStudyId = nextStudy;
    }
    if (canvasStore.studyCode !== nextStudy) {
      canvasStore.openStudy(nextStudy);
    }
  } else if (nextStudy && canvasStore.studyCode !== nextStudy) {
    canvasStore.openStudy(nextStudy);
  }

  if (hasExplicitSite || studyChanged) {
    scopeStore.activeSiteId = nextSite;
    if (clinicalInstance) {
      clinicalInstance.activeSiteId = nextSite;
    }
  }

  if (etmfInstance) {
    if (hasExplicitStudy || nextStudy) {
      etmfInstance.selectedStudyId = nextStudy;
    }
    if (hasExplicitSite || studyChanged) {
      etmfInstance.selectedSiteId = nextSite;
    }
  }

  if (options.syncRouteQuery && options.router && options.route) {
    const siteKey = options.siteQueryKey ?? "siteId";
    const currentQuery = options.route.query ?? {};
    const currentStudyQuery = cleanScopeString(currentQuery.studyId);
    const currentSiteQuery = cleanScopeString(currentQuery[siteKey]);
    if (currentStudyQuery !== nextStudy || currentSiteQuery !== nextSite) {
      const nextQuery: LocationQueryRaw = {
        ...(currentQuery as LocationQueryRaw),
      };
      if (nextStudy) {
        nextQuery.studyId = nextStudy;
      } else {
        delete nextQuery.studyId;
      }
      if (nextSite) {
        nextQuery[siteKey] = nextSite;
      } else {
        delete nextQuery[siteKey];
        if (siteKey === "siteId") {
          delete nextQuery.site;
        }
      }
      if (typeof options.router.replace === "function") {
        try {
          const result = options.router.replace({ query: nextQuery });
          if (result && typeof result.catch === "function") {
            result.catch(() => {});
          }
        } catch {
          // Ignore router errors in isolated component mounts.
        }
      }
    }
  }

  return { studyId: nextStudy, siteId: nextSite };
}

/**
 * Resolves initial study and site scope from props and URL query/params first,
 * falling back to active `studyCanvas` and `clinicalScope` state without fabricating IDs.
 */
export function resolveHydratedWorkspaceScope(
  route?: {
    params?: Record<string, unknown>;
    query?: Record<string, unknown>;
  } | null,
  props?: { studyId?: string | null; siteId?: string | null }
): { studyId: string; siteId: string } {
  const pinia = getActivePinia();
  const scopeStore = useClinicalScopeStore(pinia);
  const canvasStore = useStudyCanvasStore(pinia);
  const demoDefaults = shouldUseDemoDefaults(pinia);

  const explicitStudy =
    cleanScopeString(props?.studyId) ||
    cleanScopeString(route?.params?.studyCode) ||
    cleanScopeString(route?.query?.studyId) ||
    cleanScopeString(route?.query?.study_id) ||
    cleanScopeString(route?.query?.study);

  const canvasStudy = cleanScopeString(canvasStore.studyCode);
  const clinicalStudy = !demoDefaults
    ? cleanScopeString(scopeStore.activeStudyId)
    : canvasStudy
      ? cleanScopeString(scopeStore.activeStudyId)
      : "";

  const studyId = explicitStudy || canvasStudy || clinicalStudy;

  const explicitSite =
    cleanScopeString(props?.siteId) ||
    cleanScopeString(route?.query?.siteId) ||
    cleanScopeString(route?.query?.site_id) ||
    cleanScopeString(route?.query?.site);

  let fallbackSite = "";
  if (
    !explicitSite &&
    !explicitStudy &&
    studyId &&
    (!demoDefaults || Boolean(canvasStudy)) &&
    cleanScopeString(scopeStore.activeStudyId) === studyId
  ) {
    fallbackSite = cleanScopeString(scopeStore.activeSiteId);
  }

  return {
    studyId,
    siteId: explicitSite || fallbackSite,
  };
}

/**
 * Clears synchronized workspace study and site state on sign-out, session expiry,
 * persona switch, or tenant/account switch.
 */
export function clearSynchronizedWorkspaceScope(): void {
  const pinia = getActivePinia() as
    | (Pinia & {
        _s?: Map<string, Record<string, unknown>>;
      })
    | undefined;
  if (!pinia) return;
  const demoDefaults = shouldUseDemoDefaults(pinia);

  if ("clinicalScope" in pinia.state.value) {
    const scopeStore = useClinicalScopeStore(pinia);
    scopeStore.$reset();
    if (!demoDefaults) {
      scopeStore.activeStudyId = "";
      scopeStore.activeSiteId = "";
    }
  }

  const clinicalInstance = pinia._s?.get("clinical");
  if (clinicalInstance && !demoDefaults) {
    clinicalInstance.activeStudyId = "";
    clinicalInstance.activeSiteId = "";
  }

  const etmfInstance = pinia._s?.get("etmf");
  if (etmfInstance) {
    etmfInstance.selectedStudyId = "";
    etmfInstance.selectedSiteId = "";
  }
}

registerSessionCacheClearHandler(() => {
  clearSynchronizedWorkspaceScope();
});
registerLogoutCleanup(clearSynchronizedWorkspaceScope);
