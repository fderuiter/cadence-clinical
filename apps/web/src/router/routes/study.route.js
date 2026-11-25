export default {
  order: 70,
  availability: "available",
  primaryFallback: "live",
  landingRoles: {
    priority: 20,
    live: ["site_investigator", "crc", "trial_coordinator"],
  },
  tourEnabled: true,
  onboardingTaskPath: "/studies",
  sopEscalation: {
    prefixes: ["/studies"],
    category: "DATA_QUERY",
  },
  palette: {
    key: "workspace:studies",
    order: 10,
    label: "Studies",
    detail: "Open a study and navigate its lifecycle",
    scopeMode: "studies-workspace",
  },
  route: {
    path: "/studies/:studyCode?",
    name: "study",
    component: () => import("../../views/StudyCanvasView.vue"),
    meta: { requiresAuth: true, layout: "canvas" },
  },
};
