/**
 * Shared clinical entity & DIA TMF artifact resolution helpers for <EntityLink>
 * and <EntityPeekDrawer>. Keeps TmfDocument and DIA taxonomy presentation
 * decoupled from Cadence.Kernel NodeKinds.
 */

export const DIA_TMF_ARTIFACT_CATALOG = Object.freeze({
  "01.01.01": Object.freeze({
    zone: 1,
    zoneName: "Trial Management",
    section: "01.01",
    sectionName: "Trial Design",
    artifactCode: "01.01.01",
    artifactName: "Clinical Trial Protocol",
  }),
  "01.01.02": Object.freeze({
    zone: 1,
    zoneName: "Trial Management",
    section: "01.01",
    sectionName: "Trial Design",
    artifactCode: "01.01.02",
    artifactName: "Clinical Trial Protocol Amendment",
  }),
  "01.01.03": Object.freeze({
    zone: 1,
    zoneName: "Trial Management",
    section: "01.01",
    sectionName: "Trial Design",
    artifactCode: "01.01.03",
    artifactName: "Clinical Trial Protocol Signoff",
  }),
  "02.01.01": Object.freeze({
    zone: 2,
    zoneName: "Central Trial Documents",
    section: "02.01",
    sectionName: "Product Information",
    artifactCode: "02.01.01",
    artifactName: "Investigator's Brochure",
  }),
  "04.02.01": Object.freeze({
    zone: 4,
    zoneName: "IRB/IEC & Regulatory Approvals",
    section: "04.02",
    sectionName: "IRB/IEC Approvals",
    artifactCode: "04.02.01",
    artifactName: "IRB/IEC Approval Notification",
  }),
  "05.02.01": Object.freeze({
    zone: 5,
    zoneName: "Site Management",
    section: "05.02",
    sectionName: "Investigator Qualification",
    artifactCode: "05.02.01",
    artifactName: "FDA Form 1572",
  }),
  "05.02.02": Object.freeze({
    zone: 5,
    zoneName: "Site Management",
    section: "05.02",
    sectionName: "Investigator Qualification",
    artifactCode: "05.02.02",
    artifactName: "Financial Disclosure",
  }),
  "05.02.03": Object.freeze({
    zone: 5,
    zoneName: "Site Management",
    section: "05.02",
    sectionName: "Investigator Qualification",
    artifactCode: "05.02.03",
    artifactName: "Investigator CV",
  }),
  "05.02.04": Object.freeze({
    zone: 5,
    zoneName: "Site Management",
    section: "05.02",
    sectionName: "Investigator Qualification",
    artifactCode: "05.02.04",
    artifactName: "Delegation of Authority Log",
  }),
  "05.02.05": Object.freeze({
    zone: 5,
    zoneName: "Site Management",
    section: "05.02",
    sectionName: "Investigator Qualification",
    artifactCode: "05.02.05",
    artifactName: "Informed Consent Form",
  }),
  "05.04.01": Object.freeze({
    zone: 5,
    zoneName: "Site Management",
    section: "05.04",
    sectionName: "Site Monitoring",
    artifactCode: "05.04.01",
    artifactName: "Site Communication Log",
  }),
  "05.04.02": Object.freeze({
    zone: 5,
    zoneName: "Site Management",
    section: "05.04",
    sectionName: "Site Monitoring",
    artifactCode: "05.04.02",
    artifactName: "Monitoring Visit Letter",
  }),
  "07.01.01": Object.freeze({
    zone: 7,
    zoneName: "Safety Reporting",
    section: "07.01",
    sectionName: "Safety Notifications",
    artifactCode: "07.01.01",
    artifactName: "Serious Adverse Event Report",
  }),
  "08.01.01": Object.freeze({
    zone: 8,
    zoneName: "Centralized & Local Testing",
    section: "08.01",
    sectionName: "Lab Documentation",
    artifactCode: "08.01.01",
    artifactName: "Central Laboratory Certificate",
  }),
  "11.01.01": Object.freeze({
    zone: 11,
    zoneName: "Statistics",
    section: "11.01",
    sectionName: "Statistical Analysis",
    artifactCode: "11.01.01",
    artifactName: "Statistical Analysis Plan",
  }),
});

const STATUS_LABELS = Object.freeze({
  DRAFT: "Draft",
  TECHNICAL_QC: "Technical QC",
  CLINICAL_QC: "Clinical QC",
  IN_REVIEW: "In Review",
  APPROVED: "Approved",
  SIGNED: "Signed",
  FINALIZED: "Finalized",
  FILED: "Filed in eTMF",
  MISSING: "Missing Required Artifact",
  REJECTED: "Rejected",
  SUPERSEDED: "Superseded",
  ARCHIVED: "Archived",
});

const STATUS_TONES = Object.freeze({
  DRAFT: "draft",
  TECHNICAL_QC: "warning",
  CLINICAL_QC: "warning",
  IN_REVIEW: "warning",
  APPROVED: "verified",
  SIGNED: "signed",
  FINALIZED: "signed",
  FILED: "verified",
  MISSING: "warning",
  REJECTED: "error",
  SUPERSEDED: "frozen",
  ARCHIVED: "locked",
});

const RAW_PARTICIPANT_NODEREF_RE = /^participant:[0-9a-f-]{8,}(?:@\d+)?$/i;
const RAW_UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const RAW_DOC_ID_RE = /^(?:DOC|ETMF|LTR)-[A-Z0-9-]+$/i;

/**
 * Normalizes a participant/subject identifier so raw `participant:<uuid>` NodeRefs
 * or bare UUIDs are never propagated as `subjectId` query values.
 */
export function normalizeEntitySubjectCode(raw) {
  const trimmed = typeof raw === "string" ? raw.trim() : "";
  if (!trimmed) return "";
  if (RAW_PARTICIPANT_NODEREF_RE.test(trimmed)) return "";
  if (RAW_UUID_RE.test(trimmed)) return "";
  return trimmed;
}

/**
 * Humanizes uppercase/underscore status or enum tokens into clinical title case.
 */
export function humanizeEntityStatus(raw) {
  const trimmed = typeof raw === "string" ? raw.trim() : "";
  if (!trimmed) return "";
  const upper = trimmed.toUpperCase();
  if (STATUS_LABELS[upper]) return STATUS_LABELS[upper];
  return trimmed
    .replace(/[_-]+/g, " ")
    .toLowerCase()
    .replace(/\b\w/g, (ch) => ch.toUpperCase())
    .replace(/\bQc\b/g, "QC")
    .replace(/\bIrb\b/g, "IRB")
    .replace(/\bIec\b/g, "IEC")
    .replace(/\bFda\b/g, "FDA")
    .replace(/\bIcf\b/g, "ICF")
    .replace(/\bCv\b/g, "CV");
}

/**
 * Resolves DIA TMF Reference Model metadata from an artifact code or clinical name.
 */
export function resolveDiaArtifactMeta(codeOrName) {
  const trimmed = typeof codeOrName === "string" ? codeOrName.trim() : "";
  if (!trimmed) return null;

  if (DIA_TMF_ARTIFACT_CATALOG[trimmed]) {
    return DIA_TMF_ARTIFACT_CATALOG[trimmed];
  }

  const codeMatch = trimmed.match(/\b(\d{2}\.\d{2}\.\d{2})\b/);
  if (codeMatch && DIA_TMF_ARTIFACT_CATALOG[codeMatch[1]]) {
    return DIA_TMF_ARTIFACT_CATALOG[codeMatch[1]];
  }

  const lower = trimmed.toLowerCase();
  if (lower.includes("1572")) return DIA_TMF_ARTIFACT_CATALOG["05.02.01"];
  if (
    lower.includes("irb") ||
    lower.includes("iec") ||
    lower.includes("ethics")
  ) {
    return DIA_TMF_ARTIFACT_CATALOG["04.02.01"];
  }
  if (lower.includes("financial") || lower.includes("disclosure")) {
    return DIA_TMF_ARTIFACT_CATALOG["05.02.02"];
  }
  if (
    /\bcv\b/.test(lower) ||
    lower.includes("curriculum vitae") ||
    lower.includes("investigator cv")
  ) {
    return DIA_TMF_ARTIFACT_CATALOG["05.02.03"];
  }
  if (lower.includes("delegation") || /\bdoa\b/.test(lower)) {
    return DIA_TMF_ARTIFACT_CATALOG["05.02.04"];
  }
  if (lower.includes("consent") || /\bicf\b/.test(lower)) {
    return DIA_TMF_ARTIFACT_CATALOG["05.02.05"];
  }
  if (lower.includes("amendment")) {
    return DIA_TMF_ARTIFACT_CATALOG["01.01.02"];
  }
  if (lower.includes("protocol")) {
    return DIA_TMF_ARTIFACT_CATALOG["01.01.01"];
  }
  if (lower.includes("brochure") || /\bib\b/.test(lower)) {
    return DIA_TMF_ARTIFACT_CATALOG["02.01.01"];
  }
  if (
    lower.includes("letter") ||
    lower.includes("monitoring") ||
    /\bmvr\b/.test(lower) ||
    /\bsiv\b/.test(lower) ||
    /\bimv\b/.test(lower)
  ) {
    return DIA_TMF_ARTIFACT_CATALOG["05.04.02"];
  }
  if (lower.includes("sae") || lower.includes("adverse")) {
    return DIA_TMF_ARTIFACT_CATALOG["07.01.01"];
  }
  if (lower.includes("lab") || lower.includes("certificate")) {
    return DIA_TMF_ARTIFACT_CATALOG["08.01.01"];
  }
  if (lower.includes("statistical") || /\bsap\b/.test(lower)) {
    return DIA_TMF_ARTIFACT_CATALOG["11.01.01"];
  }
  if (
    lower.includes("site agreement") ||
    lower.includes("cta") ||
    lower.includes("contract")
  ) {
    return {
      zone: 5,
      zoneName: "Site Management",
      section: "05.02",
      sectionName: "Investigator Qualification",
      artifactCode: "05.02.01",
      artifactName: trimmed,
    };
  }

  return null;
}

/**
 * Builds a scoped `/etmf` deep-link URL preserving study, site, normalized subjectCode,
 * documentId, artifactCode, and binder/qc-queue tab.
 */
export function buildEntityBinderUrl(options = {}) {
  const basePath =
    typeof options.basePath === "string" && options.basePath.trim()
      ? options.basePath.trim()
      : "/etmf";
  const params = new URLSearchParams();

  const studyId =
    typeof options.studyId === "string" ? options.studyId.trim() : "";
  const siteId =
    typeof options.siteId === "string" ? options.siteId.trim() : "";
  const subjectId = normalizeEntitySubjectCode(options.subjectId);
  const documentId =
    typeof options.documentId === "string" ? options.documentId.trim() : "";
  const artifactCode =
    typeof options.artifactCode === "string" ? options.artifactCode.trim() : "";
  const tab =
    options.tab === "qc-queue" || options.tab === "binder" ? options.tab : "";

  if (studyId) params.set("studyId", studyId);
  if (siteId) params.set("siteId", siteId);
  if (subjectId) params.set("subjectId", subjectId);
  if (artifactCode) params.set("artifactCode", artifactCode);
  if (documentId) params.set("documentId", documentId);
  if (tab) params.set("tab", tab);

  const qs = params.toString();
  return qs ? `${basePath}?${qs}` : basePath;
}

/**
 * Resolves a TmfDocument or DIA artifact reference into a human-readable descriptor
 * for <EntityLink> and <EntityPeekDrawer>.
 */
export function resolveEntityLinkDescriptor(input = {}) {
  const doc =
    input.document && typeof input.document === "object"
      ? input.document
      : null;

  const documentId = String(
    input.documentId || doc?.id || doc?.document_id || ""
  ).trim();

  const rawCode = String(
    input.artifactCode || doc?.artifact_code || doc?.artifactCode || ""
  ).trim();

  const rawArtifactName = String(
    input.artifactName || doc?.artifact_type || doc?.artifactType || ""
  ).trim();

  const filename = String(input.filename || doc?.filename || "").trim();

  const diaMeta =
    resolveDiaArtifactMeta(rawCode) ||
    resolveDiaArtifactMeta(rawArtifactName) ||
    resolveDiaArtifactMeta(input.label) ||
    resolveDiaArtifactMeta(filename);

  const artifactCode = rawCode || diaMeta?.artifactCode || "";
  const artifactLabel =
    rawArtifactName || diaMeta?.artifactName || "Regulated eTMF Artifact";

  const zoneNumber = doc?.zone ?? diaMeta?.zone ?? null;
  const zoneName = diaMeta?.zoneName || "";
  const sectionCode = String(doc?.section || diaMeta?.section || "").trim();
  const sectionName = diaMeta?.sectionName || "";

  const zoneLabel = zoneNumber
    ? zoneName
      ? `Zone ${zoneNumber}: ${zoneName}`
      : `Zone ${zoneNumber}`
    : "";
  const sectionLabel = sectionCode
    ? sectionName
      ? `${sectionCode} ${sectionName}`
      : sectionCode
    : "";

  const rawStatus = String(
    input.status || doc?.status || (documentId ? "FILED" : "")
  ).trim();
  const statusUpper = rawStatus.toUpperCase();
  const statusLabel = humanizeEntityStatus(rawStatus);
  const statusTone = STATUS_TONES[statusUpper] || "draft";

  const studyId = String(
    input.studyId || doc?.study_id || doc?.studyId || ""
  ).trim();
  const siteId = String(
    input.siteId || doc?.site_id || doc?.siteId || ""
  ).trim();
  const subjectId = normalizeEntitySubjectCode(
    input.subjectId || doc?.subject_id || doc?.subjectId || ""
  );

  const versionIndex =
    input.versionIndex ?? doc?.version_index ?? doc?.versionIndex ?? null;
  const versionLabel =
    versionIndex !== null && versionIndex !== undefined && versionIndex !== ""
      ? `v${versionIndex}`
      : "";

  const explicitLabel =
    typeof input.label === "string" ? input.label.trim() : "";
  let chipLabel = explicitLabel;
  if (!chipLabel) {
    if (rawArtifactName && !RAW_DOC_ID_RE.test(rawArtifactName)) {
      chipLabel = rawArtifactName;
    } else if (diaMeta?.artifactName) {
      chipLabel = diaMeta.artifactName;
    } else if (filename) {
      chipLabel = filename;
    } else {
      chipLabel = "Regulated eTMF Document";
    }
  }

  const title =
    filename ||
    (explicitLabel && !RAW_DOC_ID_RE.test(explicitLabel)
      ? explicitLabel
      : "") ||
    artifactLabel ||
    chipLabel;

  const subtitle =
    typeof input.subtitle === "string" && input.subtitle.trim()
      ? input.subtitle.trim()
      : [zoneLabel, sectionLabel].filter(Boolean).join(" · ");

  const tab =
    input.tab || (statusUpper === "TECHNICAL_QC" ? "qc-queue" : "binder");

  const query = {};
  if (studyId) query.studyId = studyId;
  if (siteId) query.siteId = siteId;
  if (subjectId) query.subjectId = subjectId;
  if (artifactCode) query.artifactCode = artifactCode;
  if (documentId) query.documentId = documentId;
  if (tab) query.tab = tab;

  const href =
    typeof input.href === "string" && input.href.trim()
      ? input.href.trim()
      : buildEntityBinderUrl({
          studyId,
          siteId,
          subjectId,
          artifactCode,
          documentId,
          tab,
        });

  const supportParts = [];
  if (documentId) supportParts.push(`Document ID: ${documentId}`);
  const checksum = String(
    doc?.content_checksum || doc?.contentChecksum || ""
  ).trim();
  if (checksum) supportParts.push(`SHA-256: ${checksum}`);
  if (typeof input.supportDetails === "string" && input.supportDetails.trim()) {
    supportParts.push(input.supportDetails.trim());
  }

  return {
    kind: documentId ? "etmf-document" : "dia-artifact",
    kindLabel: documentId ? "eTMF Document" : "DIA TMF Artifact",
    chipLabel,
    title,
    subtitle,
    zoneLabel,
    sectionLabel,
    artifactCode,
    artifactLabel,
    filename,
    documentId,
    statusLabel,
    statusTone,
    studyId,
    siteId,
    subjectId,
    versionLabel,
    href,
    query,
    supportDetails: supportParts.join(" · "),
  };
}
