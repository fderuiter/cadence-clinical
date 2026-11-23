export default {
  order: 290,
  availability: "demo",
  contextLabel: "Laboratory governance",
  catalog: {
    title: "Lab Governance",
    family: "Lens",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Lens module roadmap",
    summary:
      "Central and local laboratory reference ranges, normalizations, and toxicity grading.",
  },
  route: {
    path: "/labs",
    name: "labs",
    component: () => import("../../views/LabGovernanceView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "cra",
        "monitor",
        "sponsor_designer",
        "sponsor_admin",
        "super_admin",
      ],
    },
  },
};
