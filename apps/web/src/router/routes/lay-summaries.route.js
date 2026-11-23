export default {
  order: 410,
  availability: "demo",
  contextLabel: "Plain-language summaries",
  sopEscalation: {
    prefixes: ["/lay-summaries"],
    category: "REGULATORY_QUERY",
  },
  catalog: {
    title: "EU CTR Lay Summaries",
    family: "Engage",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Engage module roadmap",
    summary:
      "Plain-language trial results authoring for participants and public registries.",
  },
  route: {
    path: "/lay-summaries",
    name: "lay-summaries",
    component: () => import("../../views/LaySummaryPortal.vue"),
    meta: {
      requiresAuth: true,
    },
  },
};
