export default {
  order: 220,
  availability: "demo",
  contextLabel: "Audit ledger",
  landingRoles: {
    priority: 50,
    all: ["auditor", "tmf_auditor"],
  },
  sopEscalation: {
    prefixes: ["/audit"],
    category: "REGULATORY_QUERY",
  },
  catalog: {
    title: "Part 11 Audit Ledger",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage:
      "Kernel audit chain active in Study Activity & Observation views; dedicated ledger UI in pre-release",
    summary:
      "Hash-chained 21 CFR Part 11 audit verification — baseline kernel infrastructure never gated by paid add-ons.",
  },
  route: {
    path: "/audit",
    name: "audit",
    component: () => import("../../views/AuditView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["auditor", "tmf_auditor", "sponsor_admin"],
    },
  },
};
