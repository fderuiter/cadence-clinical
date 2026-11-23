export default {
  order: 130,
  availability: "available",
  contextLabel: "Observation",
  sopEscalation: {
    prefixes: ["/observations"],
    category: "DATA_QUERY",
  },
  tourEnabled: true,
  tourRoles: ["site_investigator", "crc", "data_manager"],
  route: {
    path: "/observations/:observationRef",
    name: "observation",
    component: () => import("../../views/ObservationView.vue"),
    props: true,
    meta: {
      requiresAuth: true,
      requiresRole: [
        "site_investigator",
        "crc",
        "data_manager",
        "cra",
        "monitor",
        "auditor",
        "tmf_auditor",
        "sponsor_admin",
      ],
    },
  },
};
