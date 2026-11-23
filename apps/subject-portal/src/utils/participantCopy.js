import { DEMO_STUDY } from "demo-fixtures";

export const PLAIN_OCR_HOLD_EXPLANATION =
  "We could not read every detail on the receipt photo clearly, so your study coordinator will check it for you.";

const RAW_STUDY_CODE_PATTERN =
  /^(?:(?:study|cadence|proto(?:col)?|trial|site|icf)[-_a-z0-9]+|(?:study|cadence|proto(?:col)?)\s+\d+|[a-z0-9]{2,12}(?:[-_][a-z0-9]{1,12})+\d[a-z0-9-_]*|[a-z]{2,8}-\d{2,5})$/i;

const KNOWN_STUDY_TITLES = Object.freeze({
  [String(DEMO_STUDY?.code || "CADENCE-101").toUpperCase()]:
    "Cadence Cardiovascular Outcomes Study",
  [String(DEMO_STUDY?.uuid || "").toUpperCase()]:
    "Cadence Cardiovascular Outcomes Study",
  "STUDY-01": "Cardiology Companion Study",
  STUDY_01: "Cardiology Companion Study",
  STUDY_001: "Cardiology Companion Study",
  "STUDY-LIVE": "Cardiology Companion Study",
});

const LANGUAGE_LABELS = Object.freeze({
  en: "English",
  es: "Spanish (Español)",
  fr: "French (Français)",
  de: "German (Deutsch)",
  nl: "Dutch (Nederlands)",
  pt: "Portuguese (Português)",
});

export function isRawProtocolOrStudyCode(value) {
  if (typeof value !== "string") return false;
  const trimmed = value.trim();
  if (!trimmed) return false;
  if (
    RAW_STUDY_CODE_PATTERN.test(trimmed) ||
    /^v?\d+(?:\.\d+)*(?:[-_][a-z0-9-]+)*$/i.test(trimmed)
  ) {
    return true;
  }
  return !/\s/.test(trimmed) && /^[A-Z0-9]+(?:[-_][A-Z0-9]+)+$/i.test(trimmed);
}

export function resolveFriendlyStudyTitle(studyId, candidateTitle = "") {
  const cleanCandidate =
    typeof candidateTitle === "string" ? candidateTitle.trim() : "";
  if (cleanCandidate && !isRawProtocolOrStudyCode(cleanCandidate)) {
    // Strip trailing or embedded raw study codes like "(STUDY-01)" or "[CADENCE-101]"
    const stripped = cleanCandidate
      .replace(/\s*[([](?:STUDY|CADENCE|PROTO)[-_A-Z0-9]+[\])]/gi, "")
      .trim();
    if (stripped) return stripped;
  }

  const normalizedId =
    typeof studyId === "string" ? studyId.trim().toUpperCase() : "";
  if (normalizedId && KNOWN_STUDY_TITLES[normalizedId]) {
    return KNOWN_STUDY_TITLES[normalizedId];
  }
  if (/ONCO|TUMOR|CANCER/.test(normalizedId)) {
    return "Oncology Companion Study";
  }
  if (/NEURO|BRAIN|CNS/.test(normalizedId)) {
    return "Neurology Companion Study";
  }
  if (/DERM/.test(normalizedId)) {
    return "Dermatology Companion Study";
  }
  if (/PEDI/.test(normalizedId)) {
    return "Pediatric Companion Study";
  }
  if (/CARDIO|HEART|CV/.test(normalizedId)) {
    return "Cardiology Companion Study";
  }
  if (normalizedId && !isRawProtocolOrStudyCode(normalizedId)) {
    return studyId.trim();
  }
  return "Cardiology Companion Study";
}

export function formatConsentEditionLabel(documentVersion) {
  const parsed = Number(documentVersion);
  if (Number.isFinite(parsed) && parsed > 0) {
    return `Edition ${parsed}`;
  }
  if (typeof documentVersion === "string" && documentVersion.trim()) {
    const clean = documentVersion.trim();
    const versionMatch = clean.match(/^v?(\d+(?:\.\d+)*)/i);
    if (versionMatch?.[1]) {
      return `Edition ${versionMatch[1]}`;
    }
    if (!isRawProtocolOrStudyCode(clean)) {
      return `Edition ${clean}`;
    }
  }
  return "Current Edition";
}

export function formatConsentLanguageLabel(languageCode) {
  const clean =
    typeof languageCode === "string" ? languageCode.trim().toLowerCase() : "en";
  return LANGUAGE_LABELS[clean] || "English";
}

export function humanizeExpenseReviewNotes(rawNotes) {
  if (typeof rawNotes !== "string") return "";
  const trimmed = rawNotes.trim();
  if (!trimmed) return "";

  let text = trimmed.replace(
    /OCR confidence(?:\s+score)?\s*\(?[0-9.]+\)?\s*(?:is\s+)?(?:below|<)\s*0\.90?[^.;]*[.;]?/gi,
    PLAIN_OCR_HOLD_EXPLANATION
  );
  text = text.replace(
    /routed to (?:site\s+)?coordinator review(?:\s+queue)?\.?/gi,
    "Your study coordinator has received this receipt and will review it soon."
  );
  text = text.replace(
    /IRS \$600\.00 annual taxable threshold reached without verified Form W-9 certification\./gi,
    "Your yearly study payments have reached $600.00, so your study coordinator needs a verified Form W-9 before sending payouts."
  );
  text = text.replace(
    /Gross amount \(\$?([0-9.]+)\) exceeds \$100\.00 auto-approval threshold\./gi,
    "Receipts over $100.00 ($1) get a quick check from your study coordinator before payment."
  );
  text = text.replace(
    /Gross amount \$([0-9.]+) exceeds \$100\.00 auto-approval threshold\./gi,
    "Receipts over $100.00 ($$$1) get a quick check from your study coordinator before payment."
  );
  return text.replace(/\s+/g, " ").trim();
}
