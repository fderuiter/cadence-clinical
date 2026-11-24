import { getActivePinia } from "pinia";
import { PERSONA_SUMMARY_BY_KEY, getPersonaSummaryEntry } from "ui";
import {
  DATA_COLLECTION_ROLES,
  ETMF_ROLES,
  SITE_READINESS_SITE_ROLES,
  SITE_READINESS_SPONSOR_ROLES,
  buildDataCollectionPath,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  resolveSiteReadinessPath,
} from "../canvas/palette";
import { DEMO_MODULE_CATALOG, isScreenAvailable } from "../router/availability";
import { isDemoBuild } from "../utils/demoMode";
import { scopedKey, storageScopeOf } from "../utils/userScopedStorage.js";
import {
  ROLE_ALIASES,
  defaultPersonaKeyForRoles,
  resolveEffectivePersonaRoles,
  useAuthStore,
} from "./auth";

export {
  DATA_COLLECTION_ROLES,
  ETMF_ROLES,
  SITE_READINESS_SITE_ROLES,
  SITE_READINESS_SPONSOR_ROLES,
};

export const CTMS_AUTHORIZED_ROLES = [
  ...SITE_READINESS_SPONSOR_ROLES,
  "pharmacist",
  "trial_coordinator",
];

export const ONBOARDING_STORAGE_NAMESPACE = "onboarding";

export const SAFETY_ROLES = [
  "safety_reviewer",
  "safety_officer",
  "safety_physician",
  "medical_monitor",
  "sponsor_mm",
  "sponsor_pv_officer",
  "qppv",
  "qppv_officer",
  "qualified_person_pv",
  "data_manager",
  "sponsor_admin",
];

const AUDIT_ROLES = ["auditor", "tmf_auditor", "sponsor_admin"];
const MDR_ROLES = [
  "sponsor_designer",
  "protocol_architect",
  "data_manager",
  "sponsor_admin",
];
const RULES_ROLES = ["sponsor_designer", "data_manager", "sponsor_admin"];

const STUDY_FALLBACK_TASK_ROLES = {
  safety: SAFETY_ROLES,
  audit: AUDIT_ROLES,
  mdr: MDR_ROLES,
  rules: RULES_ROLES,
};

const NON_ONBOARDING_ROUTES = new Set([
  "landing",
  "login",
  "forbidden",
  "unavailable",
]);

export function resolveStorePinia(store) {
  return store?._p || getActivePinia() || null;
}

export function hasRequiredRole(userRoles, requiredRoles) {
  if (!requiredRoles || requiredRoles.length === 0) return true;
  return (userRoles || []).some((uRole) => {
    if (requiredRoles.includes(uRole)) return true;
    const aliases = ROLE_ALIASES[uRole] || [];
    return aliases.some((alias) => requiredRoles.includes(alias));
  });
}

export function resolveCatalogKeyForPersona(personaKey) {
  if (personaKey && PERSONA_SUMMARY_BY_KEY[personaKey]) {
    return personaKey;
  }
  if (personaKey === "designer" || personaKey === "study_designer") {
    return "sponsor_designer";
  }
  if (personaKey === "crc") return "site_crc";
  if (personaKey === "principal_investigator") return "site_investigator";
  if (personaKey === "cra" || personaKey === "monitor") return "cra_monitor";
  if (
    personaKey === "safety_reviewer" ||
    personaKey === "safety_physician" ||
    personaKey === "medical_monitor" ||
    personaKey === "sponsor_mm"
  ) {
    return "safety_officer";
  }
  if (personaKey === "tmf_auditor") return "auditor";
  if (personaKey === "sponsor_admin") return "super_admin";
  return null;
}

export function resolveEffectivePersonaKey(personaKey, authStore) {
  const mappedKey = resolveCatalogKeyForPersona(personaKey);
  const rawRoles = authStore?.rawRoles || [];
  const normalizedRoles = authStore?.normalizedRoles || [];

  if (
    (!mappedKey || mappedKey === "super_admin") &&
    rawRoles.length > 0 &&
    !normalizedRoles.includes("sponsor_admin")
  ) {
    return defaultPersonaKeyForRoles(rawRoles);
  }
  return mappedKey || "super_admin";
}

export function getActivePersona(explicitAuthStore = null) {
  try {
    const authStore = explicitAuthStore || useAuthStore();
    if (authStore && authStore.currentPersona) {
      return authStore.currentPersona;
    }
  } catch {
    // Pinia or auth store may not be initialized yet
  }
  return "super_admin";
}

export function currentOnboardingScope(explicitAuthStore = null) {
  try {
    return storageScopeOf(explicitAuthStore || useAuthStore());
  } catch {
    return null;
  }
}

/**
 * Resolves the account-scoped storage key
 * `cadence_user:<tenant>:<user>:onboarding:<persona>:<baseKey>`, or `null`
 * when no authenticated account with both `tenantId` and `userId` is active.
 */
export function getOnboardingStorageKey(
  baseKey,
  personaKey,
  explicitAuthStore = null
) {
  const persona = personaKey || getActivePersona(explicitAuthStore);
  return scopedKey(
    currentOnboardingScope(explicitAuthStore),
    ONBOARDING_STORAGE_NAMESPACE,
    persona,
    baseKey
  );
}

export function effectiveRolesForPersona(personaKey, explicitAuthStore = null) {
  try {
    const authStore = explicitAuthStore || useAuthStore();
    return resolveEffectivePersonaRoles(
      authStore?.rawRoles,
      personaKey || authStore?.currentPersona || "super_admin",
      { isAuthenticated: Boolean(authStore?.isAuthenticated) }
    );
  } catch {
    const entry = getPersonaSummaryEntry(personaKey);
    return entry ? [...entry.requires] : ["sponsor_admin"];
  }
}

const OPEN_SCOPED_TASK_ROUTES = new Set([
  "overview",
  "training",
  "tickets",
  "knowledge",
]);

function normalizeTaskRouteName(task) {
  if (task?.routeName) return task.routeName;
  const rawRoute = typeof task?.route === "string" ? task.route.trim() : "";
  if (!rawRoute) return null;
  const cleanPath = rawRoute.split("?")[0];
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
  const scopedQuery = { studyCode, siteId, subjectCode };

  if (routeName === "study") {
    return isScreenAvailable("study")
      ? buildStudiesWorkspacePath({
          studyCode,
          siteId,
          subjectCode,
          participantRef,
        })
      : null;
  }
  if (OPEN_SCOPED_TASK_ROUTES.has(routeName)) {
    return isScreenAvailable(routeName)
      ? buildScopedWorkflowPath(`/${routeName}`, scopedQuery)
      : null;
  }
  if (routeName === "ecrf") {
    if (isDemoBuild()) {
      return isScreenAvailable("ecrf") &&
        hasRequiredRole(userRoles, DATA_COLLECTION_ROLES)
        ? buildDataCollectionPath({
            studyCode,
            siteId: explicitSiteId,
            subjectCode,
            enrollmentId,
          })
        : null;
    }
    if (
      !isScreenAvailable("study") ||
      !hasRequiredRole(userRoles, DATA_COLLECTION_ROLES)
    ) {
      return null;
    }
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
  if (routeName === "site-readiness") {
    return resolveSiteReadinessPath(userRoles, scopedQuery);
  }
  if (routeName === "ctms") {
    return isScreenAvailable("ctms") &&
      hasRequiredRole(userRoles, CTMS_AUTHORIZED_ROLES)
      ? buildScopedWorkflowPath("/ctms", scopedQuery)
      : null;
  }
  if (routeName === "ssu") {
    return isScreenAvailable("ssu") &&
      hasRequiredRole(userRoles, [
        ...SITE_READINESS_SPONSOR_ROLES,
        ...SITE_READINESS_SITE_ROLES,
      ])
      ? buildScopedWorkflowPath("/ssu", scopedQuery)
      : null;
  }
  if (routeName === "etmf") {
    return isScreenAvailable("etmf") && hasRequiredRole(userRoles, ETMF_ROLES)
      ? buildScopedWorkflowPath("/etmf", scopedQuery)
      : null;
  }
  const studyFallbackRoles = STUDY_FALLBACK_TASK_ROLES[routeName];
  if (studyFallbackRoles) {
    if (!hasRequiredRole(userRoles, studyFallbackRoles)) return null;
    if (isScreenAvailable(routeName)) {
      return buildScopedWorkflowPath(`/${routeName}`, scopedQuery);
    }
    return isScreenAvailable("study")
      ? buildStudiesWorkspacePath({
          studyCode,
          siteId,
          subjectCode,
          participantRef,
        })
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
    return catalogEntry.routeName === "observation"
      ? catalogEntry.defaultPath
      : buildScopedWorkflowPath(catalogEntry.defaultPath, scopedQuery);
  }
  return null;
}

export function isTourRouteAuthorized(routeName, userRoles) {
  if (!routeName) return true;
  if (NON_ONBOARDING_ROUTES.has(routeName)) return false;
  if (routeName === "safety") {
    if (!hasRequiredRole(userRoles, SAFETY_ROLES)) return false;
    return isScreenAvailable("safety") || isScreenAvailable("study");
  }
  if (!isScreenAvailable(routeName)) return false;
  if (
    routeName === "study" ||
    routeName === "overview" ||
    routeName === "training" ||
    routeName === "tickets" ||
    routeName === "knowledge"
  ) {
    return true;
  }
  if (routeName === "etmf") {
    return hasRequiredRole(userRoles, ETMF_ROLES);
  }
  if (routeName === "ecrf") {
    return hasRequiredRole(userRoles, DATA_COLLECTION_ROLES);
  }
  if (routeName === "observation") {
    const obsRoles = DEMO_MODULE_CATALOG.observation?.requiresRole || [
      "principal_investigator",
      "site_investigator",
      "crc",
      "data_manager",
    ];
    return hasRequiredRole(userRoles, obsRoles);
  }
  if (routeName === "ctms") {
    return hasRequiredRole(userRoles, CTMS_AUTHORIZED_ROLES);
  }
  if (routeName === "ssu") {
    return hasRequiredRole(userRoles, [
      ...SITE_READINESS_SPONSOR_ROLES,
      ...SITE_READINESS_SITE_ROLES,
    ]);
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

function cloneSummaryTasks(personaKey) {
  const catalogKey = resolveCatalogKeyForPersona(personaKey) || "super_admin";
  const entry =
    getPersonaSummaryEntry(catalogKey) || PERSONA_SUMMARY_BY_KEY.super_admin;
  return (entry?.taskSummaries || []).map((task) => ({
    ...task,
    completed: false,
  }));
}

export function readPassiveOnboardingBypassOverview(
  pinia,
  personaKey,
  explicitAuthStore = null
) {
  const onboardingState = pinia?.state?.value?.onboarding;
  if (onboardingState && typeof onboardingState.bypassOverview === "boolean") {
    return onboardingState.bypassOverview;
  }
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const bypassKey = getOnboardingStorageKey(
        "bypass_overview",
        personaKey || getActivePersona(explicitAuthStore),
        explicitAuthStore
      );
      if (!bypassKey) return false;
      return window.localStorage.getItem(bypassKey) === "true";
    } catch {
      return false;
    }
  }
  return false;
}

export function readPassiveOnboardingTourActive(pinia, authStore) {
  const onboardingState = pinia?.state?.value?.onboarding;
  if (onboardingState) {
    return Boolean(onboardingState.isActive && !onboardingState.disabled);
  }
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const personaKey = getActivePersona(authStore);
      const disabledKey = getOnboardingStorageKey(
        "disabled",
        personaKey,
        authStore
      );
      if (!disabledKey) return false;
      if (window.localStorage.getItem(disabledKey) === "true") {
        return false;
      }
      const activeKey = getOnboardingStorageKey(
        "is_active",
        personaKey,
        authStore
      );
      return activeKey
        ? window.localStorage.getItem(activeKey) === "true"
        : false;
    } catch {
      return false;
    }
  }
  return false;
}

function readPersistedTasksForPersona(
  storagePersonaKey,
  catalogPersonaKey,
  authStore
) {
  const defaultTasks = cloneSummaryTasks(catalogPersonaKey);
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const tasksKey = getOnboardingStorageKey(
        "tasks",
        storagePersonaKey,
        authStore
      );
      if (!tasksKey) return defaultTasks;
      const raw = window.localStorage.getItem(tasksKey);
      if (raw !== null) {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed) && parsed.length > 0) {
          const completedById = new Map(
            parsed
              .filter((item) => item && typeof item.id === "string")
              .map((item) => [item.id, Boolean(item.completed)])
          );
          return defaultTasks.map((defTask) => ({
            ...defTask,
            completed: completedById.has(defTask.id)
              ? Boolean(completedById.get(defTask.id))
              : false,
          }));
        }
      }
    } catch {
      // Fall back to default persona catalog tasks
    }
  }
  return defaultTasks;
}

export function readPassiveOnboardingSummary(pinia, authStore) {
  const onboardingState = pinia?.state?.value?.onboarding;
  const explicitPersona =
    onboardingState?.currentPersona || getActivePersona(authStore);
  const effectivePersona = resolveEffectivePersonaKey(
    explicitPersona,
    authStore
  );
  const roles = effectiveRolesForPersona(effectivePersona, authStore);
  const sourceTasks = onboardingState
    ? onboardingState.tasks || []
    : readPersistedTasksForPersona(
        explicitPersona,
        effectivePersona,
        authStore
      );

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
