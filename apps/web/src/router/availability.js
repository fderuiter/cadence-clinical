/**
 * Screen availability and commercial product-family mapping for workspace routes.
 *
 * Routes whose .NET Clinical Kernel endpoints are live appear in AVAILABLE_SCREENS.
 * Routes whose .NET module endpoints remain in pre-release resolve through
 * SCREEN_CATALOG so UnavailableView presents their owning product family,
 * commercial tier packaging, and pre-release delivery roadmap without exposing
 * internal issue trackers.
 */

import { isDemoBuild } from "../utils/demoMode";

/** Route names whose screens call endpoints the .NET services serve. */
export const AVAILABLE_SCREENS = new Set([
  // Clinical Canvas: participants, site activation, eTMF documents, greenlight, training, milestones, and monitoring.
  "study",
  // Execution: subjects, forms, observations, SDV, queries (#4828).
  "ecrf",
  // Kernel: one observation and its correction (ADR-2620).
  "observation",
  // CTMS & Execution: site activation, milestones, monitoring visits & letters, DoA, and startup greenlight (#4829).
  "ctms",
  "ssu",
  // eTMF: DIA TMF Reference Model binder, intelligence staging, clinical QC review, and binder exports (#4829).
  "etmf",
  // Knowledge: protocol training curricula, assignments, and Part 11 delegation sign-off (#4829).
  "training",
]);

/** Routes unlocked in interactive static demo builds backed by stateful MSW handlers. */
export const DEMO_AVAILABLE_SCREENS = new Set([
  ...AVAILABLE_SCREENS,
  "mdr",
  "digitization",
  "icf-builder",
  "econsent-authoring",
  "amendment-diff",
  "imaging",
  "devices",
  "device-investigation",
  "adjudication",
  "cec",
  "audit",
  "auditor",
  "rules",
  "coding",
  "data-lock",
  "lakehouse",
  "sensors",
  "labs",
  "dct",
  "safety",
  "exports",
  "etmf",
  "quality",
  "notifications",
  "tickets",
  "documents",
  "knowledge",
  "training",
  "participant-wallet",
  "lay-summaries",
]);

/** Routes that do not depend on study-scoped module APIs (public plans, onboarding, sign-in, error pages). */
const ALWAYS_AVAILABLE = new Set([
  "landing",
  "overview",
  "plans",
  "trial-onboarding",
  "login",
  "forbidden",
  "unavailable",
]);

/**
 * Metadata for workspace routes, mapping each unmigrated screen to its product family
 * in docs/strategy/PRODUCT_CATALOG.md and its pre-release delivery status.
 */
export const SCREEN_CATALOG = Object.freeze({
  overview: {
    title: "Overview & Orientation Hub",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage: "Active workspace shell",
    summary: "Role-tailored workspace orientation and quick-start navigation.",
  },
  mdr: {
    title: "Protocol & SoA Designer",
    family: "Blueprint",
    minimumTier: "Individual",
    isBaselineCapability: false,
    roadmapStage:
      "Pre-release module migration (read path live in Study Workspace)",
    summary:
      "Authoring of USDM protocol structures, eligibility criteria, and Schedule of Activities.",
  },
  digitization: {
    title: "Protocol AI Digitizer",
    family: "Blueprint",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Intelligence & Blueprint roadmap",
    summary:
      "Human-reviewed protocol extraction into governed USDM drafts with lineage attribution.",
  },
  "icf-builder": {
    title: "eConsent & ICF Builder",
    family: "Engage",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Engage module roadmap",
    summary:
      "Informed consent form authoring, IRB version tracking, and participant comprehension checks.",
  },
  "econsent-authoring": {
    title: "eConsent authoring",
    family: "Engage",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Engage module roadmap",
    summary:
      "Structured consent document templates and multi-language site localization.",
  },
  "amendment-diff": {
    title: "Amendments & Migration",
    family: "Blueprint",
    minimumTier: "Individual",
    isBaselineCapability: false,
    roadmapStage:
      "Pre-release Blueprint authoring surface (impact API live in Study Workspace)",
    summary:
      "Side-by-side protocol version comparison and downstream eCRF migration assessment.",
  },
  imaging: {
    title: "DICOM & RECIST Viewer",
    family: "Lens",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Lens module roadmap",
    summary:
      "De-identified medical imaging review, lesion tracking, and RECIST response assessment.",
  },
  devices: {
    title: "Device & IVD Suite",
    family: "Allocate",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Allocate module roadmap",
    summary:
      "Investigational medical-device exposure, firmware version lineage, and accountability.",
  },
  "device-investigation": {
    title: "Device & IVD Suite",
    family: "Allocate",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Allocate module roadmap",
    summary:
      "Device incident traceability linking firmware/hardware versions to clinical observations.",
  },
  adjudication: {
    title: "CEC adjudication",
    family: "Vigil",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Vigil module roadmap",
    summary:
      "Blinded clinical events committee dossier assembly and independent endpoint adjudication.",
  },
  cec: {
    title: "CEC adjudication",
    family: "Vigil",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Vigil module roadmap",
    summary:
      "Blinded clinical events committee dossier assembly and independent endpoint adjudication.",
  },
  audit: {
    title: "Part 11 Audit Ledger",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage:
      "Kernel audit chain active in Study Activity & Observation views; dedicated ledger UI in pre-release",
    summary:
      "Hash-chained 21 CFR Part 11 audit verification — baseline kernel infrastructure never gated by paid add-ons.",
  },
  auditor: {
    title: "Auditor portal",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage:
      "Kernel inspection reads active in Study Workspace; standalone auditor console in pre-release",
    summary:
      "Read-and-export regulatory inspection portal over tamper-evident audit chains and attestations.",
  },
  rules: {
    title: "Study Rules & Queries",
    family: "Capture",
    minimumTier: "Individual",
    isBaselineCapability: false,
    roadmapStage:
      "Inline queries active in Data Collection; standalone rule studio in pre-release",
    summary:
      "Edit-check specification, discrepancy query triage, and cross-visit validation rules.",
  },
  coding: {
    title: "Medical Coding",
    family: "Capture",
    minimumTier: "Individual",
    isBaselineCapability: false,
    roadmapStage:
      "Kernel terminology registry active; MedDRA / WHODrug coding workbench in pre-release",
    summary:
      "Governed concept mapping and medical dictionary coding with versioned terminology audit trails.",
  },
  "data-lock": {
    title: "Data Lock Console",
    family: "Capture",
    minimumTier: "Individual",
    isBaselineCapability: true,
    roadmapStage:
      "Kernel scoped locks enforced; batch lock console in pre-release",
    summary:
      "Study, site, participant, and observation lock orchestration with baseline electronic attestations.",
  },
  lakehouse: {
    title: "Lakehouse",
    family: "Lens",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Lens analytics roadmap",
    summary:
      "Traceable clinical dataset projections from source observations to analysis-ready views.",
  },
  sensors: {
    title: "Connected Sensors & IoT",
    family: "Lens",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Lens ingestion roadmap",
    summary:
      "Wearable telemetry ingestion and device-origin provenance into clinical observations.",
  },
  labs: {
    title: "Lab Governance",
    family: "Lens",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Lens module roadmap",
    summary:
      "Central and local laboratory reference ranges, normalizations, and toxicity grading.",
  },
  dct: {
    title: "Telehealth & Direct-to-Patient",
    family: "Engage",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Engage module roadmap",
    summary:
      "Decentralized visit scheduling, video consultations, and remote coordinator workflows.",
  },
  safety: {
    title: "Safety & DSUR Compiler",
    family: "Vigil",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Vigil module roadmap",
    summary:
      "Serious adverse event reporting, narrative generation, and DSUR aggregate safety tables.",
  },
  exports: {
    title: "Data Export Wizard",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage:
      "Regulated kernel read APIs active; CDISC archive packager in pre-release",
    summary:
      "Authorized clinical record and audit-trail export — preserved even after trial expiration.",
  },
  etmf: {
    title: "eTMF Document Vault",
    family: "Orbit",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage:
      "Site readiness indicators active; full DIA Reference Model vault in pre-release",
    summary:
      "Essential regulatory document lifecycle, completeness tracking, and inspection readiness.",
  },
  quality: {
    title: "Quality & RBQM Cockpit",
    family: "Assure",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Assure module roadmap",
    summary:
      "Key risk indicators, protocol deviation management, and CAPA effectiveness tracking.",
  },
  notifications: {
    title: "Notifications",
    family: "Cadence Core",
    minimumTier: "Baseline",
    isBaselineCapability: true,
    roadmapStage:
      "Kernel outbox events active; user notification inbox in pre-release",
    summary:
      "Actionable clinical task alerts and study milestone notifications.",
  },
  tickets: {
    title: "Clinical Issues & Tickets",
    family: "Orbit",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Orbit module roadmap",
    summary:
      "Operational site issue tracking, monitoring action items, and escalation workflows.",
  },
  documents: {
    title: "Documents Hub",
    family: "Orbit",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Orbit module roadmap",
    summary:
      "Controlled clinical document versions, investigator brochures, and site distribution.",
  },
  knowledge: {
    title: "Knowledge & SOP Hub",
    family: "Assure",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage:
      "Contextual help drawer active; enterprise SOP repository in pre-release",
    summary:
      "Standard operating procedures, study manuals, and version-controlled guidance.",
  },
  training: {
    title: "Study Training & Gating",
    family: "Assure",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage:
      "Kernel qualification model active; training LMS console in pre-release",
    summary:
      "Role qualification tracking and pre-access training verification before clinical actions.",
  },
  "participant-wallet": {
    title: "Participant Financial Wallet",
    family: "Orbit",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Orbit payments roadmap",
    summary:
      "Participant stipend disbursements and visit reimbursement tracking.",
  },
  "lay-summaries": {
    title: "EU CTR Lay Summaries",
    family: "Engage",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Engage module roadmap",
    summary:
      "Plain-language trial results authoring for participants and public registries.",
  },
});

export function screenMetadata(routeName) {
  if (!routeName) {
    return {
      title: "This screen",
      family: "Blueprint",
      minimumTier: "Individual",
      isBaselineCapability: false,
      roadmapStage: "Pre-release module roadmap",
      summary:
        "This workspace module is progressing through pre-release Clinical Kernel integration.",
    };
  }
  return (
    SCREEN_CATALOG[routeName] || {
      title: "This screen",
      family: "Blueprint",
      minimumTier: "Individual",
      isBaselineCapability: false,
      roadmapStage: "Pre-release module roadmap",
      summary:
        "This workspace module is progressing through pre-release Clinical Kernel integration.",
    }
  );
}

export function screenTitle(routeName) {
  return SCREEN_CATALOG[routeName]?.title || "This screen";
}

export function isScreenAvailable(routeName, env) {
  if (!routeName) return false;
  if (ALWAYS_AVAILABLE.has(routeName) || AVAILABLE_SCREENS.has(routeName)) {
    return true;
  }
  const demoActive =
    env !== undefined
      ? isDemoBuild(env)
      : import.meta.env?.MODE === "demo" ||
        import.meta.env?.VITE_DEMO_MODE === "true";
  if (demoActive) {
    return DEMO_AVAILABLE_SCREENS.has(routeName);
  }
  return false;
}
