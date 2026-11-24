import { getActivePinia } from "pinia";
import {
  buildDataCollectionPath,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  resolveSiteReadinessPath,
} from "../canvas/palette";
import {
  DEFAULT_SETUP_TASKS,
  getPersonaChecklist,
} from "../catalog/staffPersonaChecklists.js";
import {
  ROLE_ALIASES,
  resolveLensEffectiveRoles,
} from "../catalog/staffPersonaCore.js";
import { DEMO_MODULE_CATALOG, isScreenAvailable } from "../router/availability";
import { isDemoBuild } from "../utils/demoMode";
import { scopedKey, storageScopeOf } from "../utils/userScopedStorage.js";
import { useAuthStore } from "./auth";

export { DEFAULT_SETUP_TASKS };

export function resolveStorePinia(store) {
  return store?._p || getActivePinia() || null;
}

export function getAuthStoreSafe(explicitPinia = null) {
  try {
    const targetPinia = explicitPinia || getActivePinia();
    return targetPinia ? useAuthStore(targetPinia) : useAuthStore();
  } catch {
    return null;
  }
}

const SITE_READINESS_SPONSOR_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "sponsor_admin",
];

const SITE_READINESS_SITE_ROLES = ["site_investigator", "crc"];

const DATA_COLLECTION_ROLES = [
  ...SITE_READINESS_SITE_ROLES,
  ...SITE_READINESS_SPONSOR_ROLES,
];

const AUDITOR_PAIR = ["auditor", "tmf_auditor"];

const OBSERVATION_ROLES = [...DATA_COLLECTION_ROLES, ...AUDITOR_PAIR];

const ETMF_ROLES = OBSERVATION_ROLES;

const DATA_LOCK_ROLES = [
  "data_manager",
  "sponsor_designer",
  "sponsor_admin",
  "super_admin",
];

const EXPORTS_ROLES = [...DATA_LOCK_ROLES, ...AUDITOR_PAIR];

const SAFETY_ROLES = [
  "safety_reviewer",
  "safety_physician",
  "qppv",
  "qppv_officer",
  "qualified_person_pv",
  "safety_officer",
  "sponsor_pv_officer",
  "sponsor_medical_monitor",
  "medical_monitor",
  "sponsor_admin",
  "data_manager",
  "super_admin",
];

const SITE_READINESS_ALL_ROLES = DATA_COLLECTION_ROLES;

const ROUTE_REQUIRED_ROLES = {
  study: null,
  overview: null,
  training: null,
  tickets: null,
  knowledge: null,
  ecrf: DATA_COLLECTION_ROLES,
  observation: OBSERVATION_ROLES,
  ctms: SITE_READINESS_SPONSOR_ROLES,
  ssu: SITE_READINESS_ALL_ROLES,
  etmf: ETMF_ROLES,
  "data-lock": DATA_LOCK_ROLES,
  exports: EXPORTS_ROLES,
  safety: SAFETY_ROLES,
};

const NON_ONBOARDING_ROUTES = new Set([
  "landing",
  "login",
  "forbidden",
  "unavailable",
]);

export function hasRequiredRole(userRoles, requiredRoles) {
  if (!requiredRoles || requiredRoles.length === 0) return true;
  return (userRoles || []).some((uRole) => {
    if (requiredRoles.includes(uRole)) return true;
    const aliases = ROLE_ALIASES[uRole] || [];
    return aliases.some((alias) => requiredRoles.includes(alias));
  });
}

export function effectiveRolesForPersona(personaKey, explicitAuthStore = null) {
  const authStore = explicitAuthStore || getAuthStoreSafe();
  return resolveLensEffectiveRoles(personaKey, authStore?.rawRoles || []);
}

export const effectiveRolesForPersonaLens = effectiveRolesForPersona;

function extractObservationRefFromTask(task) {
  const raw =
    typeof task === "string"
      ? task.trim()
      : typeof task?.route === "string"
        ? task.route.trim()
        : "";
  const cleanPath = raw.split("?")[0];
  if (!cleanPath.startsWith("/observations/")) return "";
  return decodeURIComponent(cleanPath.slice("/observations/".length)).trim();
}

function normalizeTaskRouteName(task) {
  if (typeof task === "object" && task?.routeName) return task.routeName;
  const raw =
    typeof task === "string"
      ? task.trim()
      : typeof task?.route === "string"
        ? task.route.trim()
        : "";
  if (!raw) return null;
  const cleanPath = raw.split("?")[0];
  if (cleanPath === "/studies" || cleanPath.startsWith("/studies/")) {
    return "study";
  }
  if (cleanPath === "/observations" || cleanPath.startsWith("/observations/")) {
    return "observation";
  }
  return cleanPath.startsWith("/") ? cleanPath.slice(1) : cleanPath;
}

function readOnboardingWorkspaceScope(explicitScope, explicitPinia) {
  if (explicitScope && typeof explicitScope === "object") {
    const siteId = String(explicitScope.siteId || "").trim();
    return {
      studyCode: String(explicitScope.studyCode || "").trim(),
      siteId,
      explicitSiteId:
        explicitScope.explicitSiteId !== undefined
          ? String(explicitScope.explicitSiteId || "").trim()
          : siteId,
      subjectCode: String(explicitScope.subjectCode || "").trim(),
      participantRef: String(explicitScope.participantRef || "").trim(),
      enrollmentId: String(explicitScope.enrollmentId || "").trim(),
      observationRef: String(explicitScope.observationRef || "").trim(),
    };
  }
  const piniaState = (explicitPinia || getActivePinia())?.state?.value;
  if (!piniaState) {
    return {
      studyCode: "",
      siteId: "",
      explicitSiteId: "",
      subjectCode: "",
      participantRef: "",
      enrollmentId: "",
      observationRef: "",
    };
  }
  const demo = isDemoBuild();
  const canvasState = piniaState.studyCanvas;
  const clinicalState = piniaState.clinicalScope ?? piniaState.clinical;
  const canvasStudy = String(canvasState?.studyCode || "").trim();
  const clinicalStudy = !demo
    ? String(clinicalState?.activeStudyId || "").trim()
    : "";
  const studyCode = canvasStudy || clinicalStudy;
  let siteId = "";
  if (studyCode) {
    if (canvasStudy === studyCode) {
      siteId = String(canvasState?.participantsFilter?.siteId || "").trim();
    }
    if (
      !siteId &&
      !demo &&
      String(clinicalState?.activeStudyId || "").trim() === studyCode
    ) {
      siteId = String(clinicalState?.activeSiteId || "").trim();
    }
  }
  return {
    studyCode,
    siteId,
    explicitSiteId: siteId,
    subjectCode: "",
    participantRef: "",
    enrollmentId: "",
    observationRef: "",
  };
}

export function resolveAuthorizedTaskRoute(
  task,
  userRoles,
  explicitScope,
  explicitPinia
) {
  if (!task) return null;
  const routeName = normalizeTaskRouteName(task);
  if (!routeName || NON_ONBOARDING_ROUTES.has(routeName)) return null;
  const scope = readOnboardingWorkspaceScope(explicitScope, explicitPinia);

  const studyCode = scope?.studyCode?.trim() || "";
  const siteId = scope?.siteId?.trim() || "";
  const explicitSiteId =
    scope?.explicitSiteId !== undefined ? scope.explicitSiteId.trim() : siteId;
  const subjectCode = scope?.subjectCode?.trim() || "";
  const participantRef = scope?.participantRef?.trim() || "";
  const enrollmentId = scope?.enrollmentId?.trim() || "";
  const observationRef = scope?.observationRef?.trim() || "";
  const workflowScope = { studyCode, siteId, subjectCode };

  if (routeName === "site-readiness") {
    return resolveSiteReadinessPath(userRoles, workflowScope);
  }
  if (routeName === "study") {
    return isScreenAvailable("study")
      ? buildStudiesWorkspacePath({ ...workflowScope, participantRef })
      : null;
  }
  if (routeName === "ecrf") {
    if (!hasRequiredRole(userRoles, DATA_COLLECTION_ROLES)) return null;
    if (isDemoBuild()) {
      return isScreenAvailable("ecrf")
        ? buildDataCollectionPath({
            studyCode,
            siteId: explicitSiteId,
            subjectCode,
            enrollmentId,
          })
        : null;
    }
    if (!isScreenAvailable("study")) return null;
    return studyCode || explicitSiteId || participantRef || subjectCode
      ? buildDataCollectionPath({
          studyCode,
          siteId: explicitSiteId,
          participantRef,
          subjectCode,
          enrollmentId,
        })
      : "/studies";
  }
  if (routeName === "observation") {
    if (!isTourRouteAuthorized("observation", userRoles)) return null;
    const resolvedObsRef =
      observationRef || extractObservationRefFromTask(task);
    if (resolvedObsRef) {
      return buildScopedWorkflowPath(
        `/observations/${encodeURIComponent(resolvedObsRef)}`,
        workflowScope
      );
    }
    if (isDemoBuild() && DEMO_MODULE_CATALOG.observation?.defaultPath) {
      return DEMO_MODULE_CATALOG.observation.defaultPath;
    }
    return isScreenAvailable("study")
      ? buildStudiesWorkspacePath({ ...workflowScope, participantRef })
      : "/observations/OBS-1001";
  }
  if (routeName in ROUTE_REQUIRED_ROLES) {
    return isTourRouteAuthorized(routeName, userRoles)
      ? buildScopedWorkflowPath(`/${routeName}`, workflowScope)
      : null;
  }
  const catalogEntry = isDemoBuild() ? DEMO_MODULE_CATALOG[routeName] : null;
  if (catalogEntry) {
    if (!isScreenAvailable(routeName)) return null;
    if (
      catalogEntry.requiresRole.length > 0 &&
      !hasRequiredRole(userRoles, catalogEntry.requiresRole)
    ) {
      return null;
    }
    if (catalogEntry.routeName === "observation") {
      return catalogEntry.defaultPath;
    }
    return buildScopedWorkflowPath(catalogEntry.defaultPath, workflowScope);
  }
  return null;
}

export function isTourRouteAuthorized(routeName, userRoles) {
  if (!routeName) return true;
  if (NON_ONBOARDING_ROUTES.has(routeName)) {
    return false;
  }
  if (!isScreenAvailable(routeName)) return false;
  if (routeName in ROUTE_REQUIRED_ROLES) {
    return hasRequiredRole(userRoles, ROUTE_REQUIRED_ROLES[routeName]);
  }
  const catalogEntry = isDemoBuild() ? DEMO_MODULE_CATALOG[routeName] : null;
  if (catalogEntry) {
    return (
      catalogEntry.requiresRole.length === 0 ||
      hasRequiredRole(userRoles, catalogEntry.requiresRole)
    );
  }
  return false;
}

export function cloneCatalogChecklist(personaKey, explicitAuthStore = null) {
  const authStore = explicitAuthStore || getAuthStoreSafe();
  const source = getPersonaChecklist(personaKey, authStore?.rawRoles || []);
  return source.map((task) => ({
    ...task,
    completed: Boolean(task.completed),
    keywords: Array.isArray(task.keywords) ? [...task.keywords] : [],
  }));
}

export function resolveOnboardingStorageKey(
  personaKey,
  field,
  explicitAuthStore = null
) {
  const authStore = explicitAuthStore || getAuthStoreSafe();
  const scope = storageScopeOf(authStore);
  return scope
    ? scopedKey(scope, "onboarding", personaKey, field)
    : `onboarding_${personaKey}_${field}`;
}

export function readOnboardingJsonArray(ls, key) {
  const raw = key ? ls.getItem(key) : null;
  if (raw === null) return null;
  try {
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function resolvePersonaKey(authStore) {
  return authStore?.currentPersona || "super_admin";
}

export function readPassiveOnboardingBypassOverview(
  pinia,
  personaKey,
  explicitAuthStore = null
) {
  const authStore = explicitAuthStore || getAuthStoreSafe(pinia);
  const targetPersona = personaKey || resolvePersonaKey(authStore);
  const onboardingState = pinia?.state?.value?.onboarding;
  if (
    onboardingState &&
    (!onboardingState.currentPersona ||
      onboardingState.currentPersona === targetPersona) &&
    typeof onboardingState.bypassOverview === "boolean"
  ) {
    return onboardingState.bypassOverview;
  }
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const key = resolveOnboardingStorageKey(
        targetPersona,
        "bypass_overview",
        authStore
      );
      return Boolean(key && window.localStorage.getItem(key) === "true");
    } catch {
      return false;
    }
  }
  return false;
}

export function readPassiveOnboardingTourActive(pinia, authStore) {
  const targetPersona = resolvePersonaKey(authStore);
  const onboardingState = pinia?.state?.value?.onboarding;
  if (
    onboardingState &&
    (!authStore?.currentPersona ||
      onboardingState.currentPersona === targetPersona)
  ) {
    return Boolean(onboardingState.isActive && !onboardingState.disabled);
  }
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const ls = window.localStorage;
      const isUnscoped = !storageScopeOf(authStore);
      const disabledKey = resolveOnboardingStorageKey(
        targetPersona,
        "disabled",
        authStore
      );
      const disabled =
        (disabledKey && ls.getItem(disabledKey) === "true") ||
        (isUnscoped &&
          targetPersona === "super_admin" &&
          ls.getItem("onboarding_disabled") === "true");
      if (disabled) return false;

      const activeKey = resolveOnboardingStorageKey(
        targetPersona,
        "is_active",
        authStore
      );
      const storedActive =
        (activeKey ? ls.getItem(activeKey) : null) ??
        (isUnscoped && targetPersona === "super_admin"
          ? ls.getItem("onboarding_is_active")
          : null);
      return storedActive === "true";
    } catch {
      return false;
    }
  }
  return false;
}

function readPersistedTasksForPersona(personaKey, authStore) {
  const baseTasks = cloneCatalogChecklist(personaKey, authStore);
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const ls = window.localStorage;
      const isUnscoped = !storageScopeOf(authStore);
      const completedKey = resolveOnboardingStorageKey(
        personaKey,
        "completed_tasks",
        authStore
      );
      const parsedCompleted = readOnboardingJsonArray(ls, completedKey);
      const storedCompletedIds = parsedCompleted
        ? parsedCompleted.filter((id) => typeof id === "string")
        : [];

      const tasksKey = resolveOnboardingStorageKey(
        personaKey,
        "tasks",
        authStore
      );
      const parsedTasks =
        readOnboardingJsonArray(ls, tasksKey) ??
        (isUnscoped && personaKey === "super_admin"
          ? readOnboardingJsonArray(ls, "onboarding_tasks")
          : null);

      if (parsedTasks && parsedTasks.length > 0) {
        return baseTasks.map((defTask) => {
          const found = parsedTasks.find((t) => t?.id === defTask.id);
          const completed = found
            ? Boolean(found.completed)
            : storedCompletedIds.includes(defTask.id);
          return { ...defTask, completed };
        });
      }
      if (storedCompletedIds.length > 0) {
        return baseTasks.map((defTask) => ({
          ...defTask,
          completed: storedCompletedIds.includes(defTask.id),
        }));
      }
    } catch {
      // Fall back to default persona checklist
    }
  }
  return baseTasks;
}

export function readPassiveOnboardingSummary(pinia, authStore) {
  const onboardingState = pinia?.state?.value?.onboarding;
  const activePersona = resolvePersonaKey(authStore);
  const useLiveState =
    onboardingState &&
    (!authStore?.currentPersona ||
      onboardingState.currentPersona === activePersona);
  const personaKey = useLiveState
    ? onboardingState.currentPersona || activePersona
    : activePersona;
  const roles = effectiveRolesForPersona(personaKey, authStore);
  const sourceTasks = useLiveState
    ? onboardingState.tasks || []
    : readPersistedTasksForPersona(personaKey, authStore);

  let totalTasksCount = 0;
  let completedTasksCount = 0;
  for (const task of sourceTasks) {
    if (resolveAuthorizedTaskRoute(task, roles, undefined, pinia)) {
      totalTasksCount += 1;
      if (task.completed) {
        completedTasksCount += 1;
      }
    }
  }

  return {
    totalTasksCount,
    completedTasksCount,
    isDrawerOpen: Boolean(onboardingState?.isDrawerOpen),
  };
}
