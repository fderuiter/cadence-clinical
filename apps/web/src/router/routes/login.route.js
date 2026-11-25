export default {
  order: 40,
  availability: "always",
  nonWorkflow: true,
  nonOnboarding: true,
  hideTourOverlay: true,
  route: {
    path: "/login",
    name: "login",
    component: () => import("../../views/LoginView.vue"),
    meta: { requiresAuth: false },
  },
};
