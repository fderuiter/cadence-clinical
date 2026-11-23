export default {
  order: 80,
  availability: "demo",
  contextLabel: "Protocol design",
  landingRoles: {
    priority: 10,
    all: ["sponsor_designer"],
    defaultFallback: true,
  },
  catalog: {
    title: "Protocol & SoA Designer",
    family: "Blueprint",
    minimumTier: "Individual",
    isBaselineCapability: false,
    roadmapStage:
      "Pre-release module migration (read path live in Study Workspace)",
    summary:
      "Authoring of USDM protocol structures, eligibility criteria, and Schedule of Activities.",
  },
  route: {
    path: "/mdr",
    name: "mdr",
    component: () => import("../../views/MdrView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
};
