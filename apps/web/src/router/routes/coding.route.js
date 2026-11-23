export default {
  order: 250,
  availability: "demo",
  contextLabel: "Medical coding",
  sopEscalation: {
    prefixes: ["/coding"],
    category: "DATA_QUERY",
  },
  catalog: {
    title: "Medical Coding",
    family: "Capture",
    minimumTier: "Individual",
    isBaselineCapability: false,
    roadmapStage:
      "Kernel terminology registry active; MedDRA / WHODrug coding workbench in pre-release",
    summary:
      "Governed concept mapping and medical dictionary coding with versioned terminology audit trails.",
  },
  route: {
    path: "/coding",
    name: "coding",
    component: () => import("../../views/MedicalCodingView.vue"),
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
