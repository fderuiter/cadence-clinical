export default {
  order: 90,
  availability: "demo",
  contextLabel: "Protocol digitization",
  catalog: {
    title: "Protocol AI Digitizer",
    family: "Blueprint",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Intelligence & Blueprint roadmap",
    summary:
      "Human-reviewed protocol extraction into governed USDM drafts with lineage attribution.",
  },
  route: {
    path: "/digitization",
    name: "digitization",
    component: () => import("../../views/ProtocolDigitizationView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
};
