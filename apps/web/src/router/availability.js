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

export { SCREEN_CATALOG, screenMetadata, screenTitle } from "./screenCatalog";

export const BRAND_TITLE_SUFFIX = "Cadence Clinical";

/**
 * Study-centric, UX-1 compliant page titles for all workspace routes.
 * Never exposes product silo acronyms (EDC, CTMS, eTMF, RTSM, P21, QMS, Safety).
 */
export const ROUTE_TITLES = /* @__PURE__ */ Object.freeze({
  landing: "Study Workspace",
  overview: "Workspace Overview",
  plans: "Subscription Plans & Study Scoping",
  "trial-onboarding": "Trial Workspace Setup",
  login: "Sign In",
  forbidden: "Access Restricted",
  unavailable: "Module Unavailable",
  study: "Studies",
  ecrf: "Clinical Data Collection",
  observation: "Clinical Observation Record",
  ctms: "Site Readiness & Operations",
  ssu: "Site Startup & Activation",
  etmf: "Trial Master File & Regulatory Binder",
  training: "Protocol Training & Qualification",
  tickets: "Operational Issues & Support",
  knowledge: "Standard Operating Procedures & Guidance",
  "data-lock": "Clinical Data Locks",
  exports: "Regulated Data Exports",
  mdr: "Protocol & Schedule Designer",
  digitization: "Protocol Document Digitization",
  "icf-builder": "Informed Consent Form Builder",
  "econsent-authoring": "Consent Template Authoring",
  "amendment-diff": "Protocol Amendments & Migration",
  imaging: "Medical Imaging & Lesion Review",
  devices: "Investigational Medical Devices",
  "device-investigation": "Device Incident Traceability",
  adjudication: "Clinical Endpoint Adjudication",
  cec: "Clinical Endpoint Adjudication",
  audit: "21 CFR Part 11 Audit Ledger",
  auditor: "Regulatory Inspection Portal",
  rules: "Validation Rules & Discrepancy Queries",
  coding: "Medical Terminology Coding",
  lakehouse: "Clinical Dataset Projections",
  sensors: "Connected Sensor Telemetry",
  labs: "Laboratory Reference Governance",
  dct: "Decentralized Visits & Telehealth",
  safety: "Pharmacovigilance & Aggregate Reporting",
  quality: "Risk-Based Quality Management",
  notifications: "Study Notifications & Task Alerts",
  documents: "Controlled Study Documents",
  "participant-wallet": "Participant Stipends & Reimbursements",
  "lay-summaries": "Plain-Language Trial Summaries",
});

export const STUDY_STAGE_TITLES = Object.freeze({
  overview: "Overview",
  design: "Protocol Design",
  startup: "Site Startup",
  conduct: "Study Conduct",
  data: "Data Review",
  submit: "Submission & Archival",
});

export const STUDY_SECTION_TITLES = Object.freeze({
  participants: "Participants",
  sites: "Sites",
  protocol: "Protocol",
  forms: "Forms",
  queries: "Discrepancy Queries",
  locks: "Data Locks",
  exports: "Regulated Exports",
  monitoring: "Monitoring Visits",
  supply: "Investigational Supply",
  documents: "Regulatory Documents",
  training: "Qualification & Training",
});

const NON_STUDY_SCOPED_ROUTES = new Set([
  "landing",
  "home",
  "overview",
  "plans",
  "trial-onboarding",
  "login",
  "forbidden",
  "unavailable",
]);

function sanitizeStudyIdentifier(raw) {
  if (typeof raw !== "string") return "";
  const trimmed = raw.trim();
  if (!trimmed || trimmed.length > 48) return "";
  return trimmed;
}

/**
 * Resolves the study-centric view label (without brand suffix), omitting participant PII.
 *
 * @param {object} to - Vue Router route location
 * @param {{ studyCode?: string }} [context]
 * @returns {string}
 */
export function resolveRouteViewLabel(to, context = {}) {
  const routeName = typeof to?.name === "string" ? to.name : "";
  const routePath = typeof to?.path === "string" ? to.path : "";

  if (routeName === "unavailable" || routePath === "/unavailable") {
    const requestedScreen =
      typeof to?.query?.screen === "string" ? to.query.screen.trim() : "";
    const targetTitle = ROUTE_TITLES[requestedScreen];
    return targetTitle
      ? `${targetTitle} (Unavailable)`
      : ROUTE_TITLES.unavailable;
  }

  const explicitStudyCode =
    sanitizeStudyIdentifier(to?.params?.studyCode) ||
    sanitizeStudyIdentifier(to?.query?.studyCode) ||
    sanitizeStudyIdentifier(to?.query?.studyId) ||
    sanitizeStudyIdentifier(context?.studyCode);

  if (routeName === "study" || routePath.startsWith("/studies")) {
    const paramStudyCode = sanitizeStudyIdentifier(to?.params?.studyCode);
    const rawStage =
      typeof to?.query?.stage === "string"
        ? to.query.stage.trim().toLowerCase()
        : "";
    const rawSection =
      typeof to?.query?.section === "string"
        ? to.query.section.trim().toLowerCase()
        : "";
    const stageTitle = STUDY_STAGE_TITLES[rawStage] || "";
    const sectionTitle = STUDY_SECTION_TITLES[rawSection] || "";

    const parts = [
      paramStudyCode ? `Study ${paramStudyCode}` : ROUTE_TITLES.study,
    ];
    if (stageTitle && sectionTitle) {
      parts.push(`${stageTitle} (${sectionTitle})`);
    } else if (stageTitle) {
      parts.push(stageTitle);
    } else if (sectionTitle) {
      parts.push(sectionTitle);
    }
    return parts.join(" · ");
  }

  const pathSlug = routePath.replace(/^\//, "").split("/")[0];
  const baseLabel =
    ROUTE_TITLES[routeName] || ROUTE_TITLES[pathSlug] || "Study Workspace";

  if (explicitStudyCode && !NON_STUDY_SCOPED_ROUTES.has(routeName)) {
    return `${baseLabel} · ${explicitStudyCode}`;
  }

  return baseLabel;
}

/**
 * Resolves the full `<title>` string ending in ` — Cadence Clinical`.
 *
 * @param {object} to - Vue Router route location
 * @param {{ studyCode?: string }} [context]
 * @returns {string}
 */
export function resolveRouteTitle(to, context = {}) {
  const label = resolveRouteViewLabel(to, context);
  return `${label} — ${BRAND_TITLE_SUFFIX}`;
}

/**
 * Formats a polite status announcement for post-load route transitions,
 * omitting participant PII.
 *
 * @param {object} to - Vue Router route location
 * @param {{ studyCode?: string }} [context]
 * @returns {string}
 */
export function formatRouteAnnouncement(to, context = {}) {
  const label = resolveRouteViewLabel(to, context);
  return `Navigated to ${label}`;
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
          requiresRole: ["cra", "monitor", "data_manager", "sponsor_admin"],
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
