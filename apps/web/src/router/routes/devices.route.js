export default {
  order: 160,
  availability: "demo",
  contextLabel: "Medical devices",
  catalog: {
    title: "Device & IVD Suite",
    family: "Allocate",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Allocate module roadmap",
    summary:
      "Investigational medical-device exposure, firmware version lineage, and accountability.",
  },
  route: {
    path: "/devices",
    name: "devices",
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
