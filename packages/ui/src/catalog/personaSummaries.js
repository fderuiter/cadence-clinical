export function freezeDeep(value) {
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

const defSummary = (
  key,
  label,
  badge,
  role,
  roleName,
  title,
  description,
  requires,
  landingRoute,
  taskPairs
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
  taskSummaries: taskPairs.map(([id, routeName]) => ({
    id,
    routeName,
    route: routeName === "study" ? "/studies" : `/${routeName}`,
  })),
});

function buildPersonaSummaryCatalog() {
  return freezeDeep([
    defSummary(
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
        ["task_protocol", "study"],
        ["task_soa", "study"],
        ["task_crf", "ecrf"],
        ["task_rules", "ctms"],
        ["task_exports", "study"],
      ]
    ),
    defSummary(
      "sponsor_designer",
      "Sponsor Protocol Designer",
      "Designer",
      "sponsor_designer",
      "Sponsor Designer",
      "Sponsor Protocol & Design Workspace",
      "Author study protocols, configure Schedules of Activities, and inspect recorded protocol versions.",
      ["sponsor_designer", "protocol_architect"],
      "mdr",
      [
        ["sd_protocol", "study"],
        ["sd_soa", "study"],
        ["sd_eligibility", "study"],
      ]
    ),
    defSummary(
      "data_manager",
      "Clinical Data Manager",
      "DM",
      "data_manager",
      "Data Manager",
      "Clinical Data Management Workspace",
      "Validate participant casebooks, triage clinical queries, review edit checks, and prepare clean datasets for lock.",
      ["data_manager"],
      "rules",
      [
        ["task_crf", "ecrf"],
        ["dm_queries", "ecrf"],
        ["dm_locks", "study"],
        ["dm_oversight", "ctms"],
      ]
    ),
    defSummary(
      "trial_coordinator",
      "Trial Coordinator",
      "Coord",
      "trial_coordinator",
      "Trial Coordinator",
      "Trial Coordinator & Site Operations Workspace",
      "Coordinate participant screening, site startup readiness, visit scheduling, and casebook follow-up.",
      ["trial_coordinator"],
      "ecrf",
      [
        ["tc_readiness", "ssu"],
        ["tc_enroll", "ecrf"],
        ["tc_capture", "ecrf"],
        ["tc_oversight", "ctms"],
      ]
    ),
    defSummary(
      "site_crc",
      "Site Coordinator / CRC",
      "Site",
      "crc",
      "Clinical Research Coordinator",
      "Site Coordinator & Data Capture Workspace",
      "Manage site startup milestones, enroll trial participants, record visit casebooks, and resolve clinical queries.",
      ["site_investigator", "crc"],
      "ecrf",
      [
        ["task_rules", "ssu"],
        ["crc_enroll", "ecrf"],
        ["crc_capture", "ecrf"],
      ]
    ),
    defSummary(
      "site_investigator",
      "Principal Investigator",
      "PI",
      "principal_investigator",
      "Principal Investigator",
      "Principal Investigator Oversight Workspace",
      "Provide medical oversight for site participants, verify delegation readiness, review safety observations, and sign casebooks.",
      ["principal_investigator"],
      "ecrf",
      [
        ["pi_readiness", "ssu"],
        ["pi_oversight", "ecrf"],
        ["pi_signoff", "ecrf"],
        ["pi_training", "training"],
      ]
    ),
    defSummary(
      "unblinded_statistician",
      "Unblinded Statistician",
      "Stat",
      "unblinded_statistician",
      "Unblinded Statistician",
      "Randomization & Biostatistics Workspace",
      "Configure randomization schedules, inspect allocation balance, and oversee unblinded statistical supply workflows.",
      ["unblinded_statistician"],
      "ctms",
      [
        ["stat_allocation", "ctms"],
        ["stat_startup", "ssu"],
        ["stat_design", "study"],
      ]
    ),
    defSummary(
      "pharmacist",
      "Site Pharmacist",
      "Pharm",
      "pharmacist",
      "Site Pharmacist",
      "Investigational Product & Pharmacy Workspace",
      "Verify investigational product inventory, confirm site supply readiness, and oversee kit accountability.",
      ["pharmacist"],
      "ssu",
      [
        ["pharm_supply", "ssu"],
        ["pharm_inventory", "ctms"],
        ["pharm_conduct", "study"],
      ]
    ),
    defSummary(
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
        ["cra_visits", "ctms"],
        ["cra_ssu", "ssu"],
        ["cra_sdv", "ecrf"],
        ["cra_master_file", "etmf"],
      ]
    ),
    defSummary(
      "safety_officer",
      "Pharmacovigilance & Safety",
      "Safety",
      "safety_reviewer",
      "Safety Officer",
      "Pharmacovigilance & Medical Safety Workspace",
      "Monitor serious adverse events, evaluate safety signals, review medical narratives, and track pharmacovigilance governance.",
      [
        "safety_reviewer",
        "safety_officer",
        "safety_physician",
        "medical_monitor",
        "sponsor_mm",
      ],
      "safety",
      [
        ["pv_signals", "safety"],
        ["pv_narratives", "study"],
        ["pv_training", "training"],
        ["pv_master_file", "etmf"],
      ]
    ),
    defSummary(
      "auditor",
      "GxP & Part 11 Auditor",
      "Auditor",
      "auditor",
      "QA / Regulatory Auditor",
      "Regulatory & Quality Assurance Audit Workspace",
      "Conduct read-only GxP inspections across 21 CFR Part 11 audit trails, signature manifests, trial master file binders, and archives.",
      ["auditor", "tmf_auditor"],
      "audit",
      [
        ["aud_trail", "audit"],
        ["aud_signatures", "study"],
        ["aud_master_file", "etmf"],
        ["aud_exports", "study"],
      ]
    ),
  ]);
}

/**
 * Lightweight cold-boot persona summary catalog for auth role resolution,
 * shell header switcher options, and passive setup-task badge counts (`UX-6`).
 */
export const PERSONA_SUMMARY_CATALOG =
  /* @__PURE__ */ buildPersonaSummaryCatalog();

function buildPersonaSummaryByKey() {
  return freezeDeep(
    Object.fromEntries(
      PERSONA_SUMMARY_CATALOG.map((entry) => [entry.key, entry])
    )
  );
}

export const PERSONA_SUMMARY_BY_KEY =
  /* @__PURE__ */ buildPersonaSummaryByKey();

/**
 * Resolves a lightweight persona summary from `PERSONA_SUMMARY_CATALOG` by key/id, or `null` if unknown.
 *
 * @param {string | null | undefined} personaKey
 */
export function getPersonaSummaryEntry(personaKey) {
  if (!personaKey || typeof personaKey !== "string") {
    return null;
  }
  return PERSONA_SUMMARY_BY_KEY[personaKey] || null;
}
