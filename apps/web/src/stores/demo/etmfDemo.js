import { DEMO_STUDY, getDemoPersonaByWorkflow } from "demo-fixtures";
import {
  demoNowIso,
  demoNowMs,
  demoRandomToken,
  isDemoBuild,
  nextDemoStep,
} from "../../utils/demoMode.js";

function isServiceMethodMocked(fn) {
  return Boolean(fn && (fn._isMockFunction || typeof fn.mock === "object"));
}

export function resolveDemoEtmfStudyId() {
  return isDemoBuild() && DEMO_STUDY.code ? DEMO_STUDY.code : "";
}

export function buildDefaultDocuments() {
  const studyId = DEMO_STUDY.code || "CADENCE-101";
  const leadUsername =
    getDemoPersonaByWorkflow("designer_lead")?.username || "designer.lead";
  const piPersona = getDemoPersonaByWorkflow("investigator");
  const piUsername = piPersona?.username || "pi.site101";
  const piFullName = piPersona
    ? `${piPersona.firstName} ${piPersona.lastName}`
    : "Site 101 Investigator";
  return [
    {
      id: "DOC-100",
      study_id: studyId,
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      filename: "protocol_v1_draft.pdf",
      mime_type: "application/pdf",
      created_at: "2026-01-06T12:00:00Z",
      created_by: leadUsername,
      version_index: 1,
      status: "DRAFT",
      reason_for_change: "Initial version",
    },
    {
      id: "DOC-999",
      study_id: studyId,
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.05",
      artifact_type: "Informed Consent Form",
      filename: "icf_v1.0_signed.pdf",
      mime_type: "application/pdf",
      created_at: "2026-01-10T09:30:00Z",
      created_by: piUsername,
      version_index: 1,
      status: "APPROVED",
      reason_for_change: "Initial signed consent.",
      signer: piFullName,
      signing_timestamp: "2026-01-10T09:35:00Z",
      signature_manifestation: {
        signing_reason: "Consent approval",
      },
    },
  ];
}

export function getDefaultDocuments() {
  return isDemoBuild() ? buildDefaultDocuments() : [];
}

export function localPredictTaxonomy(filename = "", content = "") {
  const text = `${filename} ${content}`.toLowerCase();
  if (text.includes("protocol") || text.includes("amendment")) {
    return {
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      confidence: 0.96,
    };
  }
  if (text.includes("1572") || text.includes("fda 1572")) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.01",
      artifact_type: "FDA Form 1572",
      confidence: 0.98,
    };
  }
  if (text.includes("financial") || text.includes("disclosure")) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.02",
      artifact_type: "Financial Disclosure",
      confidence: 0.94,
    };
  }
  if (
    text.includes("cv") ||
    text.includes("curriculum") ||
    text.includes("resume")
  ) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.03",
      artifact_type: "Investigator CV",
      confidence: 0.95,
    };
  }
  if (
    text.includes("delegation") ||
    text.includes("doa") ||
    text.includes("log")
  ) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.04",
      artifact_type: "Delegation of Authority Log",
      confidence: 0.92,
    };
  }
  if (
    text.includes("consent") ||
    text.includes("icf") ||
    text.includes("informed")
  ) {
    return {
      zone: 5,
      section: "05.02",
      artifact_code: "05.02.05",
      artifact_type: "Informed Consent Form",
      confidence: 0.97,
    };
  }
  if (text.includes("brochure") || text.includes("ib")) {
    return {
      zone: 2,
      section: "02.01",
      artifact_code: "02.01.01",
      artifact_type: "Investigator's Brochure",
      confidence: 0.95,
    };
  }
  if (
    text.includes("irb") ||
    text.includes("approval") ||
    text.includes("iec")
  ) {
    return {
      zone: 4,
      section: "04.02",
      artifact_code: "04.02.01",
      artifact_type: "IRB/IEC Approval Notification",
      confidence: 0.91,
    };
  }
  if (
    text.includes("sae") ||
    text.includes("adverse") ||
    text.includes("safety")
  ) {
    return {
      zone: 7,
      section: "07.01",
      artifact_code: "07.01.01",
      artifact_type: "Serious Adverse Event Report",
      confidence: 0.93,
    };
  }
  if (text.includes("lab") || text.includes("certificate")) {
    return {
      zone: 8,
      section: "08.01",
      artifact_code: "08.01.01",
      artifact_type: "Central Laboratory Certificate",
      confidence: 0.89,
    };
  }
  if (text.includes("sap") || text.includes("statistic")) {
    return {
      zone: 11,
      section: "11.01",
      artifact_code: "11.01.01",
      artifact_type: "Statistical Analysis Plan",
      confidence: 0.92,
    };
  }
  return {
    zone: 1,
    section: "01.01",
    artifact_code: "01.01.01",
    artifact_type: "General Clinical Document",
    confidence: 0.65,
  };
}

export function buildLocalIntelligenceReport(
  filename,
  content = "",
  mimeType = "application/pdf"
) {
  const primary = localPredictTaxonomy(filename, content);
  const tier =
    primary.confidence >= 0.85
      ? "HIGH"
      : primary.confidence >= 0.7
        ? "MEDIUM"
        : "LOW";
  return {
    filename,
    mime_type: mimeType,
    sha256_hash:
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
    modality: mimeType.includes("pdf") ? "PDF_BINARY" : "TEXT",
    primary_classification: primary,
    alternative_candidates: [],
    confidence_tier: tier,
    qc_recommendation: tier === "HIGH" ? "AUTO_CLASSIFY" : "CRA_QC_REQUIRED",
    ai_generation_manifest: {
      model: "Cadence-eTMF-Classifier-v3.2",
      prompt_hash: "a1b2c3d4e5f6", // pragma: allowlist secret
      confidence: primary.confidence,
      confidence_tier: tier,
      timestamp: new Date().toISOString(),
    },
  };
}

export function fetchDemoEtmfDocuments({
  authStore,
  etmfService,
  selectedArtifactId,
}) {
  if (
    !isDemoBuild() ||
    !authStore?.isDemoMode ||
    authStore.token ||
    isServiceMethodMocked(etmfService?.getDocuments)
  ) {
    return { handled: false, documentsList: [] };
  }

  let allDocs;
  if (typeof window !== "undefined" && window.localStorage) {
    const stored = window.localStorage.getItem("demo_documents");
    if (stored) {
      allDocs = JSON.parse(stored);
    } else {
      allDocs = getDefaultDocuments();
      window.localStorage.setItem("demo_documents", JSON.stringify(allDocs));
    }
  } else {
    allDocs = getDefaultDocuments();
  }

  const documentsList = selectedArtifactId
    ? allDocs.filter((doc) => doc.artifact_code === selectedArtifactId)
    : allDocs;

  return { handled: true, documentsList };
}

export async function uploadDemoEtmfDocument({
  authStore,
  etmfService,
  body,
  changeReason,
  documentsList,
}) {
  if (
    !isDemoBuild() ||
    !authStore?.isDemoMode ||
    authStore.token ||
    isServiceMethodMocked(etmfService?.ingestDocument)
  ) {
    return { handled: false, response: null };
  }

  await new Promise((resolve) => setTimeout(resolve, 300));
  const step = nextDemoStep("etmf-upload");
  const newDoc = {
    id: "DOC-" + demoNowMs(step),
    study_id: body.study_id,
    site_id: body.site_id,
    zone: body.zone,
    section: body.section,
    artifact_code: body.artifact_code,
    artifact_type: body.artifact_type,
    filename: body.filename,
    mime_type: body.mime_type,
    content: body.content,
    content_base64: body.content_base64,
    created_at: demoNowIso(step),
    created_by:
      authStore.identity?.username ||
      getDemoPersonaByWorkflow("designer_lead")?.username ||
      "designer.lead",
    version_index: 1,
    status: "DRAFT",
    reason_for_change: changeReason,
  };

  if (typeof window !== "undefined" && window.localStorage) {
    const stored = window.localStorage.getItem("demo_documents");
    const allDocs = stored ? JSON.parse(stored) : getDefaultDocuments();
    allDocs.push(newDoc);
    window.localStorage.setItem("demo_documents", JSON.stringify(allDocs));
  } else if (Array.isArray(documentsList)) {
    documentsList.push(newDoc);
  }

  return {
    handled: true,
    response: {
      status: "success",
      document_id: newDoc.id,
      version_index: 1,
    },
  };
}

export function fallbackSubmitDemoDraftForQc({
  authStore,
  etmfService,
  documentsList,
  docId,
  reason_for_change,
}) {
  const offlineDemo =
    isDemoBuild() && authStore?.isDemoMode && !authStore.token;
  if (!offlineDemo || isServiceMethodMocked(etmfService?.submitDocumentForQc)) {
    return { handled: false, result: null };
  }
  const doc = (documentsList || []).find((d) => d.id === docId);
  if (doc) {
    doc.status = "TECHNICAL_QC";
    doc.approval_status = "TECHNICAL_QC";
    doc.reason_for_change = reason_for_change;
  }
  return {
    handled: true,
    result: doc || { id: docId, status: "TECHNICAL_QC" },
  };
}

export function resetDemoEtmfStorage() {
  if (!isDemoBuild()) {
    return [];
  }
  if (typeof window !== "undefined" && window.localStorage) {
    window.localStorage.removeItem("demo_documents");
  }
  return getDefaultDocuments();
}

export function fallbackStageDemoQcDocument({
  authStore,
  scopedPayload,
  currentStudyId,
  documentsList,
  qcQueue,
}) {
  const offlineDemo =
    isDemoBuild() && authStore?.isDemoMode && !authStore.token;
  if (!offlineDemo) {
    return { handled: false, item: null };
  }
  const report = buildLocalIntelligenceReport(
    scopedPayload.filename,
    scopedPayload.content,
    scopedPayload.mime_type
  );
  const step = nextDemoStep("etmf-qc-stage");
  const newDoc = {
    id: "DOC-" + demoRandomToken(7, 36, "etmf-qc-stage").toUpperCase(),
    study_id: scopedPayload.study_id || currentStudyId,
    site_id: scopedPayload.site_id || null,
    zone: report.primary_classification.zone,
    section: report.primary_classification.section,
    artifact_code: report.primary_classification.artifact_code,
    artifact_type: report.primary_classification.artifact_type,
    filename: scopedPayload.filename,
    mime_type: scopedPayload.mime_type || "application/pdf",
    created_at: demoNowIso(step),
    created_by: "crc.site101",
    version_index: 1,
    status: "TECHNICAL_QC",
    approval_status: "TECHNICAL_QC",
    reason_for_change: scopedPayload.reason_for_change,
    metadata_json: {
      document_intelligence: report,
      assigned_cra: scopedPayload.assigned_cra || null,
    },
  };
  if (Array.isArray(documentsList)) {
    documentsList.push(newDoc);
  }
  const item = {
    document_id: newDoc.id,
    study_id: newDoc.study_id,
    site_id: newDoc.site_id,
    filename: newDoc.filename,
    status: "TECHNICAL_QC",
    intelligence_report: report,
    staged_at: newDoc.created_at,
    staged_by: newDoc.created_by,
    qc_assigned_to: scopedPayload.assigned_cra || null,
    discrepancy_notes:
      report.confidence_tier === "LOW"
        ? ["Low classification confidence score"]
        : [],
  };
  if (Array.isArray(qcQueue)) {
    qcQueue.unshift(item);
  }
  return { handled: true, item };
}

export function fallbackDeriveDemoQcQueue({ authStore, documentsList }) {
  const offlineDemo =
    isDemoBuild() && authStore?.isDemoMode && !authStore.token;
  if (!offlineDemo) {
    return { handled: false, qcQueue: [] };
  }
  const derived = (documentsList || [])
    .filter((d) => d.status === "TECHNICAL_QC")
    .map((d) => {
      const report =
        d.metadata_json?.document_intelligence ||
        buildLocalIntelligenceReport(d.filename, d.content, d.mime_type);
      return {
        document_id: d.id,
        study_id: d.study_id,
        site_id: d.site_id,
        filename: d.filename,
        status: d.status,
        intelligence_report: report,
        staged_at: d.created_at,
        staged_by: d.created_by,
        qc_assigned_to: null,
        discrepancy_notes:
          report.confidence_tier === "LOW"
            ? ["Low classification confidence score"]
            : [],
      };
    });
  return { handled: true, qcQueue: derived };
}

export function fallbackReviewDemoQcDocument({
  authStore,
  documentsList,
  qcQueue,
  docId,
  decision,
  reason_for_change,
  override_artifact_code,
}) {
  const offlineDemo =
    isDemoBuild() && authStore?.isDemoMode && !authStore.token;
  if (!offlineDemo) {
    return { handled: false, qcQueue, result: null };
  }
  const doc = (documentsList || []).find((d) => d.id === docId);
  if (doc) {
    if (decision === "ACCEPT" || decision === "OVERRIDE") {
      if (override_artifact_code) {
        doc.artifact_code = override_artifact_code;
      }
      doc.status = "APPROVED";
      doc.approval_status = "APPROVED";
    } else if (decision === "REJECT") {
      doc.status = "REJECTED";
      doc.approval_status = "REJECTED";
    }
    doc.reason_for_change = reason_for_change;
  }
  return {
    handled: true,
    qcQueue: (qcQueue || []).filter((i) => i.document_id !== docId),
    result: { success: true },
  };
}

export function fetchDemoDocumentContent({
  authStore,
  etmfService,
  docOrId,
  docId,
  documentsList,
}) {
  const offlineDemo =
    isDemoBuild() && authStore?.isDemoMode && !authStore.token;
  if (
    !offlineDemo ||
    isServiceMethodMocked(etmfService?.downloadDocumentContent)
  ) {
    return { handled: false, result: null };
  }
  const doc =
    (typeof docOrId === "object" ? docOrId : null) ||
    (documentsList || []).find((d) => d.id === docId);
  const mimeType = doc?.mime_type || "application/pdf";
  const bodyText = doc?.content || `Demo document: ${doc?.filename || docId}`;
  const blob = new Blob([bodyText], { type: mimeType });
  return {
    handled: true,
    result: {
      blob,
      mimeType,
      sha256: doc?.content_checksum || doc?.contentChecksum || "",
    },
  };
}
