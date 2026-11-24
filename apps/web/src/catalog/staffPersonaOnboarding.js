import { resolvePersonaCatalogKey } from "./staffPersonaCore.js";

const SEL_STUDIES = "#nav-studies, #btn-mobile-menu-toggle";
const SEL_DATA = "#nav-data-collection, #btn-mobile-menu-toggle";
const SEL_SITE = "#nav-site-readiness, #btn-mobile-menu-toggle";
const SEL_PALETTE = "#btn-open-palette";
const SEL_CHECKLIST = "#btn-onboarding-checklist-trigger";
const SEL_HELP = "#btn-contextual-help-trigger";

const ROUTE_PATHS = {
  study: "/studies",
  "site-readiness": "/ctms",
};

const KW_STUDY = ["study", "studies", "protocol", "welcome"];
const KW_SOA = [
  "lifecycle",
  "design",
  "version",
  "soa",
  "schedule",
  "arm",
  "epoch",
];
const KW_CRF = ["data", "collection", "crf", "ecrf", "form", "query"];
const KW_SITE = [
  "site",
  "readiness",
  "activation",
  "milestone",
  "rule",
  "rules",
  "query",
  "queries",
  "ssu",
];
const KW_SOP = ["knowledge", "sop", "guidance"];
const KW_TRAIN = ["training", "qualification"];
const KW_ETMF = ["documents", "regulatory", "filing"];
const KW_TICKETS = ["tickets", "escalation", "support"];

function task(id, title, description, routeName, keywords, route) {
  return {
    id,
    title,
    description,
    routeName,
    route: route || ROUTE_PATHS[routeName] || `/${routeName}`,
    completed: false,
    keywords,
  };
}

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

export const DEFAULT_SETUP_TASKS = Object.freeze([
  task(
    "task_protocol",
    "Open a Study Workspace",
    "Open a study by code to inspect stages, participants, and activity",
    "study",
    KW_STUDY
  ),
  task(
    "task_soa",
    "Review Study Lifecycle & Protocol Versions",
    "Navigate Design through Submit stages and inspect protocol versions",
    "study",
    KW_SOA
  ),
  task(
    "task_crf",
    "Verify Data Collection Forms",
    "Inspect case report forms, field validations, and discrepancy queries",
    "ecrf",
    KW_CRF
  ),
  task(
    "task_rules",
    "Check Site Readiness",
    "Review site activation milestones and startup readiness",
    "site-readiness",
    KW_SITE
  ),
  task(
    "task_exports",
    "Configure Workspace Orientation",
    "Set your default landing preference and explore SOP guidance",
    "overview",
    ["orientation", "overview", "preference", "help"]
  ),
]);

export const PERSONA_CHECKLISTS = Object.freeze({
  super_admin: DEFAULT_SETUP_TASKS,
  sponsor_designer: [
    task(
      "task_protocol",
      "Open Study Design Stage",
      "Inspect a study's Design stage and recorded protocol versions",
      "study",
      KW_STUDY
    ),
    task(
      "task_soa",
      "Navigate Study Lifecycle & Schedules",
      "Verify progression across Design, Start, Conduct, Data, Analyze, and Submit",
      "study",
      KW_SOA
    ),
    task(
      "task_design_knowledge",
      "Consult Protocol Authoring SOPs",
      "Review protocol design SOPs and governance guidance",
      "knowledge",
      KW_SOP
    ),
    task(
      "task_design_training",
      "Verify Role Qualification & Training",
      "Confirm protocol authoring curriculum in Study Training",
      "training",
      KW_TRAIN
    ),
  ],
  data_manager: [
    task(
      "task_crf",
      "Validation Rules & Discrepancy Queries",
      "Inspect case report forms, field checks, and discrepancy notes",
      "ecrf",
      KW_CRF
    ),
    task(
      "task_protocol",
      "Study Data & Lock Readiness",
      "Review participant counts, validation findings, and lock readiness",
      "study",
      KW_STUDY
    ),
    task(
      "task_rules",
      "Site Readiness & Activation",
      "Verify startup milestones and active site status",
      "site-readiness",
      KW_SITE
    ),
    task(
      "task_dm_etmf",
      "Essential Data Management Documents",
      "Confirm data management plan and validation filing status",
      "etmf",
      KW_ETMF
    ),
  ],
  site_crc: [
    task(
      "task_rules",
      "Site Readiness Check",
      "Verify site activation readiness and startup tasks for your site",
      "site-readiness",
      KW_SITE,
      "/ssu"
    ),
    task(
      "task_crf",
      "Participant Enrollment & Data Collection",
      "Register participants and complete scheduled visit forms",
      "ecrf",
      KW_CRF
    ),
    task(
      "task_protocol",
      "Study Participant Overview",
      "Inspect site participant statuses and conduct activity",
      "study",
      KW_STUDY
    ),
    task(
      "task_crc_training",
      "Complete Site Training Curriculum",
      "Confirm required site protocol and data entry training",
      "training",
      KW_TRAIN
    ),
  ],
  site_investigator: [
    task(
      "task_crf",
      "Case Report Form & Signature Review",
      "Review visit observations and sign completed case report forms",
      "ecrf",
      KW_CRF
    ),
    task(
      "task_rules",
      "Site Readiness & Investigator Oversight",
      "Confirm site startup obligations and delegation readiness",
      "site-readiness",
      KW_SITE,
      "/ssu"
    ),
    task(
      "task_protocol",
      "Study Conduct & Participant Oversight",
      "Inspect enrolled participants, safety alerts, and governance",
      "study",
      KW_STUDY
    ),
    task(
      "task_pi_etmf",
      "Investigator Regulatory Binder",
      "Verify investigator qualification and site filing status",
      "etmf",
      KW_ETMF
    ),
  ],
  cra_monitor: [
    task(
      "task_rules",
      "Site Readiness & Activation Review",
      "Track site onboarding milestones and activation state",
      "site-readiness",
      KW_SITE
    ),
    task(
      "task_crf",
      "Source Data Verification (SDV)",
      "Review and verify form entries at the participant level",
      "ecrf",
      KW_CRF
    ),
    task(
      "task_protocol",
      "Monitor Study Conduct",
      "Inspect study-wide participant counts and site status",
      "study",
      KW_STUDY
    ),
    task(
      "task_cra_etmf",
      "Essential Document Completeness",
      "Inspect site regulatory filing and monitoring artifacts",
      "etmf",
      KW_ETMF
    ),
  ],
  safety_officer: [
    task(
      "task_safety_triage",
      "Review Expedited Safety Cases",
      "Triage serious adverse events, narratives, and reporting timelines",
      "safety",
      ["safety", "adverse"]
    ),
    task(
      "task_protocol",
      "Study Safety & Conduct Overview",
      "Inspect participant status, safety signals, and protocol versions",
      "study",
      KW_STUDY
    ),
    task(
      "task_safety_tickets",
      "Track Safety & Escalation Queues",
      "Follow up on pharmacovigilance inquiries in Tickets",
      "tickets",
      KW_TICKETS
    ),
    task(
      "task_safety_knowledge",
      "Consult Pharmacovigilance SOPs",
      "Review expedited reporting and aggregate DSUR SOPs",
      "knowledge",
      KW_SOP
    ),
  ],
  auditor: [
    task(
      "task_protocol",
      "Study Activity Trail Verification",
      "Inspect chronological study activity and audit provenance",
      "study",
      KW_STUDY
    ),
    task(
      "task_soa",
      "Protocol Version & Governance Provenance",
      "Verify recorded protocol versions and attested decisions",
      "study",
      KW_SOA
    ),
    task(
      "task_auditor_etmf",
      "Inspect Essential Regulatory Documents",
      "Review read-only regulatory filing and audit trails",
      "etmf",
      KW_ETMF
    ),
    task(
      "task_auditor_knowledge",
      "Verify Controlled SOP Library",
      "Confirm published SOP versions and compliance guidance",
      "knowledge",
      KW_SOP
    ),
  ],
  pharmacist: [
    task(
      "task_protocol",
      "Inspect Site Supply & Kit Accountability",
      "Verify site product inventory and kit status in Conduct",
      "study",
      KW_STUDY
    ),
    task(
      "task_pharmacy_training",
      "Confirm Unblinded Pharmacy Qualification",
      "Complete product handling and blinding-firewall training",
      "training",
      KW_TRAIN
    ),
    task(
      "task_pharmacy_tickets",
      "Review Supply & Excursion Inquiries",
      "Track temperature excursions and resupply requests in Tickets",
      "tickets",
      KW_TICKETS
    ),
    task(
      "task_pharmacy_knowledge",
      "Consult Pharmacy Dispensing SOPs",
      "Review storage, dispensation, and reconciliation SOPs",
      "knowledge",
      KW_SOP
    ),
  ],
  unblinded_statistician: [
    task(
      "task_protocol",
      "Inspect Randomization & Allocation Governance",
      "Review randomization schemes, stratification, and two-person approval",
      "study",
      KW_STUDY
    ),
    task(
      "task_stat_knowledge",
      "Review Statistical & Blinding Firewall SOPs",
      "Verify randomization encryption and unblinded governance SOPs",
      "knowledge",
      KW_SOP
    ),
    task(
      "task_stat_training",
      "Confirm Unblinded Biostatistics Qualification",
      "Complete unblinded qualification and second-person review training",
      "training",
      KW_TRAIN
    ),
    task(
      "task_stat_tickets",
      "Check Statistical Governance Inquiries",
      "Review audited inquiries for randomization lists and DMC readiness",
      "tickets",
      KW_TICKETS
    ),
  ],
});

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
