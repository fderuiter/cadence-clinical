export default {
  order: 270,
  availability: "demo",
  contextLabel: "Clinical analytics",
  catalog: {
    title: "Lakehouse",
    family: "Lens",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Lens analytics roadmap",
    summary:
      "Traceable clinical dataset projections from source observations to analysis-ready views.",
  },
  route: {
    path: "/lakehouse",
    name: "lakehouse",
    component: () => import("../../views/LakehouseView.vue"),
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
};
