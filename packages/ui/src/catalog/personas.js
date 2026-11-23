function freezeDeep(value) {
  if (!value || typeof value !== "object" || Object.isFrozen(value)) {
    return value;
  }
  if (Array.isArray(value)) {
    for (const item of value) {
      freezeDeep(item);
    }
    return Object.freeze(value);
  }
  for (const key of Object.keys(value)) {
    freezeDeep(value[key]);
  }
  return Object.freeze(value);
}

const ROUTE_PATHS = {
  study: "/studies",
  ecrf: "/ecrf",
  ctms: "/ctms",
  ssu: "/ssu",
  etmf: "/etmf",
  training: "/training",
  safety: "/safety",
  audit: "/audit",
  overview: "/overview",
};

const defTask = (id, routeName, title, description, keywords) => ({
  id,
  title,
  description,
  routeName,
  route: ROUTE_PATHS[routeName] || `/${routeName}`,
  keywords,
});

const defStep = (routeName, targetSelector, title, description, prompt) => ({
  routeName,
  targetSelector,
  title,
  description,
  prompt,
});

const defPersona = (
  key,
  label,
  badge,
  role,
  roleName,
  title,
  description,
  requires,
  landingRoute,
  tasks,
  steps
) => ({
  id: key,
  key,
  label,
  badge,
  role,
  roleName,
  title,
  description,
  requires,
  landingRoute,
  tasks,
  tourSteps: steps.map((entry, idx) => ({ step: idx + 1, ...entry })),
});

/**
 * Canonical catalog of all eight Cadence Clinical workspace personas (`UX-6`).
 */
export const PERSONA_CATALOG = freezeDeep([
  defPersona(
    "super_admin",
    "Super Admin (All Access)",
    "Admin",
    "sponsor_admin",
    "Super Admin",
    "Super Admin & Governance Workspace",
    "Oversee multi-study governance, protocol versions, site operations, data quality, and 21 CFR Part 11 compliance.",
    ["sponsor_admin"],
    "mdr",
    [
      defTask(
        "task_protocol",
        "study",
        "Author Protocol & Endpoints",
        "Define study design, arms, and eligibility criteria in the protocol model.",
        ["study", "studies", "protocol", "welcome", "endpoints"]
      ),
      defTask(
        "task_soa",
        "study",
        "Configure Schedule of Activities",
        "Map study visits, cohorts, and assessment windows.",
        ["soa", "schedule", "activities", "visits"]
      ),
      defTask(
        "task_crf",
        "ecrf",
        "Design Casebook Forms",
        "Configure clinical casebook forms and field validation constraints.",
        ["ecrf", "crf", "canvas", "form"]
      ),
      defTask(
        "task_rules",
        "ctms",
        "Verify Site Readiness & Monitoring",
        "Review site activation milestones, monitoring schedules, and allocations.",
        ["ctms", "monitoring", "site", "allocation"]
      ),
      defTask(
        "task_exports",
        "study",
        "Review Regulatory & Archival Readiness",
        "Inspect audit readiness, electronic signature logs, and submission workflows.",
        ["export", "odm", "audit", "submission", "archive"]
      ),
    ],
    [
      defStep(
        "study",
        null,
        "Welcome to Cadence Clinical",
        "Oversee protocol design, clinical conduct, site operations, safety review, and 21 CFR Part 11 governance.",
        "Welcome, Administrator! Oversee studies, data collection, site readiness, and governance."
      ),
      defStep(
        "study",
        "#nav-studies",
        "Studies & Clinical Canvas",
        "Open any trial on the Clinical Canvas to inspect protocol versions, visit schedules, and milestone locks.",
        "Inspect participant counts, protocol versions, and governance proposals per study."
      ),
      defStep(
        "ecrf",
        "#nav-data-collection",
        "Data Collection & Casebooks",
        "Review participant enrollment, clinical form instances, source verification, and query workflows.",
        "Verify structured clinical data capture and real-time validation checks."
      ),
      defStep(
        "ctms",
        "#nav-site-readiness",
        "Site Operations & Startup",
        "Monitor site activation readiness, monitoring visit reports, and recruitment milestones.",
        "Oversee site readiness and activation workflows across clinical sites."
      ),
      defStep(
        "overview",
        "#btn-contextual-help-trigger",
        "Contextual Guidance & Support",
        "Open role-tailored standard operating guidance and workspace checklists from any screen.",
        "Your unified clinical workspace is ready."
      ),
    ]
  ),
  defPersona(
    "sponsor_designer",
    "Sponsor Protocol Designer",
    "Designer",
    "sponsor_designer",
    "Sponsor Designer",
    "Sponsor Protocol & Design Workspace",
    "Author study protocols, configure Schedules of Activities, and inspect recorded protocol versions and collection definitions.",
    ["sponsor_designer", "protocol_architect"],
    "mdr",
    [
      defTask(
        "sd_protocol",
        "study",
        "Inspect Study Protocol Structure",
        "Review study arms, epochs, objectives, and protocol versions on the Clinical Canvas.",
        ["protocol", "usdm", "arms", "design"]
      ),
      defTask(
        "sd_soa",
        "study",
        "Verify Schedule of Activities Matrix",
        "Confirm planned visits, assessment windows, and linked collection specifications.",
        ["soa", "schedule", "visits", "matrix"]
      ),
      defTask(
        "sd_eligibility",
        "study",
        "Review Inclusion & Exclusion Criteria",
        "Validate structured eligibility criteria and controlled terminology bindings.",
        ["eligibility", "inclusion", "exclusion", "terminology"]
      ),
    ],
    [
      defStep(
        "study",
        null,
        "Welcome to Protocol Design",
        "Author structured trial designs, visit schedules, and standardized collection forms from one workspace.",
        "Welcome, Designer! Navigate study lifecycle stages and inspect protocol versions."
      ),
      defStep(
        "study",
        "#nav-studies",
        "Structured Study Design",
        "Navigate between protocol versions, study arms, planned encounters, and collection specifications.",
        "Structure and review study lifecycle sections from one unified workspace."
      ),
      defStep(
        "overview",
        "#btn-onboarding-checklist-trigger",
        "Design Readiness Checklist",
        "Track protocol authoring, visit schedule verification, and terminology review milestones.",
        "Track setup progress and manage orientation preferences from the header."
      ),
      defStep(
        "overview",
        "#btn-contextual-help-trigger",
        "Protocol Authoring Guidance",
        "Reference CDISC USDM and CDASH design standards alongside your active workspace.",
        "Use Studies, Search, Orientation, and Help from the persistent top bar."
      ),
    ]
  ),
  defPersona(
    "data_manager",
    "Clinical Data Manager",
    "DM",
    "data_manager",
    "Data Manager",
    "Clinical Data Management Workspace",
    "Validate participant casebooks, triage clinical queries, review edit check rules, and prepare clean datasets for lock.",
    ["data_manager"],
    "rules",
    [
      defTask(
        "task_crf",
        "ecrf",
        "Review Casebook Data & Edit Checks",
        "Inspect recorded form values, range validations, and pre-commit edit check warnings.",
        ["ecrf", "crf", "canvas", "form", "validation"]
      ),
      defTask(
        "dm_queries",
        "ecrf",
        "Triage & Close Clinical Queries",
        "Evaluate site query responses, verify discrepancy resolutions, and close answered queries.",
        ["query", "discrepancy", "triage"]
      ),
      defTask(
        "dm_locks",
        "study",
        "Verify Milestone Freeze & Lock Readiness",
        "Check open query counts and signature completeness ahead of database freeze or lock.",
        ["lock", "freeze", "dataset", "clean"]
      ),
      defTask(
        "dm_oversight",
        "ctms",
        "Coordinate Site Data Quality Follow-Up",
        "Align with clinical monitors on site-level query aging and verification throughput.",
        ["site", "oversight", "monitoring"]
      ),
    ],
    [
      defStep(
        "ecrf",
        null,
        "Welcome to Data Management",
        "Oversee clinical data cleanliness, discrepancy management, edit checks, and milestone lock readiness.",
        "Welcome, Data Manager! Inspect form validations, queries, and participant progress."
      ),
      defStep(
        "ecrf",
        "#ecrf-form-instance",
        "Casebook Discrepancy & Query Review",
        "Inspect item revisions, automated edit check findings, and audit-trailed query threads.",
        "Validate scheduled visit forms and monitor discrepancy resolution."
      ),
      defStep(
        "study",
        "#nav-studies",
        "Study Data & Lock Governance",
        "Monitor cohort data completeness, freeze checkpoints, and export readiness on the Clinical Canvas.",
        "Cross-reference form capture with live participant status in the study workspace."
      ),
      defStep(
        "ctms",
        "#nav-site-readiness",
        "Site Operational Alignment",
        "Review site recruitment and monitoring visit progress to prioritize data cleaning.",
        "Coordinate site data quality follow-up across active trial sites."
      ),
    ]
  ),
  defPersona(
    "site_crc",
    "Site Coordinator / CRC",
    "Site",
    "crc",
    "Clinical Research Coordinator",
    "Site Coordinator & Data Capture Workspace",
    "Manage site startup milestones, screen and enroll trial participants, record visit casebooks, and resolve clinical queries.",
    ["site_investigator", "crc", "trial_coordinator"],
    "ecrf",
    [
      defTask(
        "task_rules",
        "ssu",
        "Confirm Site Activation & Startup Readiness",
        "Verify regulatory packet status, delegation readiness, and site activation greenlight.",
        ["ssu", "startup", "activation", "readiness"]
      ),
      defTask(
        "crc_enroll",
        "ecrf",
        "Select or Enroll Study Participants",
        "Verify informed consent status and select an active participant for visit entry.",
        ["participant", "subject", "enroll", "consent"]
      ),
      defTask(
        "crc_capture",
        "ecrf",
        "Record Visit Casebook & Answer Queries",
        "Enter source-verified observations with 21 CFR Part 11 change reasons and resolve queries.",
        ["ecrf", "visit", "form", "query"]
      ),
    ],
    [
      defStep(
        "ecrf",
        null,
        "Welcome to Data Collection",
        "Capture participant visit data, track informed consent, and respond to clinical queries at your site.",
        "Welcome, Site Coordinator! Manage participant visits, casebooks, and site readiness."
      ),
      defStep(
        "ecrf",
        "#ecrf-participant-bar",
        "Participant & Visit Context",
        "Select an enrolled participant and scheduled protocol visit before opening or updating a casebook.",
        "Select participants and navigate scheduled visit encounters for accurate capture."
      ),
      defStep(
        "ecrf",
        "#ecrf-form-instance",
        "Clinical Casebook Entry",
        "Enter observations with account-scoped draft protection, inline edit checks, and 21 CFR Part 11 audit reasons.",
        "Record clinical observations with immediate range checks and discrepancy notes."
      ),
      defStep(
        "ssu",
        "#nav-site-readiness",
        "Site Startup & Readiness",
        "Track regulatory greenlight prerequisites, essential site documents, and activation milestones.",
        "Verify site startup milestones to maintain clinical site readiness."
      ),
      defStep(
        "training",
        "#btn-contextual-help-trigger",
        "Protocol Training & Guidance",
        "Complete assigned protocol training modules and consult contextual site guidance anytime.",
        "Participant data collection and site readiness workflows are ready."
      ),
    ]
  ),
  defPersona(
    "site_investigator",
    "Principal Investigator",
    "PI",
    "principal_investigator",
    "Principal Investigator",
    "Principal Investigator Oversight Workspace",
    "Provide medical oversight for site participants, verify delegation readiness, review safety observations, and sign completed casebooks.",
    ["principal_investigator"],
    "ecrf",
    [
      defTask(
        "pi_readiness",
        "ssu",
        "Verify Site Delegation & Activation Readiness",
        "Confirm investigator responsibilities, delegation logs, and site greenlight status.",
        ["delegation", "ssu", "activation", "investigator"]
      ),
      defTask(
        "pi_oversight",
        "ecrf",
        "Review Participant Eligibility & Casebooks",
        "Inspect enrolled participant timelines, clinical observations, and resolved site queries.",
        ["participant", "eligibility", "oversight", "casebook"]
      ),
      defTask(
        "pi_signoff",
        "ecrf",
        "Sign Completed Casebooks (21 CFR Part 11)",
        "Attest to clinical accuracy on verified forms using step-up electronic signature authentication.",
        ["sign", "signature", "attest", "part 11"]
      ),
      defTask(
        "pi_training",
        "training",
        "Complete Investigator Protocol Training",
        "Attest to current protocol amendments and safety reporting procedures.",
        ["training", "amendment", "attestation"]
      ),
    ],
    [
      defStep(
        "ecrf",
        null,
        "Welcome to Investigator Oversight",
        "Exercise Principal Investigator medical oversight across participant eligibility, casebooks, delegation readiness, and Part 11 sign-off.",
        "Welcome, Principal Investigator! Review participant casebooks, safety observations, and electronic sign-off."
      ),
      defStep(
        "ecrf",
        "#ecrf-participant-bar",
        "Site Participant Cohort",
        "Select any site participant to review visit progress, consent status, and clinical assessments.",
        "Inspect site participant eligibility, consent state, and scheduled visits."
      ),
      defStep(
        "ecrf",
        "#ecrf-form-instance",
        "Casebook Review & Electronic Signature",
        "Inspect recorded observations and apply legally binding 21 CFR Part 11 investigator signatures.",
        "Attest to verified clinical forms using step-up electronic signature authentication."
      ),
      defStep(
        "ssu",
        "#nav-site-readiness",
        "Delegation & Site Readiness",
        "Verify site startup documentation, staff delegation logs, and protocol training compliance.",
        "Confirm investigator delegation logs and site operational readiness."
      ),
    ]
  ),
  defPersona(
    "cra_monitor",
    "CRA Clinical Monitor",
    "Monitor",
    "cra",
    "Clinical Research Associate",
    "Clinical Monitoring & Site Oversight Workspace",
    "Track site startup greenlight, conduct monitoring visits, perform source data verification, and inspect trial master file completeness.",
    ["cra", "monitor"],
    "ctms",
    [
      defTask(
        "cra_visits",
        "ctms",
        "Manage Site Monitoring Visits & Letters",
        "Schedule qualification, initiation, and interim monitoring visits and track follow-up actions.",
        ["monitoring", "visit", "letter", "site"]
      ),
      defTask(
        "cra_ssu",
        "ssu",
        "Verify Site Startup & Regulatory Greenlight",
        "Confirm essential regulatory documents and site activation prerequisites across assigned sites.",
        ["ssu", "greenlight", "startup", "regulatory"]
      ),
      defTask(
        "cra_sdv",
        "ecrf",
        "Perform Source Data Verification & Queries",
        "Verify recorded participant fields against source records and issue targeted clinical queries.",
        ["sdv", "verify", "source", "query"]
      ),
      defTask(
        "cra_master_file",
        "etmf",
        "Inspect Trial Master File Completeness",
        "Review site binder artifacts, expiration alerts, and inspection readiness across zones.",
        ["binder", "artifact", "completeness", "document"]
      ),
    ],
    [
      defStep(
        "ctms",
        null,
        "Welcome to Clinical Operations",
        "Coordinate site activation, monitoring visits, source data verification, and trial master file quality.",
        "Welcome, Clinical Monitor! Track site readiness, monitoring visits, and source verification."
      ),
      defStep(
        "ctms",
        "#nav-site-readiness",
        "Site Monitoring & Milestones",
        "Track monitoring visit schedules, recruitment velocity, and site operational action items.",
        "Manage monitoring schedules, visit letters, and site follow-up items."
      ),
      defStep(
        "study",
        "#nav-studies",
        "Study Conduct Overview",
        "Inspect cross-site enrollment, protocol adherence, and visit completion on the Clinical Canvas.",
        "Monitor study-wide participant progress and site activation health."
      ),
      defStep(
        "ecrf",
        "#ecrf-form-instance",
        "Source Data Verification",
        "Compare recorded casebook items with source documentation and raise or close monitoring queries.",
        "Verify source observations at the item level and track discrepancy queries."
      ),
      defStep(
        "etmf",
        "#btn-contextual-help-trigger",
        "Trial Master File & Monitoring Guidance",
        "Verify essential regulatory artifacts and open contextual monitoring guidance from any view.",
        "Your clinical monitoring workspace and standard operating guidance are ready."
      ),
    ]
  ),
  defPersona(
    "safety_officer",
    "Pharmacovigilance & Safety",
    "Safety",
    "safety_reviewer",
    "Safety Officer",
    "Pharmacovigilance & Medical Safety Workspace",
    "Monitor serious adverse events, evaluate emerging safety signals, review medical narratives, and track pharmacovigilance governance.",
    [
      "safety_reviewer",
      "safety_officer",
      "safety_physician",
      "medical_monitor",
      "sponsor_mm",
    ],
    "safety",
    [
      defTask(
        "pv_signals",
        "safety",
        "Triage Serious Adverse Events & Safety Signals",
        "Evaluate expedited safety reports, causality assessments, and aggregate signal thresholds.",
        ["safety", "sae", "signal", "pharmacovigilance"]
      ),
      defTask(
        "pv_narratives",
        "study",
        "Review Cohort Safety Narratives & Conduct",
        "Inspect participant clinical timelines, concomitant medications, and safety observations.",
        ["narrative", "cohort", "adverse", "study"]
      ),
      defTask(
        "pv_training",
        "training",
        "Verify Safety Governance & Protocol Training",
        "Confirm completion of expedited safety reporting and medical review training curricula.",
        ["training", "governance", "curriculum"]
      ),
      defTask(
        "pv_master_file",
        "etmf",
        "Inspect Safety Trial Master File Artifacts",
        "Verify safety management plans, periodic safety reports, and regulatory notifications.",
        ["safety", "artifact", "dsur", "report"]
      ),
    ],
    [
      defStep(
        "safety",
        null,
        "Welcome to Pharmacovigilance & Safety",
        "Conduct medical safety review, evaluate serious adverse event narratives, and monitor signal trends.",
        "Welcome, Safety Reviewer! Triage serious adverse events and evaluate cohort safety signals."
      ),
      defStep(
        "study",
        "#nav-studies",
        "Cohort Safety & Clinical Timelines",
        "Inspect participant clinical observations, safety endpoints, and protocol conduct on the Clinical Canvas.",
        "Review participant clinical timelines and concomitant observations per study."
      ),
      defStep(
        "training",
        "#btn-onboarding-checklist-trigger",
        "Safety Readiness & Training",
        "Track pharmacovigilance orientation milestones and protocol safety training attestations.",
        "Verify expedited safety reporting training and workspace milestones."
      ),
      defStep(
        "overview",
        "#btn-contextual-help-trigger",
        "Medical Review & ICH E2B Guidance",
        "Access pharmacovigilance standard operating procedures and safety reporting references in context.",
        "Your pharmacovigilance review workspace and medical safety guidance are ready."
      ),
    ]
  ),
  defPersona(
    "auditor",
    "GxP & Part 11 Auditor",
    "Auditor",
    "auditor",
    "QA / Regulatory Auditor",
    "Regulatory & Quality Assurance Audit Workspace",
    "Conduct read-only GxP inspections across immutable 21 CFR Part 11 audit trails, signature manifests, trial master file binders, and archives.",
    ["auditor", "tmf_auditor"],
    "audit",
    [
      defTask(
        "aud_trail",
        "audit",
        "Inspect Immutable 21 CFR Part 11 Audit Trail",
        "Verify chronological actor attributions, timestamps, and change reasons across clinical records.",
        ["audit", "trail", "part 11", "attribution"]
      ),
      defTask(
        "aud_signatures",
        "study",
        "Verify Electronic Signature Manifests",
        "Confirm cryptographic content bindings and step-up signature meanings on study records.",
        ["signature", "manifest", "attestation"]
      ),
      defTask(
        "aud_master_file",
        "etmf",
        "Audit Trial Master File Inspection Readiness",
        "Examine certified copies, artifact provenance, and binder completeness across regulatory zones.",
        ["inspection", "binder", "certified", "provenance"]
      ),
      defTask(
        "aud_exports",
        "study",
        "Validate Submission & Archival Snapshots",
        "Review read-only archival exports and regulatory submission packages on the Clinical Canvas.",
        ["archive", "submission", "export", "snapshot"]
      ),
    ],
    [
      defStep(
        "study",
        null,
        "Welcome to Regulatory & Audit",
        "Perform read-only GxP and 21 CFR Part 11 inspections across study records, signature manifests, and binders.",
        "Welcome, Auditor! Inspect immutable activity streams, signature manifests, and certified records."
      ),
      defStep(
        "study",
        "#nav-studies",
        "Read-Only Study & Archive Inspection",
        "Inspect recorded protocol versions, electronic signature logs, and submission packages on the Clinical Canvas.",
        "Verify chronological clinical activity and protocol version provenance."
      ),
      defStep(
        "etmf",
        "#btn-onboarding-checklist-trigger",
        "Trial Master File & Audit Checklist",
        "Verify certified regulatory artifacts and track inspection readiness tasks.",
        "Keep track of inspection milestones in the opt-in orientation checklist."
      ),
      defStep(
        "overview",
        "#btn-contextual-help-trigger",
        "21 CFR Part 11 & ICH E6 Guidance",
        "Reference regulatory compliance documentation and audit trail specifications from any screen.",
        "Your inspection workspace and contextual compliance guidance are ready."
      ),
    ]
  ),
]);

export const PERSONA_CATALOG_BY_KEY = freezeDeep(
  Object.fromEntries(PERSONA_CATALOG.map((entry) => [entry.key, entry]))
);

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
