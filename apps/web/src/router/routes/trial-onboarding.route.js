export default {
  order: 30,
  availability: "always",
  nonWorkflow: true,
  hideTourOverlay: true,
  route: {
    path: "/onboarding/trial",
    name: "trial-onboarding",
    component: () => import("../../views/TrialSetupView.vue"),
    meta: { requiresAuth: true },
  },
};
