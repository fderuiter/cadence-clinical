export default {
  order: 330,
  availability: "available",
  contextLabel: "Regulatory documents",
  sopEscalation: {
    prefixes: ["/etmf"],
    category: "REGULATORY_QUERY",
  },
  studyScopedLaunch: true,
  palette: {
    key: "workspace:trial-documents",
    order: 40,
    label: "Trial documents",
    detail: "Study binder, document classification, and clinical review",
    scopeMode: "study",
  },
  catalog: {
    title: "eTMF Document Vault",
    family: "Orbit",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage:
      "Site readiness indicators active; full DIA Reference Model vault in pre-release",
    summary:
      "Essential regulatory document lifecycle, completeness tracking, and inspection readiness.",
  },
  route: {
    path: "/etmf",
    name: "etmf",
    component: () => import("../../views/DocumentManagerView.vue"),
    meta: {
      requiresAuth: true,
      requiresRole: [
        "cra",
        "monitor",
        "data_manager",
        "site_investigator",
        "crc",
        "auditor",
        "tmf_auditor",
        "sponsor_admin",
      ],
    },
  },
};
