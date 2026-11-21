// Offline-cached study site coordinator contacts and PII-safe display helpers.
import { MOCK_INSTRUMENTS } from "../data/mock-data.js";
import { state } from "../stores/state.js";

export const SITE_CONTACT_STORAGE_KEY = "cadence_site_contacts_v1";

export const DEFAULT_SITE_CONTACT = Object.freeze({
  siteName: "Cadence Clinical Research Site",
  coordinatorRole: "Study Site Coordinator",
  phoneDisplay: "+1 (800) 555-0199", // deid-ignore
  phoneHref: "tel:+18005550199", // deid-ignore
  emailDisplay: "coordinator@cadence-clinical.com", // deid-ignore
  emailHref:
    "mailto:coordinator@cadence-clinical.com?subject=Study%20Participant%20Support", // deid-ignore
  hours: "Available online & offline — contact site staff for urgent questions",
});

export const COORDINATOR_QUARANTINE_STEPS =
  "Next step: Tap Sync Now to reload active survey definitions, or contact your study coordinator by phone or email if this survey needs to be reassigned.";

function sanitizeTelHref(href, fallback) {
  if (typeof href === "string" && href.trim().startsWith("tel:")) {
    return href.trim();
  }
  return fallback;
}

function sanitizeMailtoHref(href, fallback) {
  if (typeof href === "string" && href.trim().startsWith("mailto:")) {
    return href.trim();
  }
  return fallback;
}

export function hydrateSiteSupportContact(overrides = null) {
  let cached = null;
  try {
    if (typeof localStorage !== "undefined") {
      const raw = localStorage.getItem(SITE_CONTACT_STORAGE_KEY);
      if (raw) {
        cached = JSON.parse(raw);
      }
    }
  } catch {
    cached = null;
  }

  const merged = {
    ...DEFAULT_SITE_CONTACT,
    ...(cached && typeof cached === "object" ? cached : {}),
    ...(overrides && typeof overrides === "object" ? overrides : {}),
  };

  merged.phoneHref = sanitizeTelHref(
    merged.phoneHref,
    DEFAULT_SITE_CONTACT.phoneHref
  );
  merged.emailHref = sanitizeMailtoHref(
    merged.emailHref,
    DEFAULT_SITE_CONTACT.emailHref
  );

  try {
    if (typeof localStorage !== "undefined") {
      localStorage.setItem(SITE_CONTACT_STORAGE_KEY, JSON.stringify(merged));
    }
  } catch {
    // Ignore storage quota or privacy mode errors; in-memory contact remains active.
  }

  state.siteSupportContact = merged;
  return merged;
}

export function getSiteSupportContact() {
  if (
    state.siteSupportContact &&
    typeof state.siteSupportContact === "object"
  ) {
    return state.siteSupportContact;
  }
  return hydrateSiteSupportContact();
}

const EMAIL_PATTERN = /\b([a-zA-Z0-9._%+-]+)@([a-zA-Z0-9.-]+\.[a-zA-Z]{2,})\b/g;
const SSN_PATTERN = /\b\d{3}-\d{2}-(\d{4})\b/g;
const PHONE_PATTERN =
  /(?<!\w)(?:\+?1[-.\s]*)?\(?\d{3}\)?[-.\s]*\d{3}[-.\s]*(\d{4})\b/g;

export function maskParticipantIdentifier(value) {
  if (value === null || value === undefined) return "";
  const raw = String(value).trim();
  if (!raw) return "";

  return raw
    .replace(EMAIL_PATTERN, (_m, localPart, domain) => {
      const prefix = localPart ? localPart[0] : "*";
      return `${prefix}***@${domain}`;
    })
    .replace(SSN_PATTERN, "***-**-$1")
    .replace(PHONE_PATTERN, "***-***-$1");
}

const PII_KEY_PATTERN =
  /(^|_)(name|first_name|last_name|full_name|patient_name|participant_name|email|phone|telephone|mobile|ssn|social_security|dob|date_of_birth|birth_date|address|street| mrn|nhs|subject_id|username)($|_)/i;

const COMMON_QUESTION_LABELS = Object.freeze({
  vssbp: "Systolic Blood Pressure (mmHg)",
  vsdpb: "Diastolic Blood Pressure (mmHg)",
  vshr: "Pulse Rate (bpm)",
  pain_scale: "Symptom Severity",
  pain_score: "Pain Score",
  has_symptoms: "New Physical Symptoms Today",
  severity: "Overall Symptom Severity",
  restricted_activity: "Restricted Daily Activities",
  missed_doses: "Missed Medication Doses",
  age: "Age",
  gender: "Gender",
});

function humanizeFieldKey(rawKey) {
  const cleaned = String(rawKey || "")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .trim();
  if (!cleaned) return "Question";
  return cleaned
    .split(/\s+/)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())
    .join(" ");
}

/**
 * Resolves a human-readable question prompt for a recorded answer key using
 * loaded or cached study instruments, falling back to plain clinical labels.
 */
export function resolveQuestionLabel(key, diaryId = null) {
  const fieldKey = String(key || "").trim();
  if (!fieldKey) return "Question";

  const activeInstruments =
    state.instruments && typeof state.instruments === "object"
      ? state.instruments
      : {};

  if (diaryId) {
    const directLabel =
      activeInstruments[diaryId]?.items?.[fieldKey]?.label ||
      MOCK_INSTRUMENTS[diaryId]?.items?.[fieldKey]?.label;
    if (directLabel) return directLabel;
  }

  for (const inst of Object.values(activeInstruments)) {
    const label = inst?.items?.[fieldKey]?.label;
    if (label) return label;
  }

  for (const inst of Object.values(MOCK_INSTRUMENTS)) {
    const label = inst?.items?.[fieldKey]?.label;
    if (label) return label;
  }

  if (COMMON_QUESTION_LABELS[fieldKey]) {
    return COMMON_QUESTION_LABELS[fieldKey];
  }

  return humanizeFieldKey(fieldKey);
}

/**
 * Resolves a participant-friendly survey title for a diary/instrument ID.
 */
export function getInstrumentDisplayName(diaryId) {
  const id = String(diaryId || "").trim();
  if (!id) return "Study Survey";
  const fromState =
    state.instruments && typeof state.instruments === "object"
      ? state.instruments[id]?.name
      : null;
  if (fromState) return fromState;
  if (MOCK_INSTRUMENTS[id]?.name) return MOCK_INSTRUMENTS[id].name;
  return humanizeFieldKey(id.replace(/^(inst|diary)_/i, ""));
}

function formatSafeScalar(key, val) {
  if (val === null || val === undefined || val === "") return "—";
  if (PII_KEY_PATTERN.test(String(key))) {
    return "[Protected]";
  }
  if (typeof val === "object") {
    if (Array.isArray(val)) {
      return val.map((v) => formatSafeScalar(key, v)).join(", ");
    }
    return "[Recorded]";
  }
  const str = maskParticipantIdentifier(String(val)).replace(/[{}[\]"]/g, "");
  return str || "—";
}

/**
 * Formats recorded answers into question-labeled `{ key, label, value }` rows
 * without emitting raw field codes, JSON syntax, or unmasked participant PII.
 */
export function formatAnswerPairs(answers, diaryId = null) {
  if (!answers || typeof answers !== "object" || Array.isArray(answers)) {
    return [];
  }
  return Object.entries(answers).map(([key, val]) => ({
    key,
    label: resolveQuestionLabel(key, diaryId),
    value: formatSafeScalar(key, val),
  }));
}

/**
 * Formats recorded answers as human-readable question/answer pairs without
 * emitting raw JSON syntax or unmasked participant PII.
 */
export function formatAnswersSummary(answers, diaryId = null) {
  const pairs = formatAnswerPairs(answers, diaryId);
  if (pairs.length === 0) return "";
  return pairs.map(({ label, value }) => `${label}: ${value}`).join(", ");
}

export function isQuarantinedSubmission(item) {
  return item?.status === "QUARANTINED" || item?.status === "LOCAL_QUARANTINED";
}

export function getSyncStatusBadgeClass(item) {
  if (!item || item.status === "QUEUED") return "pending";
  if (
    item.status === "CREATED" ||
    item.status === "UPDATED_CLIENT_WINS" ||
    item.status === "SYNCED" ||
    item.status === "MERGED"
  ) {
    return "completed";
  }
  if (
    item.status === "IGNORED_SERVER_WINS" ||
    item.status === "DECRYPTION_ERROR" ||
    isQuarantinedSubmission(item)
  ) {
    return "overdue";
  }
  return "pending";
}

export function getSyncStatusLabel(item) {
  if (!item) return "Saved offline";
  if (item.status === "QUEUED") return "Saved offline";
  if (
    item.status === "CREATED" ||
    item.status === "UPDATED_CLIENT_WINS" ||
    item.status === "SYNCED"
  ) {
    return "Sent";
  }
  if (item.status === "MERGED") return "Combined";
  if (item.status === "IGNORED_SERVER_WINS") return "Already on file";
  if (isQuarantinedSubmission(item)) return "Needs review";
  if (item.status === "DECRYPTION_ERROR") {
    return item.unrecoverable ? "Locked (previous PIN)" : "Locked — enter PIN";
  }
  return "Saved";
}

export function getSyncStatusExplanation(item, queuedDescription = "") {
  if (!item) return "";
  if (item.status === "QUEUED") {
    return queuedDescription || "Waiting for internet connection...";
  }
  if (
    item.status === "CREATED" ||
    item.status === "UPDATED_CLIENT_WINS" ||
    item.status === "SYNCED"
  ) {
    return "Sent to your study team.";
  }
  if (item.status === "MERGED") {
    return "Combined your offline answers with your study record.";
  }
  if (item.status === "IGNORED_SERVER_WINS") {
    return "Your study team already had the latest version of this entry.";
  }
  if (isQuarantinedSubmission(item)) {
    return humanizeValidationError(item.error);
  }
  if (item.status === "DECRYPTION_ERROR") {
    if (item.unrecoverable) {
      return humanizeValidationError(item.error);
    }
    return "Locked — enter your security PIN to unlock your saved answers.";
  }
  return "";
}

/**
 * Converts technical or schema validation error strings into plain-language
 * explanations suitable for trial participants, stripping raw JSON and PII.
 */
export function humanizeValidationError(rawError) {
  if (!rawError) {
    return "One or more survey responses did not match the current study rules.";
  }

  let msg = String(rawError).trim();

  // Strip raw JSON objects/arrays if an upstream error serialized a payload
  msg = msg.replace(/\{[\s\S]*\}|\[[\s\S]*\]/g, "").trim();

  // Strip technical prefixes while keeping actionable clinical field guidance
  msg = msg
    .replace(/^Local schema quarantine:\s*/i, "")
    .replace(/^Schema Validation Failed:\s*/i, "")
    .replace(/^Demographic Validation Error:\s*/i, "")
    .replace(/^Clinical Validation Error:\s*/i, "")
    .replace(/^HTTP Error (400|422):?\s*/i, "")
    .trim();

  msg = maskParticipantIdentifier(msg)
    .replace(/[{}[\]]/g, "")
    .trim();

  if (
    !msg ||
    /^unprocessable entity$/i.test(msg) ||
    /^bad request$/i.test(msg)
  ) {
    return "One or more answers fell outside the allowed clinical range for the active survey version.";
  }

  return msg;
}
