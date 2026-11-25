export default {
  order: 60,
  availability: "always",
  nonWorkflow: true,
  nonOnboarding: true,
  hideTourOverlay: "when-inactive",
  sopEscalation: {
    prefixes: ["/unavailable"],
    category: "TECHNICAL_SYSTEM",
  },
  route: {
    path: "/unavailable",
    name: "unavailable",
    component: () => import("../../views/UnavailableView.vue"),
    meta: { requiresAuth: false },
  },
};
