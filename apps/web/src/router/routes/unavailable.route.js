export default {
  order: 60,
  availability: "always",
  nonWorkflow: true,
  nonOnboarding: true,
  hideTourOverlay: true,
  route: {
    path: "/unavailable",
    name: "unavailable",
    component: () => import("../../views/UnavailableView.vue"),
    meta: { requiresAuth: false },
  },
};
