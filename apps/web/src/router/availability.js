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
  // Clinical Canvas: participants, protocol versions, site activation, startup readiness, documents,
  // training, milestones, delegation logs, monitoring visits, investigational supply, and queries.
  "study",
  // Execution: subjects, forms, observations, SDV, queries (#4828).
  "ecrf",
  // Kernel: one observation and its correction (ADR-2620).
  "observation",
  // CTMS & Execution: site activation, startup greenlight, milestones, monitoring visits & compiled letters, DoA logs, and RTSM supply;
  // unbacked financials, CRA allocation, recruitment edits, and adaptive dose escalation stay gated to demo builds (#4829).
  "ctms",
  "ssu",
  // eTMF: DIA TMF Reference Model binder, intelligence staging, clinical QC review, and binder exports (#4829).
  "etmf",
  // Knowledge: protocol training curricula, assignments, and Part 11 delegation sign-off (#4829).
  "training",
  // Orbit: operational issue queue, SLA timers, dual-visibility notes, Part 11 sign-off, and audit trail.
  "tickets",
  // Assure: published SOP repository, contextual guidance, immutable version snapshots, and Part 11 audit ledger.
  "knowledge",
  // Consolidated into Clinical Canvas: scoped locks (stage=data&section=locks) and regulated exports (stage=submit&section=exports).
  "data-lock",
  "exports",
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
      "Rule packs, edit-check previews, validation findings, and inline queries active in Study Workspace; standalone rule studio in pre-release",
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
    roadmapStage: "Active in Clinical Canvas (Data → Locks)",
    summary:
      "Study, site, participant, form, and observation lock orchestration with baseline electronic attestations.",
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
    roadmapStage: "Active in Clinical Canvas (Submit → Exports)",
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
    roadmapStage:
      "Operational issue queue, SLA timers, dual-visibility notes, Part 11 sign-off, and audit trail active; binary evidence storage in pre-release",
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
      "Contextual help, published SOP repository, immutable version snapshots, and Part 11 audit ledger active; multi-state Markdown authoring in pre-release",
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

/**
 * @typedef {object} DemoCatalogModule
 * @property {string} routeName
 * @property {string} defaultPath
 * @property {string} title
 * @property {string} family
 * @property {string} summary
 * @property {string[]} requiresRole
 */

/** @type {readonly string[]} */
export const DEMO_PRODUCT_FAMILY_ORDER =
  import.meta.env?.MODE === "production" && !isDemoBuild()
    ? /* @__PURE__ */ Object.freeze([])
    : /* @__PURE__ */ Object.freeze([
        "Blueprint",
        "Capture",
        "Orbit",
        "Engage",
        "Allocate",
        "Lens",
        "Vigil",
        "Assure",
        "Cadence Core",
      ]);

/**
 * UX-1 compliant clinical catalog metadata and route RBAC requirements for all 35
 * screens in DEMO_AVAILABLE_SCREENS. Used by demo-mode Overview launchers and ⌘K
 * preview destinations without exposing product-silo acronyms.
 *
 * @type {Readonly<Record<string, DemoCatalogModule>>}
 */
export const DEMO_MODULE_CATALOG =
  import.meta.env?.MODE === "production" && !isDemoBuild()
    ? /* @__PURE__ */ Object.freeze({})
    : /* @__PURE__ */ Object.freeze({
        study: {
          routeName: "study",
          defaultPath: "/studies",
          title: "Studies",
          family: "Blueprint",
          summary:
            "Open a study workspace to navigate lifecycle stages, participants, protocol versions, and activity.",
          requiresRole: [],
        },
        mdr: {
          routeName: "mdr",
          defaultPath: "/mdr",
          title: "Protocol & schedule designer",
          family: "Blueprint",
          summary:
            "Author structured protocol designs, eligibility criteria, and Schedule of Activities matrices.",
          requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
        },
        digitization: {
          routeName: "digitization",
          defaultPath: "/digitization",
          title: "Protocol digitization",
          family: "Blueprint",
          summary:
            "Review extracted protocol drafts into governed study structures with source lineage.",
          requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
        },
        "amendment-diff": {
          routeName: "amendment-diff",
          defaultPath: "/amendment-diff",
          title: "Protocol amendments",
          family: "Blueprint",
          summary:
            "Compare protocol versions side by side and assess downstream casebook migration impact.",
          requiresRole: [
            "sponsor_designer",
            "data_manager",
            "sponsor_admin",
            "super_admin",
            "cra",
            "monitor",
            "site_investigator",
            "crc",
          ],
        },
        ecrf: {
          routeName: "ecrf",
          defaultPath: "/ecrf",
          title: "Data collection",
          family: "Capture",
          summary:
            "Capture clinical visit observations, validate case report forms, and complete source data verification.",
          requiresRole: [
            "site_investigator",
            "crc",
            "data_manager",
            "sponsor_admin",
            "cra",
            "monitor",
          ],
        },
        observation: {
          routeName: "observation",
          defaultPath:
            "/observations/observation:00000000-0000-0000-0000-000000000001", // deid-ignore
          title: "Observation record",
          family: "Capture",
          summary:
            "Inspect an individual clinical observation, revision history, audit chain, and governed corrections.",
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
        rules: {
          routeName: "rules",
          defaultPath: "/rules",
          title: "Validation rules",
          family: "Capture",
          summary:
            "Configure edit-check specifications, cross-visit validation logic, and discrepancy triage.",
          requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
        },
        coding: {
          routeName: "coding",
          defaultPath: "/coding",
          title: "Medical coding",
          family: "Capture",
          summary:
            "Map verbatim clinical terms to governed medical dictionaries with versioned audit trails.",
          requiresRole: [
            "data_manager",
            "sponsor_designer",
            "sponsor_admin",
            "super_admin",
          ],
        },
        "data-lock": {
          routeName: "data-lock",
          defaultPath: "/data-lock",
          title: "Data locks",
          family: "Capture",
          summary:
            "Coordinate study, site, participant, and form lock readiness with electronic attestations.",
          requiresRole: [
            "data_manager",
            "sponsor_designer",
            "sponsor_admin",
            "super_admin",
          ],
        },
        ctms: {
          routeName: "ctms",
          defaultPath: "/ctms",
          title: "Site readiness",
          family: "Orbit",
          summary:
            "Track site activation checklists, monitoring visits, delegation logs, and operational milestones.",
          requiresRole: [
            "cra",
            "monitor",
            "data_manager",
            "sponsor_admin",
            "unblinded_statistician",
            "pharmacist",
            "trial_coordinator",
          ],
        },
        ssu: {
          routeName: "ssu",
          defaultPath: "/ssu",
          title: "Site startup",
          family: "Orbit",
          summary:
            "Coordinate regulatory greenlight prerequisites, country readiness matrices, and site activation.",
          requiresRole: [
            "cra",
            "monitor",
            "data_manager",
            "sponsor_admin",
            "site_investigator",
            "crc",
            "trial_coordinator",
            "unblinded_statistician",
            "pharmacist",
          ],
        },
        etmf: {
          routeName: "etmf",
          defaultPath: "/etmf",
          title: "Trial documents",
          family: "Orbit",
          summary:
            "Manage essential regulatory binder artifacts, completeness checks, and inspection readiness.",
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
        documents: {
          routeName: "documents",
          defaultPath: "/documents",
          title: "Controlled documents",
          family: "Orbit",
          summary:
            "Distribute controlled clinical documents, investigator brochures, and site acknowledgements.",
          requiresRole: [],
        },
        tickets: {
          routeName: "tickets",
          defaultPath: "/tickets",
          title: "Support tickets",
          family: "Orbit",
          summary:
            "Track operational site issues, monitoring action items, service targets, and escalations.",
          requiresRole: [],
        },
        "participant-wallet": {
          routeName: "participant-wallet",
          defaultPath: "/participant-wallet",
          title: "Participant reimbursements",
          family: "Orbit",
          summary:
            "Administer participant visit stipends, travel expense claims, and disbursement approvals.",
          requiresRole: [],
        },
        "icf-builder": {
          routeName: "icf-builder",
          defaultPath: "/icf-builder",
          title: "Informed consent builder",
          family: "Engage",
          summary:
            "Author informed consent forms, ethics version tracking, and participant comprehension checks.",
          requiresRole: [],
        },
        "econsent-authoring": {
          routeName: "econsent-authoring",
          defaultPath: "/econsent-authoring",
          title: "Consent authoring",
          family: "Engage",
          summary:
            "Prepare structured consent templates and multi-language site localization packages.",
          requiresRole: ["sponsor_designer", "data_manager", "sponsor_admin"],
        },
        dct: {
          routeName: "dct",
          defaultPath: "/dct",
          title: "Decentralized visits",
          family: "Engage",
          summary:
            "Coordinate remote visit scheduling, telehealth consultations, and home nursing workflows.",
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
        "lay-summaries": {
          routeName: "lay-summaries",
          defaultPath: "/lay-summaries",
          title: "Plain-language summaries",
          family: "Engage",
          summary:
            "Author plain-language trial results summaries for participants and public registries.",
          requiresRole: [],
        },
        devices: {
          routeName: "devices",
          defaultPath: "/devices",
          title: "Medical devices",
          family: "Allocate",
          summary:
            "Track investigational device exposure, firmware version lineage, and site accountability.",
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
        "device-investigation": {
          routeName: "device-investigation",
          defaultPath: "/device-investigation",
          title: "Device investigation",
          family: "Allocate",
          summary:
            "Trace device incident investigations linking hardware and firmware versions to observations.",
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
        imaging: {
          routeName: "imaging",
          defaultPath: "/imaging",
          title: "Medical imaging",
          family: "Lens",
          summary:
            "Review de-identified imaging studies, lesion measurements, and dual-reader response assessments.",
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
        lakehouse: {
          routeName: "lakehouse",
          defaultPath: "/lakehouse",
          title: "Clinical analytics",
          family: "Lens",
          summary:
            "Explore traceable clinical dataset projections from source observations to analysis views.",
          requiresRole: [
            "data_manager",
            "sponsor_designer",
            "sponsor_admin",
            "sponsor_statistician",
            "unblinded_statistician",
            "super_admin",
          ],
        },
        sensors: {
          routeName: "sensors",
          defaultPath: "/sensors",
          title: "Connected sensors",
          family: "Lens",
          summary:
            "Monitor wearable telemetry streams and device-origin provenance into clinical observations.",
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
        labs: {
          routeName: "labs",
          defaultPath: "/labs",
          title: "Laboratory governance",
          family: "Lens",
          summary:
            "Manage central and local laboratory reference ranges, unit normalizations, and grading.",
          requiresRole: [
            "site_investigator",
            "crc",
            "data_manager",
            "cra",
            "monitor",
            "sponsor_designer",
            "sponsor_admin",
            "super_admin",
          ],
        },
        safety: {
          routeName: "safety",
          defaultPath: "/safety",
          title: "Safety review",
          family: "Vigil",
          summary:
            "Compile serious adverse event narratives, signal assessments, and aggregate safety summaries.",
          requiresRole: [
            "safety_physician",
            "qppv",
            "qppv_officer",
            "qualified_person_pv",
            "safety_officer",
            "sponsor_pv_officer",
            "sponsor_medical_monitor",
            "medical_monitor",
            "sponsor_admin",
            "data_manager",
            "super_admin",
          ],
        },
        adjudication: {
          routeName: "adjudication",
          defaultPath: "/adjudication",
          title: "Endpoint adjudication",
          family: "Vigil",
          summary:
            "Assemble blinded clinical endpoint dossiers and record independent committee determinations.",
          requiresRole: [
            "site_investigator",
            "crc",
            "data_manager",
            "sponsor_admin",
            "cra",
            "monitor",
            "sponsor_designer",
            "auditor",
            "tmf_auditor",
          ],
        },
        cec: {
          routeName: "cec",
          defaultPath: "/cec",
          title: "Clinical events committee",
          family: "Vigil",
          summary:
            "Coordinate clinical events committee charter workflows, reviewer assignments, and consensus voting.",
          requiresRole: [
            "site_investigator",
            "crc",
            "data_manager",
            "sponsor_admin",
            "cra",
            "monitor",
            "sponsor_designer",
            "auditor",
            "tmf_auditor",
          ],
        },
        quality: {
          routeName: "quality",
          defaultPath: "/quality",
          title: "Quality management",
          family: "Assure",
          summary:
            "Monitor key risk indicators, protocol deviations, and corrective and preventive actions.",
          requiresRole: [
            "sponsor_designer",
            "data_manager",
            "sponsor_admin",
            "super_admin",
            "cra",
            "monitor",
            "site_investigator",
            "crc",
            "auditor",
            "tmf_auditor",
          ],
        },
        knowledge: {
          routeName: "knowledge",
          defaultPath: "/knowledge",
          title: "Knowledge & procedures",
          family: "Assure",
          summary:
            "Consult published standard operating procedures, study manuals, and controlled guidance.",
          requiresRole: [],
        },
        training: {
          routeName: "training",
          defaultPath: "/training",
          title: "Study training",
          family: "Assure",
          summary:
            "Verify protocol curricula, staff qualification assignments, and delegation sign-off.",
          requiresRole: [],
        },
        audit: {
          routeName: "audit",
          defaultPath: "/audit",
          title: "Audit ledger",
          family: "Cadence Core",
          summary:
            "Verify hash-chained 21 CFR Part 11 audit records, actor provenance, and electronic signatures.",
          requiresRole: ["auditor", "tmf_auditor", "sponsor_admin"],
        },
        auditor: {
          routeName: "auditor",
          defaultPath: "/auditor",
          title: "Inspection portal",
          family: "Cadence Core",
          summary:
            "Conduct read-and-export regulatory inspection reviews across audit chains and attestations.",
          requiresRole: ["auditor", "tmf_auditor", "sponsor_admin"],
        },
        exports: {
          routeName: "exports",
          defaultPath: "/exports",
          title: "Regulated exports",
          family: "Cadence Core",
          summary:
            "Generate authorized clinical record and audit-trail export packages for submission and archiving.",
          requiresRole: [
            "data_manager",
            "sponsor_designer",
            "sponsor_admin",
            "super_admin",
            "auditor",
            "tmf_auditor",
          ],
        },
        notifications: {
          routeName: "notifications",
          defaultPath: "/notifications",
          title: "Notifications",
          family: "Cadence Core",
          summary:
            "Review actionable clinical task alerts, query assignments, and study milestone notices.",
          requiresRole: [],
        },
      });

/**
 * @returns {DemoCatalogModule[]}
 */
export function listDemoCatalogModules() {
  if (!isDemoBuild()) return [];
  const familyRank = new Map(
    DEMO_PRODUCT_FAMILY_ORDER.map((family, index) => [family, index])
  );
  return [...DEMO_AVAILABLE_SCREENS]
    .map((routeName) => DEMO_MODULE_CATALOG[routeName])
    .filter(Boolean)
    .sort((a, b) => {
      const rankA = familyRank.get(a.family) ?? 999;
      const rankB = familyRank.get(b.family) ?? 999;
      if (rankA !== rankB) return rankA - rankB;
      return a.title.localeCompare(b.title);
    });
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
