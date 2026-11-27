export const CLINICAL_ACRONYMS = {
  ecrf: "eCRF",
  crf: "CRF",
  sdv: "SDV",
  ctms: "CTMS",
  ssu: "SSU",
  etmf: "eTMF",
  tmf: "TMF",
  gxp: "GxP",
  edc: "EDC",
  soa: "SoA",
  usdm: "USDM",
  pi: "PI",
  sae: "SAE",
  susar: "SUSAR",
  rtsm: "RTSM",
  irb: "IRB",
  iec: "IEC",
  qc: "QC",
  mdr: "MDR",
  capa: "CAPA",
  sla: "SLA",
  sop: "SOP",
  rbac: "RBAC",
  doa: "DoA",
  edoa: "eDoA",
  icf: "ICF",
  siv: "SIV",
  fpfv: "FPFV",
  mvr: "MVR",
  ip: "IP",
  kri: "KRI",
  qms: "QMS",
  cec: "CEC",
  dct: "DCT",
  udi: "UDI",
  dicom: "DICOM",
  recist: "RECIST",
  loinc: "LOINC",
  cdisc: "CDISC",
  odm: "ODM",
  sdtm: "SDTM",
  cra: "CRA",
  crc: "CRC",
  econsent: "eConsent",
  epro: "ePRO",
  ecoa: "eCOA",
  meddra: "MedDRA",
  whodrug: "WHODrug",
};

const LOWERCASE_CONNECTORS = new Set([
  "and",
  "or",
  "of",
  "for",
  "in",
  "on",
  "to",
  "by",
  "with",
]);

export function formatAnchorSlug(rawAnchor) {
  const clean = String(rawAnchor || "")
    .trim()
    .replace(/^#+/, "");
  if (!clean) return "";
  const words = clean.split(/[-_\s]+/).filter(Boolean);
  return words
    .map((word, idx) => {
      const lower = word.toLowerCase();
      if (CLINICAL_ACRONYMS[lower]) {
        return CLINICAL_ACRONYMS[lower];
      }
      if (idx > 0 && LOWERCASE_CONNECTORS.has(lower)) {
        return lower;
      }
      return lower.charAt(0).toUpperCase() + lower.slice(1);
    })
    .join(" ");
}

export function formatTicketScope(studyId, siteId) {
  if (studyId && siteId) return `${studyId} / ${siteId}`;
  if (studyId) return studyId;
  if (siteId) return siteId;
  return "Global";
}

export function formatTimestamp(isoString) {
  if (!isoString) return "Pending";
  const parsed = new Date(isoString);
  if (Number.isNaN(parsed.getTime())) return String(isoString);
  return parsed.toISOString().replace("T", " ").slice(0, 16) + " UTC";
}

export function formatTags(tags) {
  if (!tags) return [];
  if (Array.isArray(tags)) return tags;
  if (typeof tags === "string") {
    try {
      const parsed = JSON.parse(tags);
      if (Array.isArray(parsed)) return parsed;
    } catch {
      return tags
        .split(",")
        .map((t) => t.trim())
        .filter(Boolean);
    }
  }
  return [];
}

export function escapeTicketSelectorValue(raw) {
  if (!raw) return "";
  if (typeof CSS !== "undefined" && typeof CSS.escape === "function") {
    return CSS.escape(String(raw));
  }
  return String(raw).replace(/(["\\])/g, "\\$1");
}

export function resolveActiveRouteLocation(route) {
  if (!route) return "";
  if (typeof route.fullPath === "string" && route.fullPath.trim()) {
    return route.fullPath.trim();
  }
  if (typeof route.path === "string" && route.path.trim()) {
    return route.path.trim();
  }
  return "";
}
