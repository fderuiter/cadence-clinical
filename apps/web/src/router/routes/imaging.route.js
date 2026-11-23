export default {
  order: 150,
  availability: "demo",
  contextLabel: "Medical imaging",
  catalog: {
    title: "DICOM & RECIST Viewer",
    family: "Lens",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Lens module roadmap",
    summary:
      "De-identified medical imaging review, lesion tracking, and RECIST response assessment.",
  },
  route: {
    path: "/imaging",
    name: "imaging",
    component: () => import("../../views/ImagingView.vue"),
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
      ],
    },
  },
};
