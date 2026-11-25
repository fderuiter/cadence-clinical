export default {
  order: 230,
  availability: "demo",
  contextLabel: "Inspection review",
  sopEscalation: {
    prefixes: ["/auditor"],
    category: "REGULATORY_QUERY",
  },
  catalog: {
    title: "Auditor portal",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage:
      "Kernel inspection reads active in Study Workspace; standalone auditor console in pre-release",
    summary:
      "Read-and-export regulatory inspection portal over tamper-evident audit chains and attestations.",
  },
  route: {
    path: "/auditor",
    name: "auditor",
    component: () => import("../../views/AuditorView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["auditor", "tmf_auditor", "sponsor_admin"],
    },
  },
};
