/**
 * Product-family catalog, baseline kernel guarantees, and scoping benchmark copy
 * rendered on commercial Plans & Scoping routes (split out of cold-boot chunks).
 */

export const PRODUCT_FAMILIES = /* @__PURE__ */ Object.freeze([
  {
    code: "Blueprint",
    name: "Blueprint",
    responsibility: "Protocol and study design",
    candidateApps: [
      "Protocol Designer",
      "Study Builder",
      "Standards Workbench",
      "Feasibility",
    ],
    deliveryStatus: "Preview active in Study Workspace",
    minimumTier: "Individual",
    summary:
      "Structured USDM protocol versions, Schedule of Activities matrices, lineage, and amendment impact analysis.",
  },
  {
    code: "Capture",
    name: "Capture",
    responsibility: "Clinical collection and discrepancy workflows",
    candidateApps: ["EDC", "Clinical Data Management"],
    deliveryStatus: "Preview active in Data Collection",
    minimumTier: "Individual",
    summary:
      "Visit eCRF capture, source data verification, governed value corrections, discrepancy queries, and scope locks.",
  },
  {
    code: "Orbit",
    name: "Orbit",
    responsibility: "Trial operations and site readiness",
    candidateApps: ["CTMS", "eTMF", "Site/eISF", "Payments"],
    deliveryStatus: "Site readiness active; vault workflows in pre-release",
    minimumTier: "Select",
    summary:
      "Site activation readiness, operational milestones, investigator site files, and trial master file governance.",
  },
  {
    code: "Engage",
    name: "Engage",
    responsibility: "Participant workflows",
    candidateApps: ["Consent", "Outcomes/eCOA", "Recruitment"],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Select",
    summary:
      "Electronic informed consent versioning, participant outcomes capture, telehealth visits, and lay summaries.",
  },
  {
    code: "Allocate",
    name: "Allocate",
    responsibility: "Intervention assignment and accountability",
    candidateApps: ["RTSM", "Clinical Supplies", "Device Accountability"],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Select",
    summary:
      "Randomization, investigational supply accountability, and medical-device / IVD exposure tracking.",
  },
  {
    code: "Vigil",
    name: "Vigil",
    responsibility: "Safety and pharmacovigilance",
    candidateApps: ["Safety", "Signal Management"],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Complete",
    summary:
      "Adverse event intake, clinical endpoint adjudication, DSUR compilation, and safety signal tracking.",
  },
  {
    code: "Lens",
    name: "Lens",
    responsibility: "Scientific evidence",
    candidateApps: [
      "Imaging",
      "Laboratory",
      "Statistical Analysis",
      "Real-World Evidence",
    ],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Complete",
    summary:
      "DICOM / RECIST imaging workflows, laboratory reference governance, connected sensors, and statistical lineage.",
  },
  {
    code: "Assure",
    name: "Assure",
    responsibility: "Quality and regulatory workflows",
    candidateApps: ["Quality Management", "Regulatory Management"],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Complete",
    summary:
      "Risk-based quality management (RBQM), CAPA oversight, SOP training gating, and submission readiness.",
  },
]);

export const BASELINE_KERNEL_GUARANTEES = /* @__PURE__ */ Object.freeze([
  {
    id: "audit",
    title: "Tamper-evident audit trails",
    detail:
      "Every command commits state, hash-chained audit entries, and outbox events atomically in Cadence Core — never sold as an add-on.",
  },
  {
    id: "signatures",
    title: "Electronic signatures (21 CFR Part 11)",
    detail:
      "Meaningful human attestations bound to fresh OIDC authentication remain available across all tiers and trial states.",
  },
  {
    id: "authorization",
    title: "Clinical Grants separate from commercial plans",
    detail:
      "Commercial Subscriptions and ProductEntitlements never grant patient data access or bypass study scope, blinding, or consent.",
  },
  {
    id: "continuity",
    title: "Post-expiry regulated read & export",
    detail:
      "When a trial window expires, new study mutations pause while authorized retrieval, audit review, and regulated exports remain active.",
  },
]);

export const MODULAR_SCOPING_BENCHMARK = /* @__PURE__ */ Object.freeze([
  {
    step: "1. Self-service account & workspace",
    industryPattern:
      "Modern EDC platforms such as Castor EDC and Veeva SiteVault let research teams register online and launch an evaluation workspace without waiting on manual operator provisioning.",
    cadenceApproach:
      "OIDC self-registration in Keycloak followed by automated Clinical Kernel tenant, sponsor organization, role, grant, and starter entitlement provisioning.",
  },
  {
    step: "2. Modular product selection",
    industryPattern:
      "Modular clinical suites (e.g., Castor modular pricing across core EDC plus eConsent, ePRO, and RTSM modules) scope deployments around protocol needs rather than forcing monolithic bundles.",
    cadenceApproach:
      "Individual, Select, and Complete tiers across eight product families with published APIs so standalone apps coexist with external systems.",
  },
  {
    step: "3. Study & residency scoping",
    industryPattern:
      "Commercial quotes factor study duration, site count, participant volume, and regional data residency before live clinical conduct.",
    cadenceApproach:
      "Per-tenant regional residency tags on kernel.tenants and transparent scoping conversations — zero opaque upcharges for baseline audit or security controls.",
  },
]);

export function findProductFamily(code) {
  return PRODUCT_FAMILIES.find((family) => family.code === code) || null;
}
