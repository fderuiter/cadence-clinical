import {
  createRouter,
  createWebHashHistory,
  createWebHistory,
} from "vue-router";
import { TOKEN_MIN_VALIDITY_SECONDS } from "../api/authorization.js";
import { useAuthStore, ROLE_ALIASES } from "../stores/auth";
import { useOnboardingStore } from "../stores/onboarding.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { AVAILABLE_SCREENS, isScreenAvailable } from "./availability";

/**
 * Seeding Gap Documentation Resolved:
 * The "Sponsor Designer" role has been successfully seeded in the Keycloak realm configuration
 * (`cadence-realm.json`) and is centrally mapped in the OIDC normalizer.
 */

// Helper to check if user has required roles, mapping UI roles to Keycloak roles
export function hasRequiredRole(userRoles, requiredRoles) {
  return userRoles.some((uRole) => {
    if (requiredRoles.includes(uRole)) return true;
    const aliases = ROLE_ALIASES[uRole] || [];
    return aliases.some((alias) => requiredRoles.includes(alias));
  });
}

// The screen each role lands on when opening "/".
function landingRouteFor(roles) {
  if (roles.includes("sponsor_designer")) return "mdr";
  if (roles.includes("site_investigator") || roles.includes("crc")) {
    return "ecrf";
  }
  if (roles.includes("cra") || roles.includes("monitor")) return "ctms";
  if (roles.includes("data_manager")) return "rules";
  if (roles.includes("auditor") || roles.includes("tmf_auditor")) {
    return "audit";
  }
  return "mdr";
}

const PRIMARY_FALLBACK_SCREENS = new Set(["ecrf", "ctms", "ssu"]);

const routes = [
  {
    path: "/",
    name: "landing",
    redirect: () => {
      const authStore = useAuthStore();
      if (!authStore.isAuthenticated) {
        return "/login";
      }
      if (authStore.needsTrialOnboarding) {
        return { name: "trial-onboarding" };
      }
      const onboardingStore = useOnboardingStore();
      if (!onboardingStore.bypassOverview) {
        return { name: "overview" };
      }
      const roles = authStore.normalizedRoles || [];
      const preferred = landingRouteFor(roles);
      if (isScreenAvailable(preferred)) {
        return { name: preferred };
      }
      // The preferred screen has no .NET backend yet: land on the first
      // role-authorized primary workflow screen that does, or open the study
      // workspace so every authenticated persona lands on a live screen.
      const fallback = routes.find(
        (route) =>
          PRIMARY_FALLBACK_SCREENS.has(route.name) &&
          AVAILABLE_SCREENS.has(route.name) &&
          (!route.meta?.requiresRole?.length ||
            hasRequiredRole(roles, route.meta.requiresRole))
      );
      if (fallback) {
        return { name: fallback.name };
      }
      return isScreenAvailable("study")
        ? { name: "study" }
        : { name: "unavailable" };
    },
  },
  {
    path: "/overview",
    name: "overview",
    component: () => import("../views/OverviewView.vue"),
    meta: { requiresAuth: true },
  },
  {
    path: "/plans",
    name: "plans",
    component: () => import("../views/PlansView.vue"),
    meta: { requiresAuth: false },
  },
  {
    path: "/onboarding/trial",
    name: "trial-onboarding",
    component: () => import("../views/TrialSetupView.vue"),
    meta: { requiresAuth: true },
  },
  {
    path: "/login",
    name: "login",
    component: () => import("../views/LoginView.vue"),
    meta: { requiresAuth: false },
  },
  {
    path: "/forbidden",
    name: "forbidden",
    component: () => import("../views/ForbiddenView.vue"),
    meta: { requiresAuth: false },
  },
  {
    path: "/unavailable",
    name: "unavailable",
    component: () => import("../views/UnavailableView.vue"),
    meta: { requiresAuth: false },
  },
  {
    // The Clinical Canvas (docs/ux/CLINICAL_CANVAS.md): one study, lifecycle navigation, three panes.
    path: "/studies/:studyCode?",
    name: "study",
    component: () => import("../views/StudyCanvasView.vue"),
    meta: { requiresAuth: true, layout: "canvas" },
  },
  {
    path: "/mdr",
    name: "mdr",
    component: () => import("../views/MdrView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
  {
    path: "/digitization",
    name: "digitization",
    component: () => import("../views/ProtocolDigitizationView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
  {
    path: "/icf-builder",
    name: "icf-builder",
    component: () => import("../views/ICFBuilderView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
  {
    path: "/econsent-authoring",
    name: "econsent-authoring",
    component: () => import("../views/ConsentAuthoringView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
  {
    path: "/amendment-diff",
    name: "amendment-diff",
    component: () => import("../views/AmendmentDiffView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "sponsor_designer",
        "data_manager",
        "sponsor_admin",
        "super_admin",
        "cra",
        "monitor",
        "site_investigator",
        "crc",
      ],
    },
  },
  {
    // One kernel observation, its history, and (for authorised roles) its correction (ADR-2620). Opened from a link to
    // the observation, so it never serves as a landing screen.
    path: "/observations/:observationRef",
    name: "observation",
    component: () => import("../views/ObservationView.vue"),
    props: true,
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "cra",
        "monitor",
        "auditor",
        "tmf_auditor",
        "sponsor_admin",
      ],
    },
  },
  {
    path: "/ecrf",
    name: "ecrf",
    component: () => import("../views/EcrfView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
        "cra",
        "monitor",
      ],
    },
  },
  {
    path: "/imaging",
    name: "imaging",
    component: () => import("../views/ImagingView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
        "cra",
        "monitor",
        "sponsor_designer",
      ],
    },
  },
  {
    path: "/devices",
    name: "devices",
    component: () => import("../views/DeviceInvestigationView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
        "cra",
        "monitor",
        "sponsor_designer",
        "auditor",
      ],
    },
  },
  {
    path: "/device-investigation",
    name: "device-investigation",
    component: () => import("../views/DeviceInvestigationView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
        "cra",
        "monitor",
        "sponsor_designer",
        "auditor",
      ],
    },
  },
  {
    path: "/adjudication",
    name: "adjudication",
    component: () => import("../views/CecAdjudicationView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
        "cra",
        "monitor",
        "sponsor_designer",
        "auditor",
        "tmf_auditor",
      ],
    },
  },
  {
    path: "/cec",
    name: "cec",
    component: () => import("../views/CecAdjudicationView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
        "cra",
        "monitor",
        "sponsor_designer",
        "auditor",
        "tmf_auditor",
      ],
    },
  },
  {
    path: "/ctms",
    name: "ctms",
    component: () => import("../views/CtmsView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["cra", "monitor", "data_manager", "sponsor_admin"],
    },
  },
  {
    path: "/ssu",
    name: "ssu",
    component: () => import("../views/CtmsView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "cra",
        "monitor",
        "data_manager",
        "sponsor_admin",
        "site_investigator",
        "crc",
      ],
    },
  },
  {
    path: "/audit",
    name: "audit",
    component: () => import("../views/AuditView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["auditor", "tmf_auditor", "sponsor_admin"],
    },
  },
  {
    path: "/auditor",
    name: "auditor",
    component: () => import("../views/AuditorView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["auditor", "tmf_auditor", "sponsor_admin"],
    },
  },
  {
    path: "/rules",
    name: "rules",
    component: () => import("../views/RulesView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
  {
    path: "/coding",
    name: "coding",
    component: () => import("../views/MedicalCodingView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "data_manager",
        "sponsor_designer",
        "sponsor_admin",
        "super_admin",
      ],
    },
  },
  {
    path: "/data-lock",
    name: "data-lock",
    component: () => import("../views/DataLockView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "data_manager",
        "sponsor_designer",
        "sponsor_admin",
        "super_admin",
      ],
    },
  },
  {
    path: "/lakehouse",
    name: "lakehouse",
    component: () => import("../views/LakehouseView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "data_manager",
        "sponsor_designer",
        "sponsor_admin",
        "sponsor_statistician",
        "unblinded_statistician",
        "super_admin",
      ],
    },
  },
  {
    path: "/sensors",
    name: "sensors",
    component: () => import("../views/SensorTelemetryView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_designer",
        "sponsor_admin",
        "cra",
        "monitor",
        "super_admin",
      ],
    },
  },
  {
    path: "/labs",
    name: "labs",
    component: () => import("../views/LabGovernanceView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "cra",
        "monitor",
        "sponsor_designer",
        "sponsor_admin",
        "super_admin",
      ],
    },
  },
  {
    path: "/dct",
    name: "dct",
    component: () => import("../views/TelehealthDtpView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_designer",
        "sponsor_admin",
        "cra",
        "monitor",
        "super_admin",
      ],
    },
  },
  {
    path: "/safety",
    name: "safety",
    component: () => import("../views/AggregateSafetyView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
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
      ],
    },
  },
  {
    path: "/exports",
    name: "exports",
    component: () => import("../views/ExportWizardView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "data_manager",
        "sponsor_designer",
        "sponsor_admin",
        "super_admin",
      ],
    },
  },
  {
    path: "/etmf",
    name: "etmf",
    component: () => import("../views/DocumentManagerView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "cra",
        "monitor",
        "data_manager",
        "site_investigator",
        "crc",
        "auditor",
        "tmf_auditor",
        "sponsor_admin",
      ],
    },
  },
  {
    path: "/quality",
    name: "quality",
    component: () => import("../views/QualityCockpitView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "sponsor_designer",
        "data_manager",
        "sponsor_admin",
        "super_admin",
        "cra",
        "monitor",
        "site_investigator",
        "crc",
        "auditor",
        "tmf_auditor",
      ],
    },
  },
  {
    path: "/notifications",
    name: "notifications",
    component: () => import("../views/NotificationsView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
  {
    path: "/tickets",
    name: "tickets",
    component: () => import("../views/TicketsView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
  {
    path: "/documents",
    name: "documents",
    component: () => import("../views/DocumentsHubView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
  {
    path: "/knowledge",
    name: "knowledge",
    component: () => import("../views/KnowledgeView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
  {
    path: "/training",
    name: "training",
    component: () => import("../views/StudyTrainingView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
  {
    path: "/participant-wallet",
    name: "participant-wallet",
    component: () => import("../views/ParticipantWalletView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
  {
    path: "/lay-summaries",
    name: "lay-summaries",
    component: () => import("../views/LaySummaryPortal.vue"),
    meta: {
      requiresAuth: true,
    },
  },
];

export function createAppHistory(
  env = import.meta.env,
  base = env?.BASE_URL || "/cadence-clinical/"
) {
  return isDemoBuild(env) ? createWebHashHistory(base) : createWebHistory(base);
}

export const router = createRouter({
  // Use hash history for static subpath demo builds and web history for Keycloak OIDC deployments
  history: createAppHistory(),
  routes,
});

// OIDC & RBAC navigation guard. Sessions come only from Keycloak: an
// unauthenticated user, or one whose token can no longer be refreshed, is sent
// to the sign-in page.
router.beforeEach(async (to) => {
  const authStore = useAuthStore();

  const requiresAuth = to.matched.some((record) => record.meta.requiresAuth);

  if (requiresAuth) {
    if (authStore.isAuthenticated) {
      try {
        await authStore.refresh(TOKEN_MIN_VALIDITY_SECONDS);
      } catch (err) {
        console.error("Keycloak session could not be refreshed:", err);
      }
    }

    if (!authStore.isAuthenticated) {
      return { path: "/login", query: { redirect: to.fullPath } };
    }

    if (authStore.needsTrialOnboarding && to.name !== "trial-onboarding") {
      return { name: "trial-onboarding" };
    }

    // Role-based authorization guard
    const requiredRoles = to.meta.requiresRole;
    if (requiredRoles && requiredRoles.length > 0) {
      const hasRole = hasRequiredRole(authStore.normalizedRoles, requiredRoles);
      if (!hasRole) {
        console.warn(
          `User does not have required roles: ${requiredRoles.join(", ")}`
        );
        return { path: "/forbidden" };
      }
    }
  }

  if (to.name && !isScreenAvailable(to.name)) {
    return { name: "unavailable", query: { screen: String(to.name) } };
  }
});
