import { createRouter, createWebHistory } from "vue-router";
import { useAuthStore, ROLE_ALIASES } from "../stores/auth";
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

const routes = [
  {
    path: "/",
    name: "landing",
    redirect: () => {
      const authStore = useAuthStore();
      if (!authStore.isAuthenticated) {
        return "/login";
      }
      const roles = authStore.normalizedRoles || [];
      const preferred = landingRouteFor(roles);
      if (isScreenAvailable(preferred)) {
        return { name: preferred };
      }
      // The preferred screen has no .NET backend yet: land on the first
      // screen that does and that the user may open.
      const fallback = routes.find(
        (route) =>
          AVAILABLE_SCREENS.has(route.name) &&
          (!route.meta?.requiresRole?.length ||
            hasRequiredRole(roles, route.meta.requiresRole))
      );
      return fallback ? { name: fallback.name } : { name: "unavailable" };
    },
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
      requiresAuth: false,
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
      requiresRole: ["cra", "monitor", "sponsor_admin"],
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
      requiresAuth: false,
    },
  },
];

export const router = createRouter({
  // Use a GitHub Pages-compatible history mode resolving from the configured Pages base path
  history: createWebHistory(import.meta.env.BASE_URL || "/cadence-clinical/"),
  routes,
});

// OIDC & RBAC Navigation guard (modern Vue Router v4 return-based guard)
router.beforeEach(async (to) => {
  const authStore = useAuthStore();

  const requiresAuth = to.matched.some((record) => record.meta.requiresAuth);

  if (requiresAuth) {
    // Verify active OIDC session integrity using Keycloak client APIs rather than trusting local/session storage
    if (window.keycloakInstance && !authStore.isDemoMode) {
      try {
        await window.keycloakInstance.updateToken(5);
        authStore.setAuth(window.keycloakInstance);
      } catch (err) {
        console.error(
          "OIDC Active session integrity verification failed via Keycloak SDK:",
          err
        );
        authStore.setAuth(null);
        return { path: "/login", query: { redirect: to.fullPath } };
      }

      if (!window.keycloakInstance.authenticated) {
        authStore.setAuth(null);
        return { path: "/login", query: { redirect: to.fullPath } };
      }
    }

    if (!authStore.isAuthenticated) {
      return { path: "/login", query: { redirect: to.fullPath } };
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
