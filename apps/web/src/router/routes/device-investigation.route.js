export default {
  order: 170,
  availability: "demo",
  contextLabel: "Device investigation",
  catalog: {
    title: "Device & IVD Suite",
    family: "Allocate",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Allocate module roadmap",
    summary:
      "Device incident traceability linking firmware/hardware versions to clinical observations.",
  },
  route: {
    path: "/device-investigation",
    name: "device-investigation",
    component: () => import("../../views/DeviceInvestigationView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_admin",
        "cra",
        "monitor",
        "sponsor_designer",
        "auditor",
      ],
    },
  },
};
