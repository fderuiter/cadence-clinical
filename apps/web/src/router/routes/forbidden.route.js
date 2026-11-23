export default {
  order: 50,
  availability: "always",
  nonWorkflow: true,
  nonOnboarding: true,
  hideTourOverlay: true,
  sopEscalation: {
    prefixes: ["/forbidden"],
    category: "ACCESS_CONTROL",
  },
  route: {
    path: "/forbidden",
    name: "forbidden",
    component: () => import("../../views/ForbiddenView.vue"),
    meta: { requiresAuth: false },
  },
};
