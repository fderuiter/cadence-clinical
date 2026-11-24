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
import {
  clearActiveStudyScope,
  getSyncedStudyScope,
  syncActiveStudyScope,
} from "./studyScopeBridge";

function cleanScopeString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

interface ExplicitWorkspaceScopeFlags {
  studyExplicit: boolean;
  siteExplicit: boolean;
}

const explicitScopeByPinia = new WeakMap<Pinia, ExplicitWorkspaceScopeFlags>();

function getOrCreateExplicitFlags(pinia: Pinia): ExplicitWorkspaceScopeFlags {
  let flags = explicitScopeByPinia.get(pinia);
  if (!flags) {
    flags = { studyExplicit: false, siteExplicit: false };
    explicitScopeByPinia.set(pinia, flags);
  }
  return flags;
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
            currentStudyId?: string;
            currentSiteId?: string;
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
  const demoDefaults = shouldUseDemoDefaults(pinia);
  const explicitFlags = pinia ? getOrCreateExplicitFlags(pinia) : null;
  const etmfInstance = pinia?._s?.get("etmf");
  const clinicalInstance = pinia?._s?.get("clinical");

  const prevStudy =
    cleanScopeString(scopeStore.activeStudyId) ||
    cleanScopeString(canvasStore.studyCode) ||
    cleanScopeString(getSyncedStudyScope(pinia));

  const hasExplicitStudy = options.studyId !== undefined;
  const hasExplicitSite = options.siteId !== undefined;
  const nextStudy = hasExplicitStudy
    ? cleanScopeString(options.studyId)
    : prevStudy;
  const studyChanged = nextStudy !== prevStudy;

  let nextSite: string;
  if (hasExplicitSite) {
    nextSite = cleanScopeString(options.siteId);
    if (explicitFlags) {
      explicitFlags.siteExplicit = Boolean(nextSite);
    }
  } else if (studyChanged) {
    nextSite = "";
    if (explicitFlags) {
      explicitFlags.siteExplicit = false;
    }
  } else if (demoDefaults && !explicitFlags?.siteExplicit) {
    nextSite = "";
  } else {
    nextSite = cleanScopeString(scopeStore.activeSiteId);
  }

  if (hasExplicitStudy && explicitFlags) {
    explicitFlags.studyExplicit = Boolean(nextStudy);
  }

  if (hasExplicitStudy) {
    scopeStore.activeStudyId = nextStudy;
    if (clinicalInstance) {
      clinicalInstance.activeStudyId = nextStudy;
    }
    if (canvasStore.studyCode !== nextStudy) {
      canvasStore.openStudy(nextStudy);
    }
    if (nextStudy) {
      syncActiveStudyScope(nextStudy, pinia);
    } else {
      clearActiveStudyScope();
    }
  } else if (nextStudy && canvasStore.studyCode !== nextStudy) {
    canvasStore.openStudy(nextStudy);
    syncActiveStudyScope(nextStudy, pinia);
  }

  if (hasExplicitSite || studyChanged) {
    scopeStore.activeSiteId = nextSite;
    if (clinicalInstance) {
      clinicalInstance.activeSiteId = nextSite;
    }
  }

  if (etmfInstance) {
    if (hasExplicitStudy || nextStudy) {
      etmfInstance.currentStudyId = nextStudy;
    }
    if (hasExplicitSite || studyChanged) {
      etmfInstance.currentSiteId = nextSite;
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
  const demoDefaults = shouldUseDemoDefaults(pinia);
  const explicitFlags = pinia ? explicitScopeByPinia.get(pinia) : undefined;
  const existingCanvas = (
    pinia as (Pinia & { _s?: Map<string, { studyCode?: string }> }) | undefined
  )?._s?.get("studyCanvas");
  const rawCanvasStudy = cleanScopeString(
    existingCanvas
      ? existingCanvas.studyCode
      : demoDefaults
        ? ""
        : useStudyCanvasStore(pinia).studyCode
  );
  const scopeStore = useClinicalScopeStore(pinia);
  if (!existingCanvas && !demoDefaults) {
    useStudyCanvasStore(pinia);
  }

  const explicitStudy =
    cleanScopeString(props?.studyId) ||
    cleanScopeString(route?.params?.studyCode) ||
    cleanScopeString(route?.query?.studyId) ||
    cleanScopeString(route?.query?.study_id) ||
    cleanScopeString(route?.query?.study);

  const canvasStudy =
    !demoDefaults || explicitFlags?.studyExplicit || Boolean(existingCanvas)
      ? rawCanvasStudy
      : "";
  const clinicalStudy = !demoDefaults
    ? cleanScopeString(scopeStore.activeStudyId)
    : canvasStudy || explicitFlags?.studyExplicit
      ? cleanScopeString(scopeStore.activeStudyId)
      : "";
  const bridgedStudy = !demoDefaults
    ? cleanScopeString(getSyncedStudyScope(pinia))
    : "";

  const studyId = explicitStudy || canvasStudy || clinicalStudy || bridgedStudy;

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
    (!demoDefaults || Boolean(explicitFlags?.siteExplicit)) &&
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
  explicitScopeByPinia.delete(pinia);
  const demoDefaults = shouldUseDemoDefaults(pinia);

  clearActiveStudyScope();

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
    etmfInstance.currentStudyId = "";
    etmfInstance.currentSiteId = "";
  }
}

registerSessionCacheClearHandler(() => {
  clearSynchronizedWorkspaceScope();
});
registerLogoutCleanup(clearSynchronizedWorkspaceScope);
