/**
 * Shared human-readable clinical label formatters for RBAC roles, workflow statuses,
 * operational enums, document modalities, and AI provenance attributes.
 *
 * Role mappings align with `apps/web/src/stores/auth.js` and
 * `packages/security/src/Rbac/RoleNormalization.cs`.
 */

const ROLE_SLUG_LABELS: Record<string, string> = {
  sysadmin: "System Administrator",
  system_admin: "System Administrator",
  system_administrator: "System Administrator",
  sponsor_admin: "Sponsor Administrator",
  super_admin: "Sponsor Administrator",
  admin: "Sponsor Administrator",
  sponsor_designer: "Protocol Designer",
  study_designer: "Protocol Designer",
  sponsor_study_designer: "Protocol Designer",
  designer: "Protocol Designer",
  sponsor_dm: "Data Manager",
  sponsor_data_manager: "Data Manager",
  data_manager: "Data Manager",
  dm: "Data Manager",
  sponsor_mm: "Medical Monitor",
  sponsor_medical_monitor: "Medical Monitor",
  medical_monitor: "Medical Monitor",
  mm: "Medical Monitor",
  safety_reviewer: "Pharmacovigilance & Safety",
  safety_officer: "Pharmacovigilance & Safety",
  pharmacovigilance: "Pharmacovigilance & Safety",
  sponsor_statistician: "Biostatistician",
  statistician: "Biostatistician",
  biostatistician: "Biostatistician",
  unblinded_statistician: "Unblinded Statistician",
  lead_unblinded_statistician: "Unblinded Statistician",
  idmc: "IDMC Member",
  dsmb: "DSMB Member",
  emergency_unblinder: "Emergency Unblinder",
  principal_investigator: "Principal Investigator",
  pi: "Principal Investigator",
  lead_investigator: "Lead Investigator",
  site_investigator: "Site Investigator",
  investigator: "Site Investigator",
  investigator_user: "Site Investigator",
  sub_investigator: "Sub-Investigator",
  authorized_er_physician: "Authorized ER Physician",
  er_physician: "Authorized ER Physician",
  crc: "Clinical Research Coordinator",
  site_crc: "Clinical Research Coordinator",
  site_coordinator: "Clinical Research Coordinator",
  study_coordinator: "Clinical Research Coordinator",
  coordinator: "Clinical Research Coordinator",
  clinical_research_coordinator: "Clinical Research Coordinator",
  cra: "Clinical Research Associate",
  clinical_research_associate: "Clinical Research Associate",
  cra_monitor: "Clinical Research Associate",
  monitor: "Clinical Monitor",
  external_monitor: "External Clinical Monitor",
  cro_monitor: "External Clinical Monitor",
  protocol_reviewer: "Protocol Reviewer",
  reviewer: "Protocol Reviewer",
  finance_officer: "Finance Officer",
  grants_manager: "Grants Manager",
  pharmacist: "Site Pharmacist",
  site_pharmacist: "Site Pharmacist",
  unblinded_pharmacist: "Unblinded Pharmacist",
  auditor: "Auditor",
  tmf_auditor: "eTMF Auditor",
  inspector: "Regulatory Inspector",
  regulatory_inspector: "Regulatory Inspector",
  regulatory_affairs: "Regulatory Affairs",
  subject: "Participant",
  participant: "Participant",
};

const CLINICAL_ENUM_LABELS: Record<string, string> = {
  TECHNICAL_QC: "Technical QC",
  CLINICAL_QC: "Clinical QC",
  CRA_QC_REQUIRED: "CRA QC Required",
  AUTO_CLASSIFY: "Auto-Classify",
  MANUAL_REVIEW: "Manual Review",
  PENDING_PI_SIGNATURE: "Pending PI Signature",
  SIGNED_OFF: "Signed Off",
  IN_PROGRESS: "In Progress",
  NOT_STARTED: "Not Started",
  ON_HOLD: "On Hold",
  LEDGER_SEALED: "Ledger Sealed",
  PDF_BINARY: "PDF Document",
  STRUCTURED_FORM: "Structured Form",
  TEXT: "Text Document",
  SIV: "Site Initiation (SIV)",
  IMV: "Interim Monitoring (IMV)",
  COV: "Close-Out (COV)",
  FOLLOW_UP_LETTER: "Follow-Up Letter",
  CONFIRMATION_LETTER: "Confirmation Letter",
  SITE_SELECTION: "Site Selection",
  INITIATION_VISIT: "Initiation Visit",
  SITE_ACTIVATION: "Site Activation",
  FIRST_SUBJECT_ENROLLED: "First Participant Enrolled",
  LAST_SUBJECT_LAST_VISIT: "Last Participant Last Visit",
  DB_LOCK: "Database Lock",
  PROCEDURE_DELTA: "Procedure Delta",
  PROTOCOL_OVERVIEW: "Protocol Overview",
  ATTESTATION_QUIZ: "Attestation & Assessment",
  PERMUTED_BLOCK: "Permuted Block",
  STRATIFIED_BLOCK: "Stratified Block",
  MINIMIZATION: "Minimization",
  SIMPLE: "Simple",
};

const PRESERVED_ACRONYMS: Record<string, string> = {
  qc: "QC",
  pi: "PI",
  crc: "CRC",
  cra: "CRA",
  dm: "DM",
  mm: "MM",
  er: "ER",
  cro: "CRO",
  idmc: "IDMC",
  dsmb: "DSMB",
  pdf: "PDF",
  csv: "CSV",
  zip: "ZIP",
  siv: "SIV",
  imv: "IMV",
  cov: "COV",
  crf: "CRF",
  ecrf: "eCRF",
  etmf: "eTMF",
  eisf: "eISF",
  econsent: "eConsent",
  gxp: "GxP",
  sae: "SAE",
  susar: "SUSAR",
  irb: "IRB",
  iec: "IEC",
  fda: "FDA",
  ema: "EMA",
  ich: "ICH",
  cdisc: "CDISC",
  odm: "ODM",
  usdm: "USDM",
  sdtm: "SDTM",
  adam: "ADaM",
  cdash: "CDASH",
  din: "DIN",
  ip: "IP",
  rtsm: "RTSM",
  ctms: "CTMS",
  edc: "EDC",
  sdv: "SDV",
  ai: "AI",
  id: "ID",
  sha256: "SHA-256",
};

function titleCaseTokens(raw: string): string {
  return raw
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .split(/[\s_-]+/)
    .filter(Boolean)
    .map((word) => {
      const lower = word.toLowerCase();
      if (PRESERVED_ACRONYMS[lower]) {
        return PRESERVED_ACRONYMS[lower];
      }
      return lower.charAt(0).toUpperCase() + lower.slice(1);
    })
    .join(" ");
}

function isAlreadyHumanPhrase(trimmed: string): boolean {
  if (!trimmed || trimmed.includes("_")) return false;
  const hasLower = /[a-z]/.test(trimmed);
  const hasUpper = /[A-Z]/.test(trimmed);
  return hasLower && hasUpper;
}

/**
 * Formats an RBAC role slug or alias into a readable clinical role title,
 * preserving already-formatted human role labels verbatim.
 */
export function formatRoleLabel(
  role: string | null | undefined,
  fallback = ""
): string {
  const trimmed = String(role ?? "").trim();
  if (!trimmed) return fallback;

  const slug = trimmed.toLowerCase().replace(/[\s-]+/g, "_");
  if (ROLE_SLUG_LABELS[slug]) {
    if (isAlreadyHumanPhrase(trimmed) && trimmed.includes(" ")) {
      return trimmed;
    }
    return ROLE_SLUG_LABELS[slug];
  }

  if (isAlreadyHumanPhrase(trimmed)) {
    return trimmed;
  }

  return titleCaseTokens(trimmed);
}

/**
 * Formats a collection of role slugs into a comma-separated clinical role list.
 */
export function formatRoleList(
  roles: Iterable<string | null | undefined> | null | undefined,
  fallback = "Assigned clinical user"
): string {
  if (!roles) return fallback;
  const seen = new Set<string>();
  const labels: string[] = [];
  for (const raw of roles) {
    const label = formatRoleLabel(raw);
    if (label && !seen.has(label)) {
      seen.add(label);
      labels.push(label);
    }
  }
  return labels.length > 0 ? labels.join(", ") : fallback;
}

/**
 * Formats a backend workflow status or enum token (`TECHNICAL_QC`, `PENDING_PI_SIGNATURE`,
 * `follow_up_letter`, etc.) into plain clinical copy while preserving regulatory acronyms.
 */
export function formatClinicalEnum(
  value: string | null | undefined,
  fallback = ""
): string {
  const trimmed = String(value ?? "").trim();
  if (!trimmed) return fallback;

  const normalizedKey = trimmed.toUpperCase().replace(/[\s-]+/g, "_");
  if (CLINICAL_ENUM_LABELS[normalizedKey]) {
    return CLINICAL_ENUM_LABELS[normalizedKey];
  }

  if (isAlreadyHumanPhrase(trimmed)) {
    return trimmed;
  }

  return titleCaseTokens(trimmed);
}

/**
 * Alias of `formatClinicalEnum` for workflow state badges and status pills.
 */
export function formatClinicalStatus(
  value: string | null | undefined,
  fallback = ""
): string {
  return formatClinicalEnum(value, fallback);
}

/**
 * Formats document storage/render modality codes (`PDF_BINARY`, `STRUCTURED_FORM`, `TEXT`).
 */
export function formatDocumentModality(
  modality: string | null | undefined,
  fallback = "Document"
): string {
  const trimmed = String(modality ?? "").trim();
  if (!trimmed) return fallback;
  return formatClinicalEnum(trimmed, fallback);
}

export interface ProvenanceAttribute {
  label: string;
  value: string;
}

function formatProvenanceValue(key: string, raw: unknown): string | null {
  if (raw === null || raw === undefined || raw === "") return null;
  if (typeof raw === "boolean") return raw ? "Yes" : "No";
  if (typeof raw === "number") {
    if (
      /confidence|score|probability/i.test(key) &&
      raw >= 0 &&
      raw <= 1 &&
      !Number.isInteger(raw)
    ) {
      return `${Math.round(raw * 100)}%`;
    }
    return String(raw);
  }
  if (typeof raw === "string") {
    const trimmed = raw.trim();
    if (!trimmed) return null;
    if (/^[A-Z][A-Z0-9_]+$/.test(trimmed)) {
      return formatClinicalEnum(trimmed);
    }
    return trimmed;
  }
  if (Array.isArray(raw)) {
    const parts = raw
      .map((entry) => formatProvenanceValue(key, entry))
      .filter((entry): entry is string => Boolean(entry));
    return parts.length > 0 ? parts.join(", ") : null;
  }
  return null;
}

/**
 * Extracts structured human-readable provenance attributes from an AI classification
 * or generation manifest object so raw JSON stays tucked inside a secondary disclosure.
 */
export function extractAiProvenanceAttributes(
  manifest: unknown
): ProvenanceAttribute[] {
  if (!manifest || typeof manifest !== "object" || Array.isArray(manifest)) {
    return [];
  }

  const rows: ProvenanceAttribute[] = [];
  for (const [key, value] of Object.entries(
    manifest as Record<string, unknown>
  )) {
    const formatted = formatProvenanceValue(key, value);
    if (formatted) {
      rows.push({
        label: formatClinicalEnum(key),
        value: formatted,
      });
      continue;
    }

    if (value && typeof value === "object" && !Array.isArray(value)) {
      for (const [nestedKey, nestedVal] of Object.entries(
        value as Record<string, unknown>
      )) {
        const nestedFormatted = formatProvenanceValue(nestedKey, nestedVal);
        if (nestedFormatted) {
          rows.push({
            label: `${formatClinicalEnum(key)} · ${formatClinicalEnum(nestedKey)}`,
            value: nestedFormatted,
          });
        }
      }
    }
  }

  return rows;
}
