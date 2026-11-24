import { PERSONA_SUMMARY_CATALOG, freezeDeep } from "./personaSummaries.js";

const SEL_STUDIES = "#nav-studies, #btn-mobile-menu-toggle";
const SEL_DATA = "#nav-data-collection, #btn-mobile-menu-toggle";
const SEL_SITE = "#nav-site-readiness, #btn-mobile-menu-toggle";
const SEL_PARTICIPANT = "#ecrf-participant-bar, .subject-enrollment-card";
const SEL_FORM = ".crc-form-renderer, #ecrf-form-instance";
const SEL_CHECKLIST = "#btn-onboarding-checklist-trigger";
const SEL_HELP = "#btn-contextual-help-trigger";

const defTaskCopy = (title, description, keywords) => ({
  title,
  description,
  keywords,
});

const defStep = (routeName, targetSelector, title, description, prompt) => ({
  routeName,
  targetSelector,
  title,
  description,
  prompt,
});

function buildPersonaCatalog() {
  const detailsByKey = {
    super_admin: {
      tasks: [
        defTaskCopy(
          "Author Protocol & Endpoints",
          "Define study design, arms, and eligibility criteria.",
          ["study", "studies", "protocol", "welcome", "endpoints"]
        ),
        defTaskCopy(
          "Configure Schedule of Activities",
          "Map study visits, cohorts, and assessment windows.",
          ["soa", "schedule", "activities", "visits"]
        ),
        defTaskCopy(
          "Design Casebook Forms",
          "Configure casebook forms and field validation rules.",
          ["ecrf", "crf", "canvas", "form"]
        ),
        defTaskCopy(
          "Verify Site Readiness & Monitoring",
          "Review site activation milestones and monitoring schedules.",
          ["ctms", "monitoring", "site", "allocation"]
        ),
        defTaskCopy(
          "Review Regulatory & Archival Readiness",
          "Inspect audit readiness, signature logs, and submissions.",
          ["export", "odm", "audit", "submission", "archive"]
        ),
      ],
      steps: [
        defStep(
          "study",
          null,
          "Welcome to Cadence Clinical",
          "Oversee protocol design, conduct, site operations, safety, and Part 11 governance.",
          "Welcome, Administrator! Oversee studies, data collection, site readiness, and governance."
        ),
        defStep(
          "study",
          SEL_STUDIES,
          "Studies & Clinical Canvas",
          "Inspect protocol versions, visit schedules, and milestone locks on the Clinical Canvas.",
          "Review participant counts, protocol versions, and governance proposals."
        ),
        defStep(
          "ecrf",
          SEL_DATA,
          "Data Collection & Casebooks",
          "Review enrollment, clinical form instances, source verification, and queries.",
          "Verify structured data capture and real-time validation checks."
        ),
        defStep(
          "ctms",
          SEL_SITE,
          "Site Operations & Startup",
          "Monitor site activation readiness, monitoring visits, and recruitment.",
          "Oversee site readiness and activation across clinical sites."
        ),
        defStep(
          "overview",
          SEL_HELP,
          "Contextual Guidance & Support",
          "Open role-tailored SOP guidance and workspace checklists from any screen.",
          "Your unified clinical workspace is ready."
        ),
      ],
    },
    sponsor_designer: {
      tasks: [
        defTaskCopy(
          "Inspect Study Protocol Structure",
          "Review study arms, epochs, objectives, and protocol versions.",
          ["protocol", "usdm", "arms", "design"]
        ),
        defTaskCopy(
          "Verify Schedule of Activities Matrix",
          "Confirm planned visits, windows, and collection forms.",
          ["soa", "schedule", "visits", "matrix"]
        ),
        defTaskCopy(
          "Review Inclusion & Exclusion Criteria",
          "Validate eligibility criteria and terminology bindings.",
          ["eligibility", "inclusion", "exclusion", "terminology"]
        ),
      ],
      steps: [
        defStep(
          "study",
          null,
          "Welcome to Protocol Design",
          "Author structured trial designs, visit schedules, and collection forms.",
          "Welcome, Designer! Navigate study lifecycle stages and inspect protocol versions."
        ),
        defStep(
          "study",
          SEL_STUDIES,
          "Structured Study Design",
          "Navigate protocol versions, study arms, encounters, and collection specs.",
          "Review study lifecycle sections from one unified workspace."
        ),
        defStep(
          "overview",
          SEL_CHECKLIST,
          "Design Readiness Checklist",
          "Track protocol authoring, visit schedule, and terminology milestones.",
          "Track setup progress and orientation preferences from the header."
        ),
        defStep(
          "overview",
          SEL_HELP,
          "Protocol Authoring Guidance",
          "Reference CDISC USDM and CDASH standards alongside your workspace.",
          "Use Studies, Search, Orientation, and Help from the top bar."
        ),
      ],
    },
    data_manager: {
      tasks: [
        defTaskCopy(
          "Review Casebook Data & Edit Checks",
          "Inspect form values, range checks, and pre-commit warnings.",
          ["ecrf", "crf", "canvas", "form", "validation"]
        ),
        defTaskCopy(
          "Triage & Close Clinical Queries",
          "Evaluate site responses, verify resolutions, and close queries.",
          ["query", "discrepancy", "triage"]
        ),
        defTaskCopy(
          "Verify Milestone Freeze & Lock Readiness",
          "Check open queries and signature completeness before lock.",
          ["lock", "freeze", "dataset", "clean"]
        ),
        defTaskCopy(
          "Coordinate Site Data Quality Follow-Up",
          "Align with monitors on query aging and verification throughput.",
          ["site", "oversight", "monitoring"]
        ),
      ],
      steps: [
        defStep(
          "ecrf",
          null,
          "Welcome to Data Management",
          "Oversee data cleanliness, query triage, edit checks, and lock readiness.",
          "Welcome, Data Manager! Inspect form validations, queries, and participant progress."
        ),
        defStep(
          "ecrf",
          SEL_FORM,
          "Casebook Discrepancy & Query Review",
          "Inspect item revisions, edit check findings, and query threads.",
          "Validate visit forms and monitor discrepancy resolution."
        ),
        defStep(
          "study",
          SEL_STUDIES,
          "Study Data & Lock Governance",
          "Monitor data completeness, freeze checkpoints, and export readiness.",
          "Cross-reference form capture with participant status."
        ),
        defStep(
          "ctms",
          SEL_SITE,
          "Site Operational Alignment",
          "Review site recruitment and monitoring progress to prioritize cleaning.",
          "Coordinate site data quality follow-up across trial sites."
        ),
      ],
    },
    trial_coordinator: {
      tasks: [
        defTaskCopy(
          "Confirm Site Startup & Operational Readiness",
          "Verify site activation milestones, regulatory packets, and supply readiness.",
          ["ssu", "startup", "readiness", "coordinator"]
        ),
        defTaskCopy(
          "Coordinate Participant Screening & Enrollment",
          "Verify informed consent status and register eligible site participants.",
          ["participant", "screen", "enroll", "consent"]
        ),
        defTaskCopy(
          "Schedule Visits & Capture Casebook Data",
          "Coordinate visit windows, enter clinical assessments, and resolve queries.",
          ["ecrf", "visit", "casebook", "query"]
        ),
        defTaskCopy(
          "Align Site Operations & Supply Milestones",
          "Track site operational metrics, visit follow-ups, and kit availability.",
          ["ctms", "site", "supply", "operations"]
        ),
      ],
      steps: [
        defStep(
          "ecrf",
          null,
          "Welcome to Trial Coordination",
          "Coordinate participant screening, visit schedules, casebooks, and site operations.",
          "Welcome, Trial Coordinator! Manage participant visits, site readiness, and operational follow-up."
        ),
        defStep(
          "ecrf",
          SEL_PARTICIPANT,
          "Participant Screening & Visit Context",
          "Select an enrolled participant and scheduled visit encounter.",
          "Verify consent state and open scheduled visit assessments."
        ),
        defStep(
          "ecrf",
          SEL_FORM,
          "Casebook Entry & Query Follow-Up",
          "Capture clinical observations with edit checks and Part 11 change reasons.",
          "Enter verified visit observations and answer discrepancy queries."
        ),
        defStep(
          "ssu",
          SEL_SITE,
          "Site Startup & Operational Alignment",
          "Track site activation milestones, operational readiness, and supply status.",
          "Maintain site startup readiness and operational milestones."
        ),
      ],
    },
    site_crc: {
      tasks: [
        defTaskCopy(
          "Confirm Site Activation & Startup Readiness",
          "Verify regulatory packets, delegation, and activation greenlight.",
          ["ssu", "startup", "activation", "readiness"]
        ),
        defTaskCopy(
          "Select or Enroll Study Participants",
          "Verify consent status and select a participant for visit entry.",
          ["participant", "subject", "enroll", "consent"]
        ),
        defTaskCopy(
          "Record Visit Casebook & Answer Queries",
          "Enter observations with Part 11 change reasons and resolve queries.",
          ["ecrf", "visit", "form", "query"]
        ),
      ],
      steps: [
        defStep(
          "ecrf",
          null,
          "Welcome to Data Collection",
          "Capture participant visit data, track consent, and answer site queries.",
          "Welcome, Site Coordinator! Manage participant visits, casebooks, and site readiness."
        ),
        defStep(
          "ecrf",
          SEL_PARTICIPANT,
          "Participant & Visit Context",
          "Select an enrolled participant and scheduled visit before opening a form.",
          "Select participants and navigate scheduled visit encounters."
        ),
        defStep(
          "ecrf",
          SEL_FORM,
          "Clinical Casebook Entry",
          "Enter observations with scoped draft protection, edit checks, and Part 11 reasons.",
          "Record observations with range checks and discrepancy notes."
        ),
        defStep(
          "ssu",
          SEL_SITE,
          "Site Startup & Readiness",
          "Track regulatory prerequisites, site documents, and activation milestones.",
          "Verify site startup milestones to maintain readiness."
        ),
        defStep(
          "training",
          SEL_HELP,
          "Protocol Training & Guidance",
          "Complete assigned protocol training and consult site guidance anytime.",
          "Participant data collection and site workflows are ready."
        ),
      ],
    },
    site_investigator: {
      tasks: [
        defTaskCopy(
          "Verify Site Delegation & Activation Readiness",
          "Confirm investigator delegation logs and site greenlight status.",
          ["delegation", "ssu", "activation", "investigator"]
        ),
        defTaskCopy(
          "Review Participant Eligibility & Casebooks",
          "Inspect participant timelines, observations, and resolved queries.",
          ["participant", "eligibility", "oversight", "casebook"]
        ),
        defTaskCopy(
          "Sign Completed Casebooks (21 CFR Part 11)",
          "Attest to clinical accuracy using step-up electronic signatures.",
          ["sign", "signature", "attest", "part 11"]
        ),
        defTaskCopy(
          "Complete Investigator Protocol Training",
          "Attest to protocol amendments and safety reporting procedures.",
          ["training", "amendment", "attestation"]
        ),
      ],
      steps: [
        defStep(
          "ecrf",
          null,
          "Welcome to Investigator Oversight",
          "Exercise Principal Investigator medical oversight across eligibility, casebooks, and Part 11 sign-off.",
          "Welcome, Principal Investigator! Review participant casebooks, safety observations, and sign-off."
        ),
        defStep(
          "ecrf",
          SEL_PARTICIPANT,
          "Site Participant Cohort",
          "Select a site participant to review visit progress, consent, and assessments.",
          "Inspect participant eligibility, consent state, and visits."
        ),
        defStep(
          "ecrf",
          SEL_FORM,
          "Casebook Review & Electronic Signature",
          "Inspect observations and apply binding 21 CFR Part 11 investigator signatures.",
          "Attest to verified forms with step-up signature authentication."
        ),
        defStep(
          "ssu",
          SEL_SITE,
          "Delegation & Site Readiness",
          "Verify startup documentation, delegation logs, and training compliance.",
          "Confirm investigator delegation logs and site readiness."
        ),
      ],
    },
    unblinded_statistician: {
      tasks: [
        defTaskCopy(
          "Verify Randomization & Allocation Lists",
          "Inspect stratification factors, block balance, and randomization readiness.",
          ["randomization", "allocation", "strata", "ctms"]
        ),
        defTaskCopy(
          "Confirm Site Supply & Activation Thresholds",
          "Review site supply buffer thresholds and resupply readiness.",
          ["ssu", "supply", "depot", "threshold"]
        ),
        defTaskCopy(
          "Review Study Arms & Analysis Populations",
          "Cross-check protocol arms, cohorts, and statistical design parameters.",
          ["study", "arms", "cohort", "biostatistics"]
        ),
      ],
      steps: [
        defStep(
          "ctms",
          null,
          "Welcome to Randomization & Biostatistics",
          "Oversee randomization schedules, treatment allocation balance, and unblinded supply parameters.",
          "Welcome, Unblinded Statistician! Review randomization parameters and site supply readiness."
        ),
        defStep(
          "ctms",
          SEL_SITE,
          "Allocation & Supply Oversight",
          "Inspect randomization readiness and site resupply status across active sites.",
          "Verify stratification balance and allocation schedules."
        ),
        defStep(
          "study",
          SEL_STUDIES,
          "Protocol Arms & Study Conduct",
          "Review study arms, epochs, and cohort progression on the Clinical Canvas.",
          "Confirm alignment between protocol design and randomization lists."
        ),
        defStep(
          "overview",
          SEL_HELP,
          "Biostatistics & Unblinding SOP Guidance",
          "Consult controlled unblinding and randomization governance procedures anytime.",
          "Your biostatistics and randomization workspace is ready."
        ),
      ],
    },
    pharmacist: {
      tasks: [
        defTaskCopy(
          "Verify Site Pharmacy & Supply Readiness",
          "Confirm pharmacy qualification, temperature logs, and receipt readiness.",
          ["ssu", "pharmacy", "supply", "temperature"]
        ),
        defTaskCopy(
          "Reconcile Investigational Product Inventory",
          "Inspect kit statuses, expiry windows, quarantine holds, and accountability.",
          ["ctms", "kit", "inventory", "quarantine"]
        ),
        defTaskCopy(
          "Review Dispensing & Study Dosing Schedule",
          "Verify protocol dosing visits and dispensing checkpoints on the Canvas.",
          ["study", "dispense", "dosing", "accountability"]
        ),
      ],
      steps: [
        defStep(
          "ssu",
          null,
          "Welcome to Pharmacy & Investigational Supply",
          "Manage investigational product receipt, kit accountability, and site pharmacy readiness.",
          "Welcome, Site Pharmacist! Verify kit inventory, temperature excursions, and dispensing readiness."
        ),
        defStep(
          "ssu",
          SEL_SITE,
          "Site Pharmacy & Supply Readiness",
          "Confirm site supply shipments, storage qualification, and kit availability.",
          "Track pharmacy readiness and investigational product stock."
        ),
        defStep(
          "study",
          SEL_STUDIES,
          "Dosing & Accountability Schedule",
          "Inspect scheduled dispensing encounters and protocol dosing rules.",
          "Cross-check visit schedules against site kit inventory."
        ),
        defStep(
          "overview",
          SEL_HELP,
          "Pharmacy & Accountability Guidance",
          "Access investigational product handling, quarantine, and accountability SOPs.",
          "Your site pharmacy workspace and SOP guidance are ready."
        ),
      ],
    },
    cra_monitor: {
      tasks: [
        defTaskCopy(
          "Manage Site Monitoring Visits & Letters",
          "Schedule monitoring visits and track site follow-up actions.",
          ["monitoring", "visit", "letter", "site"]
        ),
        defTaskCopy(
          "Verify Site Startup & Regulatory Greenlight",
          "Confirm essential documents and site activation prerequisites.",
          ["ssu", "greenlight", "startup", "regulatory"]
        ),
        defTaskCopy(
          "Perform Source Data Verification & Queries",
          "Verify participant fields against source records and issue queries.",
          ["sdv", "verify", "source", "query"]
        ),
        defTaskCopy(
          "Inspect Trial Master File Completeness",
          "Review site binder artifacts, expirations, and inspection readiness.",
          ["binder", "artifact", "completeness", "document"]
        ),
      ],
      steps: [
        defStep(
          "ctms",
          null,
          "Welcome to Clinical Operations",
          "Coordinate site activation, monitoring visits, source verification, and trial master file quality.",
          "Welcome, Clinical Monitor! Track site readiness, monitoring visits, and source verification."
        ),
        defStep(
          "ctms",
          SEL_SITE,
          "Site Monitoring & Milestones",
          "Track visit schedules, recruitment velocity, and operational actions.",
          "Manage monitoring schedules, visit letters, and follow-up items."
        ),
        defStep(
          "study",
          SEL_STUDIES,
          "Study Conduct Overview",
          "Inspect cross-site enrollment, adherence, and visit completion.",
          "Monitor study-wide participant progress and site health."
        ),
        defStep(
          "ecrf",
          SEL_FORM,
          "Source Data Verification",
          "Compare casebook items with source records and manage queries.",
          "Verify source observations and track discrepancy queries."
        ),
        defStep(
          "etmf",
          SEL_HELP,
          "Trial Master File & Monitoring Guidance",
          "Verify regulatory artifacts and open contextual monitoring guidance.",
          "Your clinical monitoring workspace and SOP guidance are ready."
        ),
      ],
    },
    safety_officer: {
      tasks: [
        defTaskCopy(
          "Triage Serious Adverse Events & Safety Signals",
          "Evaluate expedited safety reports, causality, and signal thresholds.",
          ["safety", "sae", "signal", "pharmacovigilance"]
        ),
        defTaskCopy(
          "Review Cohort Safety Narratives & Conduct",
          "Inspect participant timelines, medications, and safety observations.",
          ["narrative", "cohort", "adverse", "study"]
        ),
        defTaskCopy(
          "Verify Safety Governance & Protocol Training",
          "Confirm expedited safety reporting and medical review training.",
          ["training", "governance", "curriculum"]
        ),
        defTaskCopy(
          "Inspect Safety Trial Master File Artifacts",
          "Verify safety management plans, periodic reports, and notifications.",
          ["safety", "artifact", "dsur", "report"]
        ),
      ],
      steps: [
        defStep(
          "safety",
          null,
          "Welcome to Pharmacovigilance & Safety",
          "Conduct medical safety review, evaluate serious adverse event narratives, and monitor signals.",
          "Welcome, Safety Reviewer! Triage serious adverse events and evaluate cohort safety signals."
        ),
        defStep(
          "study",
          SEL_STUDIES,
          "Cohort Safety & Clinical Timelines",
          "Inspect clinical observations, safety endpoints, and conduct on the Canvas.",
          "Review participant timelines and concomitant observations."
        ),
        defStep(
          "training",
          SEL_CHECKLIST,
          "Safety Readiness & Training",
          "Track pharmacovigilance milestones and safety training attestations.",
          "Verify safety reporting training and workspace milestones."
        ),
        defStep(
          "overview",
          SEL_HELP,
          "Medical Review & ICH E2B Guidance",
          "Access pharmacovigilance SOPs and safety reporting references in context.",
          "Your pharmacovigilance workspace and safety guidance are ready."
        ),
      ],
    },
    auditor: {
      tasks: [
        defTaskCopy(
          "Inspect Immutable 21 CFR Part 11 Audit Trail",
          "Verify actor attributions, timestamps, and change reasons.",
          ["audit", "trail", "part 11", "attribution"]
        ),
        defTaskCopy(
          "Verify Electronic Signature Manifests",
          "Confirm cryptographic bindings and signature meanings on records.",
          ["signature", "manifest", "attestation"]
        ),
        defTaskCopy(
          "Audit Trial Master File Inspection Readiness",
          "Examine certified copies, provenance, and binder completeness.",
          ["inspection", "binder", "certified", "provenance"]
        ),
        defTaskCopy(
          "Validate Submission & Archival Snapshots",
          "Review read-only archival exports and regulatory submissions.",
          ["archive", "submission", "export", "snapshot"]
        ),
      ],
      steps: [
        defStep(
          "study",
          null,
          "Welcome to Regulatory & Audit",
          "Perform read-only GxP and Part 11 inspections across records and binders.",
          "Welcome, Auditor! Inspect immutable activity streams, signatures, and certified records."
        ),
        defStep(
          "study",
          SEL_STUDIES,
          "Read-Only Study & Archive Inspection",
          "Inspect protocol versions, signature logs, and submissions on the Canvas.",
          "Verify chronological activity and protocol version provenance."
        ),
        defStep(
          "etmf",
          SEL_CHECKLIST,
          "Trial Master File & Audit Checklist",
          "Verify certified regulatory artifacts and track inspection tasks.",
          "Track inspection milestones in the orientation checklist."
        ),
        defStep(
          "overview",
          SEL_HELP,
          "21 CFR Part 11 & ICH E6 Guidance",
          "Reference compliance documentation and audit trail specs from any screen.",
          "Your inspection workspace and compliance guidance are ready."
        ),
      ],
    },
  };

  return freezeDeep(
    PERSONA_SUMMARY_CATALOG.map((summary) => {
      const { taskSummaries, ...base } = summary;
      const details = detailsByKey[summary.key];
      return {
        ...base,
        tasks: taskSummaries.map((taskSummary, idx) => ({
          ...taskSummary,
          ...details.tasks[idx],
        })),
        tourSteps: details.steps.map((entry, idx) => ({
          step: idx + 1,
          ...entry,
        })),
      };
    })
  );
}

/**
 * Canonical catalog of all eleven Cadence Clinical workspace personas (`UX-6`).
 */
export const PERSONA_CATALOG = /* @__PURE__ */ buildPersonaCatalog();

function buildPersonaCatalogByKey() {
  return freezeDeep(
    Object.fromEntries(PERSONA_CATALOG.map((entry) => [entry.key, entry]))
  );
}

export const PERSONA_CATALOG_BY_KEY =
  /* @__PURE__ */ buildPersonaCatalogByKey();

/**
 * Resolves a persona definition from `PERSONA_CATALOG` by key/id, or `null` if unknown.
 *
 * @param {string | null | undefined} personaKey
 */
export function getPersonaCatalogEntry(personaKey) {
  if (!personaKey || typeof personaKey !== "string") {
    return null;
  }
  return PERSONA_CATALOG_BY_KEY[personaKey] || null;
}
