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

import { isDemoBuild } from "../utils/demoMode";

/** Route names whose screens call endpoints the .NET services serve. */
export const AVAILABLE_SCREENS = new Set([
  // Clinical Canvas: lifecycle stages (Design, Start, Conduct, Data) over Kernel, Execution, CTMS, eTMF, and Knowledge.
  "study",
  // Execution: subjects, forms, observations, SDV, queries (#4828).
  "ecrf",
  // Kernel: one observation and its correction (ADR-2620).
  "observation",
  // Execution + CTMS + eTMF: site activation, milestones, monitoring visits & letters, delegation of authority, and startup greenlight.
  "ctms",
  "ssu",
  // eTMF: Trial Master File binder, essential document QC staging, and clinical QC review.
  "etmf",
  // Knowledge: protocol curricula, training assignments, and 21 CFR Part 11 delegation sign-off.
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

export function isScreenAvailable(routeName, env) {
  if (!routeName) return false;
  if (ALWAYS_AVAILABLE.has(routeName) || AVAILABLE_SCREENS.has(routeName)) {
    return true;
  }
  const demoActive =
    env !== undefined
      ? isDemoBuild(env)
      : import.meta.env.MODE === "demo" ||
        import.meta.env.VITE_DEMO_MODE === "true";
  if (demoActive) {
    return DEMO_AVAILABLE_SCREENS.has(routeName);
  }
  return false;
}
