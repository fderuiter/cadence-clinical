import { defineStore, getActivePinia } from "pinia";
import { registerLogoutCleanup } from "../auth/sessionCoordinator.js";
import {
  buildDataCollectionPath,
  buildScopedWorkflowPath,
  buildStudiesWorkspacePath,
  resolveSiteReadinessPath,
} from "../canvas/palette";
import {
  ROLE_ALIASES,
  resolveLensEffectiveRoles,
  resolvePersonaCatalogKey,
} from "../catalog/staffPersonaCore.js";
import {
  DEFAULT_SETUP_TASKS,
  PERSONA_TOUR_SEQUENCES,
  getPersonaOnboardingEntry,
} from "../catalog/staffPersonaOnboarding.js";
import { DEMO_MODULE_CATALOG, isScreenAvailable } from "../router/availability";
import {
  demoNowIso,
  demoNowMs,
  demoRandomToken,
  isDemoBuild,
  nextDemoStep,
} from "../utils/demoMode";
import {
  scopePrefix,
  scopedKey,
  storageScopeOf,
} from "../utils/userScopedStorage.js";
import { useAuthStore } from "./auth";

export { DEFAULT_SETUP_TASKS };

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

const LEGACY_FIELDS = [
  "current_step",
  "is_active",
  "disabled",
  "events",
  "tasks",
];

export const ROLE_STEP_SEQUENCES = PERSONA_TOUR_SEQUENCES;

export function hasRequiredRole(userRoles, requiredRoles) {
  if (!requiredRoles || requiredRoles.length === 0) return true;
  return (userRoles || []).some((uRole) => {
    if (requiredRoles.includes(uRole)) return true;
    const aliases = ROLE_ALIASES[uRole] || [];
    return aliases.some((alias) => requiredRoles.includes(alias));
  });
}

function getAuthStoreSafe() {
  try {
    return useAuthStore();
  } catch {
    return null;
  }
}

export function effectiveRolesForPersonaLens(personaKey, authStoreOverride) {
  const authStore = authStoreOverride || getAuthStoreSafe();
  return resolveLensEffectiveRoles(personaKey, authStore?.rawRoles || []);
}

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

function getActivePersona() {
  return getAuthStoreSafe()?.currentPersona || "super_admin";
}

function cloneCatalogChecklist(personaKey) {
  const authStore = getAuthStoreSafe();
  const entry = getPersonaOnboardingEntry(
    personaKey,
    authStore?.rawRoles || []
  );
  const source = entry?.checklist || DEFAULT_SETUP_TASKS;
  return source.map((task) => ({
    ...task,
    completed: Boolean(task.completed),
    keywords: Array.isArray(task.keywords) ? [...task.keywords] : [],
  }));
}

function resolveOnboardingKey(personaKey, field) {
  const scope = storageScopeOf(getAuthStoreSafe());
  return scope
    ? scopedKey(scope, "onboarding", personaKey, field)
    : `onboarding_${personaKey}_${field}`;
}

function migrateLegacyKeysIfUnscoped() {
  if (typeof window === "undefined" || !window.localStorage) return;
  const ls = window.localStorage;
  if (storageScopeOf(getAuthStoreSafe())) {
    for (const field of LEGACY_FIELDS) {
      ls.removeItem(`onboarding_${field}`);
    }
    return;
  }
  try {
    let hasLegacy = false;
    for (const field of LEGACY_FIELDS) {
      const val = ls.getItem(`onboarding_${field}`);
      if (val !== null) {
        hasLegacy = true;
        const dest = `onboarding_super_admin_${field}`;
        if (ls.getItem(dest) === null) {
          ls.setItem(dest, val);
        }
      }
    }
    if (hasLegacy) {
      for (const field of LEGACY_FIELDS) {
        ls.removeItem(`onboarding_${field}`);
      }
    }
  } catch (e) {
    console.error("Failed to migrate legacy onboarding storage keys", e);
  }
}

function readJsonArray(ls, key) {
  const raw = key ? ls.getItem(key) : null;
  if (raw === null) return null;
  try {
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function readStorageForPersona(personaKey) {
  let currentStep = 1;
  let isActive = false;
  let disabled = false;
  let bypassOverview = false;
  let events = [];
  let tasks = cloneCatalogChecklist(personaKey);

  if (typeof window !== "undefined" && window.localStorage) {
    try {
      const ls = window.localStorage;
      migrateLegacyKeysIfUnscoped();

      const storedStep = ls.getItem(
        resolveOnboardingKey(personaKey, "current_step")
      );
      if (storedStep !== null) {
        const parsed = parseInt(storedStep, 10);
        if (!isNaN(parsed) && parsed > 0) {
          currentStep = parsed;
        }
      }

      disabled =
        ls.getItem(resolveOnboardingKey(personaKey, "disabled")) === "true";
      bypassOverview =
        ls.getItem(resolveOnboardingKey(personaKey, "bypass_overview")) ===
        "true";
      const storedIsActive = ls.getItem(
        resolveOnboardingKey(personaKey, "is_active")
      );
      isActive = disabled ? false : storedIsActive === "true";

      const parsedCompleted = readJsonArray(
        ls,
        resolveOnboardingKey(personaKey, "completed_tasks")
      );
      const storedCompletedIds = parsedCompleted
        ? parsedCompleted.filter((id) => typeof id === "string")
        : [];

      const parsedEvents = readJsonArray(
        ls,
        resolveOnboardingKey(personaKey, "events")
      );
      if (parsedEvents) {
        events = parsedEvents;
      }

      const parsedTasks = readJsonArray(
        ls,
        resolveOnboardingKey(personaKey, "tasks")
      );
      if (parsedTasks && parsedTasks.length > 0) {
        tasks = tasks.map((defTask) => {
          const found = parsedTasks.find((t) => t.id === defTask.id);
          const completed = found
            ? Boolean(found.completed)
            : storedCompletedIds.includes(defTask.id);
          return { ...defTask, completed };
        });
      } else if (storedCompletedIds.length > 0) {
        tasks = tasks.map((defTask) => ({
          ...defTask,
          completed: storedCompletedIds.includes(defTask.id),
        }));
      }
    } catch (e) {
      console.error("Failed to parse onboarding state from localStorage", e);
    }
  }

  return {
    currentStep,
    isActive,
    disabled,
    bypassOverview,
    events,
    tasks,
  };
}

function buildOverviewFallbackSteps(baseSequence) {
  const firstStep = baseSequence?.[0];
  const lastStep = baseSequence?.[baseSequence.length - 1];
  return [
    {
      step: 1,
      routeName: "overview",
      targetSelector: null,
      title: firstStep?.title || "Welcome to Workspace Orientation",
      description:
        firstStep?.description ||
        "Review role-tailored orientation guidance and workspace preferences.",
      prompt:
        firstStep?.prompt ||
        "Explore your workspace orientation and contextual SOP resources.",
    },
    {
      step: 2,
      routeName: "overview",
      targetSelector: "#btn-contextual-help-trigger",
      title: lastStep?.title || "Tour Complete!",
      description:
        lastStep?.description ||
        "Your workspace orientation is complete. Open Help anytime for standard operating procedure guidance.",
      prompt:
        lastStep?.prompt ||
        "Use Search (⌘K) and Help from the persistent top bar anytime.",
    },
  ];
}

function clampStepToTotal(step, totalSteps) {
  const maxStep = Math.max(1, totalSteps || 1);
  const parsed = Number.isFinite(step) && step > 0 ? step : 1;
  return Math.min(parsed, maxStep);
}

function getSequenceForPersona(personaKey) {
  const authStore = getAuthStoreSafe();
  const roles = authStore?.normalizedRoles || [];
  const rawRoles = authStore?.rawRoles || [];
  let baseSequence;

  if (
    personaKey === "super_admin" &&
    authStore?.currentPersona === "super_admin" &&
    roles.includes("sponsor_admin")
  ) {
    baseSequence = ROLE_STEP_SEQUENCES.super_admin;
  } else if (
    personaKey &&
    personaKey !== "super_admin" &&
    ROLE_STEP_SEQUENCES[personaKey]
  ) {
    baseSequence = ROLE_STEP_SEQUENCES[personaKey];
  } else {
    const resolvedKey = resolvePersonaCatalogKey(
      personaKey === "super_admin" ? null : personaKey,
      rawRoles
    );
    baseSequence =
      ROLE_STEP_SEQUENCES[resolvedKey] ||
      ROLE_STEP_SEQUENCES[personaKey] ||
      ROLE_STEP_SEQUENCES.super_admin;
  }

  const effectiveRoles = effectiveRolesForPersonaLens(personaKey, authStore);
  const filtered = (baseSequence || [])
    .filter((step) => isTourRouteAuthorized(step.routeName, effectiveRoles))
    .map((step, idx) => ({ ...step, step: idx + 1 }));

  if (filtered.length > 0) {
    return filtered;
  }

  return buildOverviewFallbackSteps(baseSequence);
}

export const useOnboardingStore = defineStore("onboarding", {
  state: () => {
    const activePersona = getActivePersona();
    const initial = readStorageForPersona(activePersona);
    const initialSteps = getSequenceForPersona(activePersona);

    return {
      currentPersona: activePersona,
      currentStep: clampStepToTotal(initial.currentStep, initialSteps.length),
      isActive: initial.isActive,
      disabled: initial.disabled,
      bypassOverview: initial.bypassOverview,
      events: initial.events,
      tasks: initial.tasks,
      isDrawerOpen: false,
    };
  },
  getters: {
    visibleTasks(state) {
      const roles = effectiveRolesForPersonaLens(state.currentPersona);
      const storePinia =
        /** @type {{ _p?: import("pinia").Pinia } | undefined} */ (this)?._p;
      /** @type {typeof DEFAULT_SETUP_TASKS} */
      const visible = [];
      for (const task of state.tasks || []) {
        const resolvedRoute = resolveAuthorizedTaskRoute(
          task,
          roles,
          undefined,
          storePinia
        );
        if (resolvedRoute) {
          visible.push({ ...task, route: resolvedRoute });
        }
      }
      return visible;
    },
    completedTasks(state) {
      return (state.tasks || []).filter((t) => t.completed).map((t) => t.id);
    },
    completedTasksCount() {
      return this.visibleTasks.filter((t) => t.completed).length;
    },
    totalTasksCount() {
      return this.visibleTasks.length;
    },
    progressPercentage() {
      if (!this.visibleTasks.length) return 0;
      const completed = this.visibleTasks.filter((t) => t.completed).length;
      return Math.round((completed / this.visibleTasks.length) * 100);
    },
    isChecklistComplete() {
      return (
        this.visibleTasks.length > 0 &&
        this.visibleTasks.every((t) => t.completed)
      );
    },
    personaSteps(state) {
      return getSequenceForPersona(state.currentPersona);
    },
    currentStepConfig(state) {
      const steps = getSequenceForPersona(state.currentPersona);
      const clampedStep = clampStepToTotal(state.currentStep, steps.length);
      const stepIdx = Math.max(0, clampedStep - 1);
      return steps[stepIdx] || steps[0] || null;
    },
    totalSteps(state) {
      const steps = getSequenceForPersona(state.currentPersona);
      return steps.length || 1;
    },
  },
  actions: {
    loadPersonaState(personaKey) {
      const targetPersona = personaKey || getActivePersona();
      this.currentPersona = targetPersona;
      const loaded = readStorageForPersona(targetPersona);
      const steps = getSequenceForPersona(targetPersona);
      const clampedStep = clampStepToTotal(loaded.currentStep, steps.length);
      this.currentStep = clampedStep;
      this.isActive = loaded.isActive;
      this.disabled = loaded.disabled;
      this.bypassOverview = loaded.bypassOverview;
      this.events = loaded.events;
      this.tasks = loaded.tasks;
      if (clampedStep !== loaded.currentStep) {
        this.persist();
      }
    },
    clearSessionState() {
      this.currentPersona = "super_admin";
      this.currentStep = 1;
      this.isActive = false;
      this.disabled = false;
      this.bypassOverview = false;
      this.events = [];
      this.tasks = cloneCatalogChecklist("super_admin");
      this.isDrawerOpen = false;
    },
    persist() {
      if (typeof window !== "undefined" && window.localStorage) {
        try {
          const targetPersona = this.currentPersona || getActivePersona();
          const entries = [
            ["current_step", String(this.currentStep)],
            ["is_active", String(this.isActive)],
            ["disabled", String(this.disabled)],
            ["bypass_overview", String(this.bypassOverview)],
            ["completed_tasks", JSON.stringify(this.completedTasks)],
            ["events", JSON.stringify(this.events)],
            ["tasks", JSON.stringify(this.tasks)],
          ];
          for (const [field, value] of entries) {
            const key = resolveOnboardingKey(targetPersona, field);
            if (key) {
              window.localStorage.setItem(key, value);
            }
          }
        } catch (e) {
          console.error(
            "Failed to persist onboarding state to localStorage",
            e
          );
        }
      }
    },
    setBypassOverview(value) {
      this.bypassOverview = Boolean(value);
      this.addEvent(
        "preference_changed",
        "Bypass Overview Landing Preference",
        this.bypassOverview ? "Enabled bypass" : "Disabled bypass"
      );
    },
    toggleTaskCompletion(taskId) {
      if (taskId) this.toggleTask(taskId);
    },
    toggleDrawer(forceState) {
      this.isDrawerOpen =
        typeof forceState === "boolean" ? forceState : !this.isDrawerOpen;
    },
    openDrawer() {
      this.isDrawerOpen = true;
    },
    closeDrawer() {
      this.isDrawerOpen = false;
    },
    toggleTask(taskId, isCompleted) {
      const task = (this.tasks || []).find((t) => t.id === taskId);
      if (task) {
        task.completed =
          typeof isCompleted === "boolean" ? isCompleted : !task.completed;
        this.addEvent(
          "task_status_changed",
          `Task ${task.title}`,
          `Task set to ${task.completed ? "completed" : "incomplete"}`
        );
      }
    },
    completeTask(taskId) {
      this.toggleTask(taskId, true);
    },
    resetChecklist() {
      const targetPersona = this.currentPersona || getActivePersona();
      this.tasks = cloneCatalogChecklist(targetPersona);
      this.addEvent(
        "checklist_reset",
        "Onboarding Checklist Reset",
        "Checklist task completion reset"
      );
    },
    addEvent(type, target, details = "") {
      const step = nextDemoStep("onboarding-event");
      const event = {
        id: `ev-${demoNowMs(step)}-${demoRandomToken(4, 36, "onboarding-event")}`,
        timestamp: demoNowIso(step),
        type,
        target,
        details,
      };
      this.events.push(event);
      this.matchTelemetryToTasks(event);
      this.persist();
    },
    matchTelemetryToTasks(event) {
      if (!this.tasks || !event || !event.type) return;
      if (
        event.type.startsWith("checklist_") ||
        event.type.startsWith("task_status_") ||
        event.type.startsWith("tour_")
      ) {
        return;
      }
      const searchStr =
        `${event.type} ${event.target} ${event.details}`.toLowerCase();
      this.tasks.forEach((task) => {
        if (
          !task.completed &&
          task.keywords &&
          task.keywords.some((kw) => {
            const regex = new RegExp(`\\b${kw}\\b`, "i");
            return regex.test(searchStr);
          })
        ) {
          task.completed = true;
        }
      });
    },
    startTour() {
      this.currentStep = 1;
      this.isActive = true;
      this.disabled = false;
      this.addEvent(
        "tour_started",
        "Onboarding Guided Tour Started",
        "User initiated tour"
      );
    },
    nextStep(targetDescription) {
      const completedStep = this.currentStep;
      this.currentStep++;
      this.addEvent(
        "step_completed",
        targetDescription,
        `Completed step ${completedStep}`
      );
    },
    prevStep() {
      if (this.currentStep > 1) {
        this.currentStep--;
        this.addEvent(
          "step_backed",
          `Step ${this.currentStep + 1}`,
          `Returned to step ${this.currentStep}`
        );
      }
    },
    dismissTour() {
      this.isActive = false;
      this.addEvent(
        "tour_dismissed",
        "Onboarding Tour Dismissed",
        "User dismissed popover"
      );
    },
    resumeTour() {
      this.currentStep = clampStepToTotal(this.currentStep, this.totalSteps);
      this.isActive = true;
      this.disabled = false;
      this.addEvent(
        "tour_resumed",
        "Onboarding Tour Resumed",
        "User resumed popover"
      );
    },
    disableTour() {
      this.disabled = true;
      this.isActive = false;
      this.addEvent(
        "tour_disabled",
        "Onboarding Tour Disabled",
        "User completely disabled onboarding"
      );
    },
    resetTour() {
      if (typeof window !== "undefined" && window.localStorage) {
        try {
          const authStore = getAuthStoreSafe();
          const currentPrefix = scopePrefix(storageScopeOf(authStore));
          const keysToRemove = [];
          for (let i = 0; i < window.localStorage.length; i++) {
            const key = window.localStorage.key(i);
            if (!key) continue;
            if (
              key.startsWith("onboarding_") ||
              (currentPrefix && key.startsWith(`${currentPrefix}onboarding:`))
            ) {
              keysToRemove.push(key);
            }
          }
          keysToRemove.forEach((key) => window.localStorage.removeItem(key));
        } catch (e) {
          console.error("Failed to purge onboarding storage keys", e);
        }
      }

      this.currentStep = 1;
      this.isActive = true;
      this.disabled = false;
      this.events = [];
      this.addEvent(
        "tour_reset",
        "Onboarding Tour Reset",
        "Tour state was fully reset"
      );
      this.persist();
    },
    clearEvents() {
      this.events = [];
      this.persist();
    },
  },
});

registerLogoutCleanup(() => {
  const activePinia = getActivePinia();
  const onboardingStore = activePinia?._s?.get("onboarding");
  if (
    onboardingStore &&
    typeof onboardingStore.clearSessionState === "function"
  ) {
    onboardingStore.clearSessionState();
  }
});
