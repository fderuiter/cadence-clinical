import { getActivePinia } from "pinia";
import {
  buildDataCollectionPath,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  resolveSiteReadinessPath,
} from "../canvas/palette";
import { DEMO_MODULE_CATALOG, isScreenAvailable } from "../router/availability";
import { isDemoBuild } from "../utils/demoMode";
import { PERSONA_PRESETS, ROLE_ALIASES, useAuthStore } from "./auth";

export function resolveStorePinia(store) {
  return store?._p || getActivePinia() || null;
}

const DATA_COLLECTION_ROLES = [
  "site_investigator",
  "crc",
  "data_manager",
  "sponsor_admin",
  "cra",
  "monitor",
];

const SITE_READINESS_SPONSOR_ROLES = [
  "cra",
  "monitor",
  "data_manager",
  "sponsor_admin",
];
const SITE_READINESS_SITE_ROLES = ["site_investigator", "crc"];
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
  const preset = PERSONA_PRESETS.find((p) => p.key === personaKey);
  try {
    const authStore = explicitAuthStore || useAuthStore();
    if (authStore?.rawRoles?.length > 0) {
      const roles = authStore.normalizedRoles || [];
      return preset &&
        personaKey !== "super_admin" &&
        roles.includes("sponsor_admin")
        ? preset.requires
        : roles;
    }
  } catch {
    // Pinia or auth store may not be initialized yet
  }
  return preset?.requires || ["sponsor_admin"];
}

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
  if (cleanPath === "/overview") return "overview";
  if (cleanPath === "/ecrf") return "ecrf";
  if (cleanPath === "/ctms") return "ctms";
  if (cleanPath === "/ssu") return "ssu";
  return cleanPath.startsWith("/") ? cleanPath.slice(1) : cleanPath;
}

export function resolveAuthorizedTaskRoute(task, userRoles, scope) {
  if (!task) return null;
  const routeName = normalizeTaskRouteName(task);
  if (!routeName || NON_ONBOARDING_ROUTES.has(routeName)) return null;

  const studyCode = scope?.studyCode?.trim() || "";
  const siteId = scope?.siteId?.trim() || "";
  const explicitSiteId =
    scope?.explicitSiteId !== undefined ? scope.explicitSiteId.trim() : siteId;
  const subjectCode = scope?.subjectCode?.trim() || "";
  const participantRef = scope?.participantRef?.trim() || "";
  const enrollmentId = scope?.enrollmentId?.trim() || "";

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
  if (routeName === "overview") {
    return isScreenAvailable("overview")
      ? buildScopedWorkflowPath("/overview", {
          studyCode,
          siteId,
          subjectCode,
        })
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
    return resolveSiteReadinessPath(userRoles, {
      studyCode,
      siteId,
      subjectCode,
    });
  }
  if (routeName === "ctms") {
    return isScreenAvailable("ctms") &&
      hasRequiredRole(userRoles, SITE_READINESS_SPONSOR_ROLES)
      ? buildScopedWorkflowPath("/ctms", {
          studyCode,
          siteId,
          subjectCode,
        })
      : null;
  }
  if (routeName === "ssu") {
    return isScreenAvailable("ssu") &&
      hasRequiredRole(userRoles, [
        ...SITE_READINESS_SPONSOR_ROLES,
        ...SITE_READINESS_SITE_ROLES,
      ])
      ? buildScopedWorkflowPath("/ssu", {
          studyCode,
          siteId,
          subjectCode,
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
    return catalogEntry.defaultPath;
  }
  return null;
}

export function isTourRouteAuthorized(routeName, userRoles) {
  if (!routeName) return true;
  if (NON_ONBOARDING_ROUTES.has(routeName)) return false;
  if (!isScreenAvailable(routeName)) return false;
  if (routeName === "study" || routeName === "overview") return true;
  if (routeName === "ecrf") {
    return hasRequiredRole(userRoles, DATA_COLLECTION_ROLES);
  }
  if (routeName === "observation") {
    const obsRoles = DEMO_MODULE_CATALOG.observation?.requiresRole || [
      "site_investigator",
      "crc",
      "data_manager",
    ];
    return hasRequiredRole(userRoles, obsRoles);
  }
  if (routeName === "ctms") {
    return hasRequiredRole(userRoles, SITE_READINESS_SPONSOR_ROLES);
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

export const DEFAULT_SETUP_TASKS = [
  {
    id: "task_protocol",
    title: "Open a Study Workspace",
    description:
      "Open a study by code to inspect lifecycle stages, participants, and activity",
    routeName: "study",
    route: "/studies",
    completed: false,
    keywords: ["study", "studies", "protocol", "welcome"],
  },
  {
    id: "task_soa",
    title: "Review Study Lifecycle & Protocol Versions",
    description:
      "Navigate Design through Submit stages and inspect recorded protocol versions",
    routeName: "study",
    route: "/studies",
    completed: false,
    keywords: [
      "lifecycle",
      "design",
      "version",
      "soa",
      "schedule",
      "arm",
      "epoch",
    ],
  },
  {
    id: "task_crf",
    title: "Verify Data Collection Forms",
    description:
      "Inspect clinical case report forms, field validations, and discrepancy queries",
    routeName: "ecrf",
    route: "/ecrf",
    completed: false,
    keywords: ["data", "collection", "crf", "ecrf", "form"],
  },
  {
    id: "task_rules",
    title: "Check Site Readiness",
    description:
      "Review site activation milestones and operational startup readiness",
    routeName: "site-readiness",
    route: "/ctms",
    completed: false,
    keywords: [
      "site",
      "readiness",
      "activation",
      "milestone",
      "rule",
      "rules",
      "query",
      "queries",
    ],
  },
  {
    id: "task_exports",
    title: "Configure Workspace Orientation",
    description:
      "Set your default landing preference and explore contextual SOP guidance",
    routeName: "overview",
    route: "/overview",
    completed: false,
    keywords: ["orientation", "overview", "preference", "help"],
  },
];

function resolvePersonaKey(authStore) {
  return authStore?.currentPersona || "super_admin";
}

export function readPassiveOnboardingBypassOverview(pinia, personaKey) {
  const onboardingState = pinia?.state?.value?.onboarding;
  if (onboardingState && typeof onboardingState.bypassOverview === "boolean") {
    return onboardingState.bypassOverview;
  }
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const key = `onboarding_${personaKey || "super_admin"}_bypass_overview`;
      return window.localStorage.getItem(key) === "true";
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
      const personaKey = resolvePersonaKey(authStore);
      const disabled =
        window.localStorage.getItem(`onboarding_${personaKey}_disabled`) ===
          "true" ||
        (personaKey === "super_admin" &&
          window.localStorage.getItem("onboarding_disabled") === "true");
      if (disabled) return false;
      const storedActive =
        window.localStorage.getItem(`onboarding_${personaKey}_is_active`) ??
        (personaKey === "super_admin"
          ? window.localStorage.getItem("onboarding_is_active")
          : null);
      return storedActive === "true";
    } catch {
      return false;
    }
  }
  return false;
}

function readPersistedTasksForPersona(personaKey) {
  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const raw =
        window.localStorage.getItem(`onboarding_${personaKey}_tasks`) ??
        (personaKey === "super_admin"
          ? window.localStorage.getItem("onboarding_tasks")
          : null);
      if (raw !== null) {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed) && parsed.length > 0) {
          return DEFAULT_SETUP_TASKS.map((defTask) => {
            const found = parsed.find((t) => t?.id === defTask.id);
            return found
              ? { ...defTask, completed: Boolean(found.completed) }
              : defTask;
          });
        }
      }
    } catch {
      // Fall back to default setup tasks
    }
  }
  return DEFAULT_SETUP_TASKS;
}

export function readPassiveOnboardingSummary(pinia, authStore) {
  const onboardingState = pinia?.state?.value?.onboarding;
  const personaKey =
    onboardingState?.currentPersona || resolvePersonaKey(authStore);
  const roles = effectiveRolesForPersona(personaKey, authStore);
  const sourceTasks = onboardingState
    ? onboardingState.tasks || []
    : readPersistedTasksForPersona(personaKey);

  let totalTasksCount = 0;
  let completedTasksCount = 0;
  for (const task of sourceTasks) {
    if (resolveAuthorizedTaskRoute(task, roles)) {
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
