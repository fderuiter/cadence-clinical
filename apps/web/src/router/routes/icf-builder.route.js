export default {
  order: 100,
  availability: "demo",
  contextLabel: "Informed consent",
  sopEscalation: {
    prefixes: ["/icf-builder"],
    category: "REGULATORY_QUERY",
  },
  catalog: {
    title: "eConsent & ICF Builder",
    family: "Engage",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Engage module roadmap",
    summary:
      "Informed consent form authoring, IRB version tracking, and participant comprehension checks.",
  },
  route: {
    path: "/icf-builder",
    name: "icf-builder",
    component: () => import("../../views/ICFBuilderView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
};
