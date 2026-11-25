export default {
  order: 120,
  availability: "demo",
  contextLabel: "Protocol amendments",
  catalog: {
    title: "Amendments & Migration",
    family: "Blueprint",
    minimumTier: "Individual",
    isBaselineCapability: false,
    roadmapStage:
      "Pre-release Blueprint authoring surface (impact API live in Study Workspace)",
    summary:
      "Side-by-side protocol version comparison and downstream eCRF migration assessment.",
  },
  route: {
    path: "/amendment-diff",
    name: "amendment-diff",
    component: () => import("../../views/AmendmentDiffView.vue"),
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
};
