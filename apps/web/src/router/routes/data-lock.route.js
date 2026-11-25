import { redirectToCanvasSection } from "../helpers.js";

export default {
  order: 260,
  availability: "available",
  sopEscalation: {
    prefixes: ["/data-lock"],
    category: "DATA_QUERY",
  },
  catalog: {
    title: "Data Lock Console",
    family: "Capture",
    minimumTier: "Individual",
    isBaselineCapability: true,
    roadmapStage: "Active in Clinical Canvas (Data → Locks)",
    summary:
      "Study, site, participant, form, and observation lock orchestration with baseline electronic attestations.",
  },
  route: {
    path: "/data-lock",
    name: "data-lock",
    component: () => import("../../views/DataLockView.vue"),
    beforeEnter: redirectToCanvasSection("data", "locks"),
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
};
