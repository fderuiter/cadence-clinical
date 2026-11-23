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
