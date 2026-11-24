export default {
  order: 10,
  availability: "always",
  nonWorkflow: true,
  tourEnabled: true,
  onboardingTaskPath: "/overview",
  palette: {
    key: "workspace:orientation",
    order: 80,
    label: "Orientation",
    detail: "Role-tailored workspace orientation and setup",
    scopeMode: "workflow",
  },
  catalog: {
    title: "Overview & Orientation Hub",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage: "Active workspace shell",
    summary: "Role-tailored workspace orientation and quick-start navigation.",
  },
  route: {
    path: "/overview",
    name: "overview",
    component: () => import("../../views/OverviewView.vue"),
    meta: { requiresAuth: true },
  },
};
