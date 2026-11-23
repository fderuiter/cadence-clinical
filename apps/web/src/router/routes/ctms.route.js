export default {
  order: 200,
  availability: "available",
  primaryFallback: "all",
  contextLabel: "Site readiness",
  landingRoles: {
    priority: 30,
    all: ["cra", "monitor"],
  },
  tourEnabled: true,
  onboardingTaskPath: "/ctms",
  siteReadinessTier: "sponsor",
  sopEscalation: {
    prefixes: ["/ctms"],
    category: "MONITORING_FINDING",
  },
  palette: {
    key: "workspace:site-readiness",
    order: 30,
    label: "Site readiness",
    detail: "Site activation, milestones, and readiness workflows",
    scopeMode: "site-readiness",
  },
  route: {
    path: "/ctms",
    name: "ctms",
    component: () => import("../../views/CtmsView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["cra", "monitor", "data_manager", "sponsor_admin"],
    },
  },
};
