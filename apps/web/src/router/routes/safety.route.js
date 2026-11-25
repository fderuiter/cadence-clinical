export default {
  order: 310,
  availability: "demo",
  contextLabel: "Safety review",
  landingRoles: {
    priority: 45,
    all: ["safety_officer", "safety_physician", "qppv"],
  },
  sopEscalation: {
    prefixes: ["/safety"],
    category: "SAFETY_ADVERSE_EVENT",
  },
  catalog: {
    title: "Safety & DSUR Compiler",
    family: "Vigil",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Vigil module roadmap",
    summary:
      "Serious adverse event reporting, narrative generation, and DSUR aggregate safety tables.",
  },
  route: {
    path: "/safety",
    name: "safety",
    component: () => import("../../views/AggregateSafetyView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "safety_physician",
        "qppv",
        "qppv_officer",
        "qualified_person_pv",
        "safety_officer",
        "sponsor_pv_officer",
        "sponsor_medical_monitor",
        "medical_monitor",
        "sponsor_admin",
        "data_manager",
        "super_admin",
      ],
    },
  },
};
