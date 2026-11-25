export default {
  order: 20,
  availability: "always",
  nonWorkflow: true,
  hideTourOverlay: true,
  route: {
    path: "/plans",
    name: "plans",
    component: () => import("../../views/PlansView.vue"),
    meta: { requiresAuth: false },
  },
};
