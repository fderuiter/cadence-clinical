export default {
  order: 280,
  availability: "demo",
  contextLabel: "Connected sensors",
  sopEscalation: {
    prefixes: ["/sensors"],
    category: "SITE_OPERATIONS",
  },
  catalog: {
    title: "Connected Sensors & IoT",
    family: "Lens",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Lens ingestion roadmap",
    summary:
      "Wearable telemetry ingestion and device-origin provenance into clinical observations.",
  },
  route: {
    path: "/sensors",
    name: "sensors",
    component: () => import("../../views/SensorTelemetryView.vue"),
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
