import { redirectToCanvasSection } from "../helpers.js";

export default {
  order: 320,
  availability: "available",
  catalog: {
    title: "Data Export Wizard",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage: "Active in Clinical Canvas (Submit → Exports)",
    summary:
      "Authorized clinical record and audit-trail export — preserved even after trial expiration.",
  },
  route: {
    path: "/exports",
    name: "exports",
    component: () => import("../../views/ExportWizardView.vue"),
    beforeEnter: redirectToCanvasSection("submit", "exports"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "data_manager",
        "sponsor_designer",
        "sponsor_admin",
        "super_admin",
        "auditor",
        "tmf_auditor",
      ],
    },
  },
};
