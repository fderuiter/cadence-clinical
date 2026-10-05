/**
 * Which screens have a .NET backend behind them.
 *
 * Most screens were written against the Python services and call endpoints
 * that no .NET service serves (see docs/web/API_MIGRATION.md). Those screens
 * are hidden from the navigation, and their routes show the "not available
 * yet" page, until their module serves the endpoints they need. Issue #4829
 * tracks each hidden screen against the kernel roadmap.
 *
 * To make a screen available: move it onto the typed client for the real
 * specs in docs/openapi, delete its Python-era client code, and add its
 * route name here.
 */

/** Route names whose screens call endpoints the .NET services serve. */
export const AVAILABLE_SCREENS = new Set([
  // Clinical Canvas: participants and their counts, and site activation, from Execution.
  "study",
  // Execution: subjects, forms, observations, SDV, queries (#4828).
  "ecrf",
  // Execution: site activation and eTMF compliance status; the other CTMS tabs run only in a demo build.
  "ctms",
  "ssu",
]);

/** Routes that are not screens over the API (sign-in, error pages). */
const ALWAYS_AVAILABLE = new Set([
  "landing",
  "overview",
  "login",
  "forbidden",
  "unavailable",
]);

/** Issue tracking the screens that are hidden until their backend exists. */
export const UNAVAILABLE_SCREENS_ISSUE =
  "https://github.com/fderuiter/cadence-clinical/issues/4829";

const SCREEN_TITLES = {
  overview: "Overview & Orientation Hub",
  mdr: "Protocol & SoA Designer",
  digitization: "Protocol AI Digitizer",
  "icf-builder": "eConsent & ICF Builder",
  "econsent-authoring": "eConsent authoring",
  "amendment-diff": "Amendments & Migration",
  imaging: "DICOM & RECIST Viewer",
  devices: "Device & IVD Suite",
  "device-investigation": "Device & IVD Suite",
  adjudication: "CEC adjudication",
  cec: "CEC adjudication",
  audit: "Part 11 Audit Ledger",
  auditor: "Auditor portal",
  rules: "Study Rules & Queries",
  coding: "Medical Coding",
  "data-lock": "Data Lock Console",
  lakehouse: "Lakehouse",
  sensors: "Connected Sensors & IoT",
  labs: "Lab Governance",
  dct: "Telehealth & Direct-to-Patient",
  safety: "Safety & DSUR Compiler",
  exports: "Data Export Wizard",
  etmf: "eTMF Document Vault",
  quality: "Quality & RBQM Cockpit",
  "quality-forensics": "Statistical Forensics",
  notifications: "Notifications",
  tickets: "Clinical Issues & Tickets",
  documents: "Documents Hub",
  knowledge: "Knowledge & SOP Hub",
  training: "Study Training & Gating",
  "participant-wallet": "Participant Financial Wallet",
  "lay-summaries": "EU CTR Lay Summaries",
  "ctms-operations": "CTMS Core Operations",
  "ctms-monitoring": "CTMS Site Monitoring",
  "ctms-delegation": "CTMS Delegation Log",
  "ctms-financials": "CTMS Site Financials",
  "ctms-startup": "CTMS Study Startup",
  "ctms-rtsm": "CTMS RTSM",
  "forensics-overview": "Forensics Overview",
  "forensics-benford": "Forensics Benford",
  "forensics-rounding": "Forensics Rounding",
  "forensics-mahalanobis": "Forensics Mahalanobis",
  "forensics-variance": "Forensics Variance",
  "forensics-duplicate-patients": "Forensics Duplicate Patients",
  "forensics-temporal": "Forensics Temporal",
  "forensics-phash": "Forensics pHash",
  "safety-exposure": "Safety Exposure",
  "safety-tabulations": "Safety Tabulations",
  "safety-line-listings": "Safety Line Listings",
  "safety-signals": "Safety Signals",
  "safety-signoff": "Safety Sign-off",
  "safety-simulator": "Safety Simulator",
  "knowledge-documents": "Knowledge Documents",
  "knowledge-authoring": "Knowledge Authoring",
  "knowledge-audit": "Knowledge Audit",
};

export function screenTitle(routeName) {
  return SCREEN_TITLES[routeName] || "This screen";
}

export function isScreenAvailable(routeName) {
  if (!routeName) return false;
  if (ALWAYS_AVAILABLE.has(routeName) || AVAILABLE_SCREENS.has(routeName)) {
    return true;
  }
  const prefix = String(routeName).split("-")[0];
  if (prefix && AVAILABLE_SCREENS.has(prefix)) {
    return true;
  }
  if (
    String(routeName).startsWith("forensics-") &&
    (AVAILABLE_SCREENS.has("quality") ||
      AVAILABLE_SCREENS.has("quality-forensics"))
  ) {
    return true;
  }
  return false;
}
