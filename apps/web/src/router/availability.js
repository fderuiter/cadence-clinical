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
  // Execution: subjects, forms, observations, SDV, queries (#4828).
  "ecrf",
  // Execution: site activation and eTMF compliance status; the other CTMS tabs run only in a demo build.
  "ctms",
  "ssu",
]);

/** Routes that are not screens over the API (sign-in, error pages). */
const ALWAYS_AVAILABLE = new Set([
  "landing",
  "login",
  "forbidden",
  "unavailable",
]);

/** Issue tracking the screens that are hidden until their backend exists. */
export const UNAVAILABLE_SCREENS_ISSUE =
  "https://github.com/fderuiter/cadence-clinical/issues/4829";

const SCREEN_TITLES = {
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
  notifications: "Notifications",
  tickets: "Clinical Issues & Tickets",
  documents: "Documents Hub",
  knowledge: "Knowledge & SOP Hub",
  training: "Study Training & Gating",
  "participant-wallet": "Participant Financial Wallet",
  "lay-summaries": "EU CTR Lay Summaries",
};

export function screenTitle(routeName) {
  return SCREEN_TITLES[routeName] || "This screen";
}

export function isScreenAvailable(routeName) {
  if (!routeName) return false;
  return ALWAYS_AVAILABLE.has(routeName) || AVAILABLE_SCREENS.has(routeName);
}
