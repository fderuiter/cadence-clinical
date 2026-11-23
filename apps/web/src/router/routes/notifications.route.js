export default {
  order: 350,
  availability: "demo",
  contextLabel: "Notifications",
  catalog: {
    title: "Notifications",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage:
      "Kernel outbox events active; user notification inbox in pre-release",
    summary:
      "Actionable clinical task alerts and study milestone notifications.",
  },
  route: {
    path: "/notifications",
    name: "notifications",
    component: () => import("../../views/NotificationsView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
};
