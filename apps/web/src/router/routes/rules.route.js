export default {
  order: 240,
  availability: "demo",
  contextLabel: "Validation rules",
  landingRoles: {
    priority: 40,
    all: ["data_manager"],
  },
  sopEscalation: {
    prefixes: ["/rules"],
    category: "DATA_QUERY",
  },
  catalog: {
    title: "Study Rules & Queries",
    family: "Capture",
    minimumTier: "Individual",
    isBaselineCapability: false,
    roadmapStage:
      "Rule packs, edit-check previews, validation findings, and inline queries active in Study Workspace; standalone rule studio in pre-release",
    summary:
      "Edit-check specification, discrepancy query triage, and cross-visit validation rules.",
  },
  route: {
    path: "/rules",
    name: "rules",
    component: () => import("../../views/RulesView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
};
