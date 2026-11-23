export default {
  order: 390,
  availability: "available",
  contextLabel: "Protocol training",
  sopEscalation: {
    prefixes: ["/training"],
    category: "REGULATORY_QUERY",
  },
  studyScopedLaunch: true,
  palette: {
    key: "workspace:training",
    order: 50,
    label: "Study training",
    detail: "Protocol curricula, staff assignments, and delegation sign-off",
    scopeMode: "study",
  },
  catalog: {
    title: "Study Training & Gating",
    family: "Assure",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage:
      "Kernel qualification model active; training LMS console in pre-release",
    summary:
      "Role qualification tracking and pre-access training verification before clinical actions.",
  },
  route: {
    path: "/training",
    name: "training",
    component: () => import("../../views/StudyTrainingView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
};
