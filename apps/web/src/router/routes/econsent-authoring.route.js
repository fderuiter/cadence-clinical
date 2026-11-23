export default {
  order: 110,
  availability: "demo",
  contextLabel: "Consent authoring",
  catalog: {
    title: "eConsent authoring",
    family: "Engage",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Engage module roadmap",
    summary:
      "Structured consent document templates and multi-language site localization.",
  },
  route: {
    path: "/econsent-authoring",
    name: "econsent-authoring",
    component: () => import("../../views/ConsentAuthoringView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
    },
  },
};
