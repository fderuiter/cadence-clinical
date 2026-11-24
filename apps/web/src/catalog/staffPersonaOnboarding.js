import {
  DEFAULT_SETUP_TASKS,
  PERSONA_CHECKLISTS,
  getPersonaChecklist,
} from "./staffPersonaChecklists.js";
import { resolvePersonaCatalogKey } from "./staffPersonaCore.js";

export { DEFAULT_SETUP_TASKS, PERSONA_CHECKLISTS, getPersonaChecklist };

const SEL_STUDIES = "#nav-studies, #btn-mobile-menu-toggle";
const SEL_DATA = "#nav-data-collection, #btn-mobile-menu-toggle";
const SEL_SITE = "#nav-site-readiness, #btn-mobile-menu-toggle";
const SEL_PALETTE = "#btn-open-palette";
const SEL_CHECKLIST = "#btn-onboarding-checklist-trigger";
const SEL_HELP = "#btn-contextual-help-trigger";

function buildTour(entries) {
  return entries.map(
    ([routeName, targetSelector, title, description, prompt], idx) => ({
      step: idx + 1,
      routeName,
      targetSelector,
      title,
      description,
      prompt,
    })
  );
}

function welcome(routeName, title, role, description) {
  return [
    routeName,
    null,
    `Welcome to ${title}`,
    description,
    `Welcome, ${role}! ${description}`,
  ];
}

function navStep(routeName, selector, title, description) {
  return [routeName, selector, title, description, description];
}

function paletteStep(routeName, scope) {
  return [
    routeName,
    SEL_PALETTE,
    "Quick Jump & Search",
    `Press ⌘K (or Ctrl+K) to jump directly across ${scope}.`,
    `Search studies and navigate ${scope} from anywhere.`,
  ];
}

function checklistStep(routeName, label) {
  return [
    routeName,
    SEL_CHECKLIST,
    label,
    "Open the setup checklist from the header to track milestones and preferences.",
    "Track setup progress and manage workspace orientation anytime.",
  ];
}

function done(routeName, domainLabel, selector = SEL_HELP) {
  return [
    routeName,
    selector,
    "Tour Complete!",
    `Your ${domainLabel} orientation is complete. Open Help or Search (⌘K) anytime for SOP guidance.`,
    `Ready! Your ${domainLabel} workspace is configured.`,
  ];
}

export const PERSONA_TOUR_SEQUENCES = Object.freeze({
  super_admin: buildTour([
    welcome(
      "study",
      "Cadence Clinical",
      "Administrator",
      "Oversee studies, data collection, site readiness, and governance in one workspace."
    ),
    navStep(
      "study",
      SEL_STUDIES,
      "Studies & Lifecycle",
      "Open any study by code to inspect Design through Submit stages and governance."
    ),
    navStep(
      "ecrf",
      SEL_DATA,
      "Data Collection",
      "Review case report forms, field validations, and discrepancy queries."
    ),
    navStep(
      "ctms",
      SEL_SITE,
      "Site Readiness",
      "Monitor site activation checklists, startup progress, and site status."
    ),
    done("study", "workspace"),
  ]),
  sponsor_designer: buildTour([
    welcome(
      "study",
      "Protocol Design",
      "Designer",
      "Structure study stages, inspect protocol versions, and review governance."
    ),
    navStep(
      "study",
      SEL_STUDIES,
      "Studies Workspace",
      "Select a study to inspect Design, Start, Conduct, Data, Analyze, and Submit."
    ),
    paletteStep("study", "lifecycle stages and workspace tools"),
    checklistStep("overview", "Orientation & Setup Checklist"),
    done("study", "protocol design"),
  ]),
  data_manager: buildTour([
    welcome(
      "ecrf",
      "Data Management",
      "Data Manager",
      "Review case report forms, validation rules, and discrepancy queries."
    ),
    navStep(
      "ecrf",
      SEL_DATA,
      "Data Collection & Validation",
      "Verify range checks, required observations, and discrepancy query resolution."
    ),
    navStep(
      "study",
      SEL_STUDIES,
      "Study Conduct & Data Stages",
      "Inspect participant counts, validation findings, and database lock readiness."
    ),
    paletteStep("study", "Studies and Data collection"),
    done("ecrf", "data management"),
  ]),
  site_crc: buildTour([
    welcome(
      "ecrf",
      "Data Collection",
      "Site Coordinator",
      "Screen participants, record visit observations, and track site readiness."
    ),
    navStep(
      "ecrf",
      "#ecrf-participant-bar, .subject-enrollment-card",
      "Participant Selection & Visits",
      "Select study participants, review visits, and enroll subjects for your site."
    ),
    navStep(
      "ecrf",
      ".crc-form-renderer, #ecrf-form-instance",
      "Case Report Form Entry",
      "Enter observations with real-time validation checks and query flags."
    ),
    navStep(
      "ssu",
      SEL_SITE,
      "Site Readiness",
      "Check site activation tasks and operational readiness for your site."
    ),
    done("study", "site coordinator", SEL_STUDIES),
  ]),
  site_investigator: buildTour([
    welcome(
      "ecrf",
      "Investigator Oversight",
      "Principal Investigator",
      "Review site observations, resolve queries, and sign casebooks under 21 CFR Part 11."
    ),
    navStep(
      "ecrf",
      SEL_DATA,
      "Casebook Review & Sign-Off",
      "Inspect completed visit forms and apply re-authenticated electronic signatures."
    ),
    navStep(
      "ssu",
      SEL_SITE,
      "Site Startup & Delegation",
      "Confirm site activation milestones and investigator delegation obligations."
    ),
    navStep(
      "study",
      SEL_STUDIES,
      "Study Conduct & Safety Oversight",
      "Monitor enrolled participants, protocol versions, and unblinding controls."
    ),
    done("ecrf", "investigator"),
  ]),
  cra_monitor: buildTour([
    welcome(
      "ctms",
      "Clinical Operations",
      "CRA",
      "Oversee site activation readiness, startup milestones, and source data verification."
    ),
    navStep(
      "ctms",
      SEL_SITE,
      "Site Readiness & Activation",
      "Track site activation requirements, readiness, and startup progress."
    ),
    navStep(
      "ecrf",
      SEL_DATA,
      "Source Data Verification",
      "Navigate to participant forms to verify observations and manage queries."
    ),
    navStep(
      "study",
      SEL_STUDIES,
      "Study Lifecycle Overview",
      "Inspect participant state counts, site progress, and governance."
    ),
    done("ctms", "clinical monitoring"),
  ]),
  safety_officer: buildTour([
    welcome(
      "study",
      "Pharmacovigilance & Safety",
      "Safety Officer",
      "Monitor serious adverse events, safety signals, and pharmacovigilance workflows."
    ),
    navStep(
      "safety",
      null,
      "Expedited Safety Triage",
      "Inspect serious adverse event cases, causality, and reporting clocks."
    ),
    paletteStep("tickets", "safety inquiries and studies"),
    done("knowledge", "pharmacovigilance"),
  ]),
  auditor: buildTour([
    welcome(
      "study",
      "Regulatory & Audit",
      "Auditor",
      "Inspect study activity records, protocol versions, and attested decisions."
    ),
    navStep(
      "study",
      SEL_STUDIES,
      "Study Activity & Provenance",
      "Open a study by code to review activity records and protocol versions."
    ),
    paletteStep("study", "Design, Conduct, and Submit stages"),
    checklistStep("overview", "Inspection Orientation"),
    done("study", "regulatory audit"),
  ]),
  pharmacist: buildTour([
    welcome(
      "study",
      "Investigational Supply",
      "Site Pharmacist",
      "Manage site kit inventory, dispensation verification, and blind-firewall accountability."
    ),
    navStep(
      "study",
      SEL_STUDIES,
      "Study Conduct & Supply",
      "Open your study workspace and select Conduct > Supply for kit status."
    ),
    paletteStep("training", "Supply, Training, Tickets, and SOPs"),
    checklistStep("tickets", "Excursion & Resupply Support"),
    done("study", "site pharmacist"),
  ]),
  unblinded_statistician: buildTour([
    welcome(
      "study",
      "Unblinded Biostatistics",
      "Unblinded Statistician",
      "Author and attest encrypted randomization schemes with separation of duties."
    ),
    navStep(
      "study",
      SEL_STUDIES,
      "Randomization & Analyze Stages",
      "Review Start randomization schemes and Analyze statistical readiness."
    ),
    paletteStep("knowledge", "blinding-firewall SOPs and study stages"),
    checklistStep("training", "Qualification & Setup Progress"),
    done("study", "unblinded biostatistics"),
  ]),
});

export function getPersonaOnboardingEntry(personaKey, rawRoles = []) {
  const resolvedKey = resolvePersonaCatalogKey(personaKey, rawRoles);
  return {
    key: resolvedKey,
    checklist: PERSONA_CHECKLISTS[resolvedKey] || DEFAULT_SETUP_TASKS,
    tourSequence:
      PERSONA_TOUR_SEQUENCES[resolvedKey] || PERSONA_TOUR_SEQUENCES.super_admin,
  };
}
