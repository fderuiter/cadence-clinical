export default {
  order: 300,
  availability: "demo",
  contextLabel: "Decentralized visits",
  catalog: {
    title: "Telehealth & Direct-to-Patient",
    family: "Engage",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Engage module roadmap",
    summary:
      "Decentralized visit scheduling, video consultations, and remote coordinator workflows.",
  },
  route: {
    path: "/dct",
    name: "dct",
    component: () => import("../../views/TelehealthDtpView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "sponsor_designer",
        "sponsor_admin",
        "cra",
        "monitor",
        "super_admin",
      ],
    },
  },
};
