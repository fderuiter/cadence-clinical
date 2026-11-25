export default {
  order: 210,
  availability: "available",
  primaryFallback: "all",
  contextLabel: "Site startup",
  tourEnabled: true,
  onboardingTaskPath: "/ssu",
  siteReadinessTier: "site",
  siteReadinessRoles: [
    "site_investigator",
    "crc",
    "trial_coordinator",
    "pharmacist",
  ],
  sopEscalation: {
    prefixes: ["/ssu"],
    category: "SITE_OPERATIONS",
  },
  route: {
    path: "/ssu",
    name: "ssu",
    component: () => import("../../views/CtmsView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "cra",
        "monitor",
        "data_manager",
        "sponsor_admin",
        "site_investigator",
        "crc",
        "trial_coordinator",
        "unblinded_statistician",
        "pharmacist",
      ],
    },
  },
};
