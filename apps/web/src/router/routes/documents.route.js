export default {
  order: 370,
  availability: "demo",
  contextLabel: "Controlled documents",
  catalog: {
    title: "Documents Hub",
    family: "Orbit",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Orbit module roadmap",
    summary:
      "Controlled clinical document versions, investigator brochures, and site distribution.",
  },
  route: {
    path: "/documents",
    name: "documents",
    component: () => import("../../views/DocumentsHubView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
};
