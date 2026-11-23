export default {
  order: 340,
  availability: "demo",
  contextLabel: "Quality management",
  catalog: {
    title: "Quality & RBQM Cockpit",
    family: "Assure",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Assure module roadmap",
    summary:
      "Key risk indicators, protocol deviation management, and CAPA effectiveness tracking.",
  },
  route: {
    path: "/quality",
    name: "quality",
    component: () => import("../../views/QualityCockpitView.vue"),
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
        "auditor",
        "tmf_auditor",
      ],
    },
  },
};
