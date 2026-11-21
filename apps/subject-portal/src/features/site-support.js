// Offline-cached study site coordinator contacts and PII-safe display helpers.
import { DEFAULT_DEMO_SITE } from "demo-fixtures";
import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";

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

  const demoSiteContact =
    isDemoModeActive() && DEFAULT_DEMO_SITE?.name
      ? {
          siteName: `${DEFAULT_DEMO_SITE.name} (${DEFAULT_DEMO_SITE.code})`,
        }
      : {};

  const merged = {
    ...DEFAULT_SITE_CONTACT,
    ...demoSiteContact,
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
 * Formats recorded answers as human-readable clinical key/value pairs without
 * emitting raw JSON syntax or unmasked participant PII.
 */
export function formatAnswersSummary(answers) {
  if (!answers || typeof answers !== "object" || Array.isArray(answers)) {
    return "";
  }
  const entries = Object.entries(answers);
  if (entries.length === 0) return "";
  return entries
    .map(([key, val]) => `${key}: ${formatSafeScalar(key, val)}`)
    .join(", ");
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
