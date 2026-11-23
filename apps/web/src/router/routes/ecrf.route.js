import { isDemoBuild } from "../../utils/demoMode.js";

export default {
  order: 140,
  availability: "available",
  primaryFallback: "demo",
  contextLabel: "Data collection",
  landingRoles: {
    priority: 20,
    demo: ["site_investigator", "crc"],
  },
  tourEnabled: true,
  onboardingTaskPath: "/ecrf",
  dataCollectionHub: true,
  sopEscalation: {
    prefixes: ["/ecrf"],
    category: "PROTOCOL_DEVIATION",
  },
  palette: {
    key: "workspace:data-collection",
    order: 20,
    label: "Data collection",
    detail: "Clinical casebooks, forms, and discrepancy review",
    scopeMode: "data-collection",
  },
  route: {
    path: "/ecrf",
    name: "ecrf",
    component: () => import("../../views/EcrfView.vue"),
    beforeEnter: async (to) => {
      if (!isDemoBuild()) {
        const { buildCanvasLocationFromEcrfQuery } =
          await import("../../canvas/ecrfRedirect");
        return buildCanvasLocationFromEcrfQuery(to.query);
      }
      return true;
    },
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
};
